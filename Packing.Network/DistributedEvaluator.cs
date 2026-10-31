using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Packing.Core;

namespace Packing.Network {
    public sealed class DistributedEvaluator : IPackingEvaluator, IDisposable {
        private readonly ServerRegistry workerDirectory;
        private readonly SemaphoreSlim reservePermit = new SemaphoreSlim(1);
        private readonly JsonSerializerOptions serializedPacket = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private long networkLayoutCount, reserveLayoutCount;
        public int BatchSize { get; }
        public TimeSpan RequestTimeout { get; }
        public bool AllowLocalFallback { get; }
        public long RemoteCandidates => Interlocked.Read(ref networkLayoutCount);
        public long LocalCandidates => Interlocked.Read(ref reserveLayoutCount);

        public DistributedEvaluator(ServerRegistry workerDirectory, int packetCapacity = 16, TimeSpan? packetTimeout = null, bool permitReserve = true) {
            this.workerDirectory = workerDirectory ?? throw new ArgumentNullException(nameof(workerDirectory));
            if (packetCapacity < 1 || packetCapacity > 32) {
                throw new ArgumentOutOfRangeException(nameof(packetCapacity));
            }

            BatchSize = packetCapacity;
            RequestTimeout = packetTimeout ?? TimeSpan.FromSeconds(10);
            if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(2)) {
                throw new ArgumentOutOfRangeException(nameof(packetTimeout));
            }

            AllowLocalFallback = permitReserve;
        }

        public void Evaluate(PackingProblem packingTask, IReadOnlyList<PackingCandidate> genomeTasks, int parallelLimit, Action<int, PackingSolution> reportLayout, CancellationToken stopSignal) {
            var packetJobs = genomeTasks.Chunk(BatchSize).ToArray();
            try {
                Task.WhenAll(packetJobs.Select(workPacket => ProcessBatchAsync(packingTask, workPacket, parallelLimit, reportLayout, stopSignal))).GetAwaiter().GetResult();
            } catch (Exception)when (stopSignal.IsCancellationRequested) {
                throw new OperationCanceledException(stopSignal);
            }
        }

        private async Task ProcessBatchAsync(PackingProblem packingTask, PackingCandidate[] genomeTasks, int parallelLimit, Action<int, PackingSolution> reportLayout, CancellationToken stopSignal) {
            var computeRequest = new BatchRequest {
                JobId = Guid.NewGuid(),
                Threads = parallelLimit,
                Figures = packingTask.Figures.Select(sourceFigure => new FigureDto { Width = sourceFigure.Width, Height = sourceFigure.Height }).ToArray(),
                Candidates = genomeTasks.Select(comparisonScore => new CandidateDto { Index = comparisonScore.Index, Order = comparisonScore.Order, StripWidth = comparisonScore.StripWidth }).ToArray()
            };
            byte[] packetPayload = JsonSerializer.SerializeToUtf8Bytes(computeRequest, serializedPacket);
            var visitedEndpoints = new HashSet<string>();
            int dispatchAttempts = 0;
            while (true) {
                stopSignal.ThrowIfCancellationRequested();
                bool pendingWait = false;
                ServerRegistry.Worker workerEntry = dispatchAttempts < 3 ? workerDirectory.Acquire(visitedEndpoints, out pendingWait) : null;
                if (workerEntry == null) {
                    if (pendingWait) {
                        await workerDirectory.WaitForChangeAsync(stopSignal);
                        continue;
                    }

                    if (!AllowLocalFallback) {
                        throw new HttpRequestException("Нет доступных серверов для пакета " + computeRequest.JobId);
                    }

                    if (!reservePermit.Wait(0)) {
                        await workerDirectory.WaitForChangeAsync(stopSignal);
                        continue;
                    }

                    try {
                        workerDirectory.Record("Локально", computeRequest.JobId.ToString(), "Резервный расчёт: " + genomeTasks.Length);
                        await Task.Run(() => Parallel.ForEach(genomeTasks, new ParallelOptions { MaxDegreeOfParallelism = parallelLimit, CancellationToken = stopSignal }, genomeTask => {
                            PackingSolution packingLayout = BottomLeftDecoder.Decode(packingTask, genomeTask.Order, genomeTask.StripWidth, stopSignal);
                            reportLayout(genomeTask.Index, packingLayout);
                            Interlocked.Increment(ref reserveLayoutCount);
                        }), stopSignal);
                        return;
                    } finally {
                        reservePermit.Release();
                        workerDirectory.Wake();
                    }
                }

                dispatchAttempts++;
                visitedEndpoints.Add(workerEntry.Endpoint);
                Stopwatch elapsedClock = Stopwatch.StartNew();
                string failureText = null;
                bool wasStopped = false;
                bool restoredLayouts = false;
                try {
                    workerDirectory.Record(workerEntry.Endpoint, computeRequest.JobId.ToString(), "Отправка: " + genomeTasks.Length + ", байт: " + packetPayload.Length + ", попытка: " + dispatchAttempts);
                    using var requestStop = CancellationTokenSource.CreateLinkedTokenSource(stopSignal);
                    requestStop.CancelAfter(RequestTimeout);
                    using var bodyContent = new ByteArrayContent(packetPayload);
                    bodyContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    using var activityText = new HttpRequestMessage(HttpMethod.Post, "api/batches") {
                        Content = bodyContent
                    };
                    activityText.Headers.Add("X-Job-Id", computeRequest.JobId.ToString());
                    using HttpResponseMessage httpReply = await workerEntry.Transport.SendAsync(activityText, HttpCompletionOption.ResponseHeadersRead, requestStop.Token);
                    httpReply.EnsureSuccessStatusCode();
                    byte[] bodyBytes = await ReadLimitedAsync(httpReply.Content, requestStop.Token);
                    BatchResponse searchOutcome = JsonSerializer.Deserialize<BatchResponse>(bodyBytes, serializedPacket);
                    PackingSolution[] packetLayouts = ValidateResponse(packingTask, genomeTasks, computeRequest.JobId, searchOutcome);
                    stopSignal.ThrowIfCancellationRequested();
                    for (int cursorIndex = 0; cursorIndex < genomeTasks.Length; cursorIndex++) {
                        reportLayout(genomeTasks[cursorIndex].Index, packetLayouts[cursorIndex]);
                    }

                    Interlocked.Add(ref networkLayoutCount, genomeTasks.Length);
                    restoredLayouts = true;
                    workerDirectory.Record(workerEntry.Endpoint, computeRequest.JobId.ToString(), "Получено байт: " + bodyBytes.Length);
                    return;
                } catch (OperationCanceledException)when (stopSignal.IsCancellationRequested) {
                    wasStopped = true;
                    throw;
                } catch (Exception caughtError)when (caughtError is HttpRequestException || caughtError is OperationCanceledException || caughtError is JsonException || caughtError is InvalidDataException || caughtError is ArgumentException || caughtError is IOException || caughtError is NotSupportedException) {
                    failureText = caughtError is OperationCanceledException ? "Тайм-аут запроса" : caughtError.Message;
                } finally {
                    workerDirectory.Release(workerEntry, computeRequest.JobId.ToString(), elapsedClock.Elapsed.TotalMilliseconds, restoredLayouts ? genomeTasks.Length : 0, failureText, wasStopped);
                }
            }
        }

        private static async Task<byte[]> ReadLimitedAsync(HttpContent bodyContent, CancellationToken abortSignal) {
            const int capacityLimit = 1024 * 1024;
            if (bodyContent.Headers.ContentLength > capacityLimit) {
                throw new InvalidDataException("Слишком большой ответ.");
            }

            using Stream replyStream = await bodyContent.ReadAsStreamAsync(abortSignal);
            using var readBuffer = new MemoryStream();
            byte[] numberPart = new byte[8192];
            while (true) {
                int amount = await replyStream.ReadAsync(numberPart.AsMemory(), abortSignal);
                if (amount == 0) {
                    return readBuffer.ToArray();
                }

                if (readBuffer.Length + amount > capacityLimit) {
                    throw new InvalidDataException("Слишком большой ответ.");
                }

                readBuffer.Write(numberPart, 0, amount);
            }
        }

        private static PackingSolution[] ValidateResponse(PackingProblem packingTask, PackingCandidate[] genomeTasks, Guid packetId, BatchResponse httpReply) {
            if (httpReply == null || httpReply.ProtocolVersion != 1 || httpReply.JobId != packetId || httpReply.Solutions == null || httpReply.Solutions.Length != genomeTasks.Length || httpReply.Solutions.Any(closedSender => closedSender == null) || httpReply.Solutions.Select(closedSender => closedSender.Index).Distinct().Count() != genomeTasks.Length) {
                throw new InvalidDataException("Неверный JobId, версия или состав ответа.");
            }

            var layoutsByNumber = httpReply.Solutions.ToDictionary(closedSender => closedSender.Index);
            var searchOutcome = new PackingSolution[genomeTasks.Length];
            for (int cursorIndex = 0; cursorIndex < genomeTasks.Length; cursorIndex++) {
                SolutionDto wireLayout;
                if (!layoutsByNumber.TryGetValue(genomeTasks[cursorIndex].Index, out wireLayout)) {
                    throw new InvalidDataException("Отсутствует экземпляр ответа");
                }

                PackingSolution packingLayout = SolutionFactory.FromCoordinates(packingTask, wireLayout.X, wireLayout.Y, genomeTasks[cursorIndex].StripWidth);
                if (packingLayout.Width != wireLayout.Width || packingLayout.Height != wireLayout.Height || packingLayout.Area != wireLayout.Area) {
                    throw new InvalidDataException("Метрика ответа не совпадает с координатами.");
                }

                searchOutcome[cursorIndex] = packingLayout;
            }

            return searchOutcome;
        }

        public void Dispose() {
            reservePermit.Dispose();
        }
    }
}
