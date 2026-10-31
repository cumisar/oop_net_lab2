using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Packing.Network {
    public sealed class WorkerSnapshot {
        public string Address { get; set; }
        public bool Enabled { get; set; }
        public bool Busy { get; set; }
        public string State { get; set; }
        public long Requests { get; set; }
        public long Completed { get; set; }
        public long Failures { get; set; }
        public long Candidates { get; set; }
        public double Milliseconds { get; set; }
        public string Error { get; set; }
        public int ServerActive { get; set; }
        public long ServerCompleted { get; set; }
    }

    public sealed class NetworkEvent {
        public DateTime Time { get; set; }
        public string Server { get; set; }
        public string JobId { get; set; }
        public string Message { get; set; }
    }

    public sealed class ServerRegistry : IDisposable {
        internal sealed class Worker {
            public string Endpoint;
            public HttpClient Transport;
            public bool SelectedForWork = true, InUse;
            public string Availability = "Не проверен", LastIssue = "";
            public DateTime NextAttemptAt;
            public long SentPackets, FinishedPackets, FaultCount, DecodedLayouts;
            public double LastDuration;
            public int ReportedRunning;
            public long ReportedFinished;
        }

        private readonly object directoryLock = new object ();
        private readonly SemaphoreSlim availabilityChanged = new SemaphoreSlim(0, 1);
        private readonly List<Worker> workerEntries = new List<Worker>();
        private readonly List<Worker> retiredWorkers = new List<Worker>();
        private readonly Queue<NetworkEvent> activityLog = new Queue<NetworkEvent>();
        private readonly Func<Uri, HttpClient> transportFactory;
        private int entryCursor;
        private bool isDisposed;
        public ServerRegistry(Func<Uri, HttpClient> transportFactory = null) {
            this.transportFactory = transportFactory ?? (endpointUri => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2), MaxConnectionsPerServer = 2 }) {
                BaseAddress = endpointUri,
                Timeout = Timeout.InfiniteTimeSpan
            }

            );
        }

        public string Add(string endpointText) {
            string caption = endpointText.Trim();
            if (!caption.Contains("://", StringComparison.Ordinal)) {
                caption = "http://" + caption;
            }

            Uri endpointUri;
            if (!Uri.TryCreate(caption, UriKind.Absolute, out endpointUri) || (endpointUri.Scheme != "http" && endpointUri.Scheme != "https") || endpointUri.AbsolutePath != "/" || endpointUri.Query.Length != 0 || endpointUri.Fragment.Length != 0 || endpointUri.UserInfo.Length != 0 || endpointUri.Port < 1 || endpointUri.Port > 65535) {
                throw new ArgumentException("Адрес: http://IP:порт, без пути и параметров.");
            }

            string canonicalEndpoint = endpointUri.AbsoluteUri;
            lock (directoryLock) {
                ObjectDisposedException.ThrowIf(isDisposed, this);
                if (workerEntries.Any(availableWorker => availableWorker.Endpoint == canonicalEndpoint)) {
                    return canonicalEndpoint;
                }

                HttpClient httpTransport = transportFactory(endpointUri);
                httpTransport.BaseAddress = endpointUri;
                httpTransport.Timeout = Timeout.InfiniteTimeSpan;
                workerEntries.Add(new Worker { Endpoint = canonicalEndpoint, Transport = httpTransport });
                Record(canonicalEndpoint, "", "Добавлен сервер");
            }

            return canonicalEndpoint;
        }

        public void Remove(string endpointText) {
            lock (directoryLock) {
                Worker entry = workerEntries.FirstOrDefault(availableWorker => availableWorker.Endpoint == endpointText);
                if (entry == null) {
                    return;
                }

                entry.SelectedForWork = false;
                workerEntries.Remove(entry);
                retiredWorkers.Add(entry);
                Record(endpointText, "", "Исключён из новых назначений");
            }
        }

        public void SetEnabled(string endpointText, bool useWorker) {
            lock (directoryLock) {
                Worker entry = workerEntries.First(availableWorker => availableWorker.Endpoint == endpointText);
                entry.SelectedForWork = useWorker;
                if (useWorker) {
                    entry.NextAttemptAt = DateTime.MinValue;
                }

                Record(endpointText, "", useWorker ? "Включён" : "Отключён для новых заданий");
            }
        }

        public WorkerSnapshot[] Snapshot() {
            lock (directoryLock)
                return workerEntries.Select(availableWorker => new WorkerSnapshot { Address = availableWorker.Endpoint, Enabled = availableWorker.SelectedForWork, Busy = availableWorker.InUse, State = availableWorker.Availability, Requests = availableWorker.SentPackets, Completed = availableWorker.FinishedPackets, Failures = availableWorker.FaultCount, Candidates = availableWorker.DecodedLayouts, Milliseconds = availableWorker.LastDuration, Error = availableWorker.LastIssue, ServerActive = availableWorker.ReportedRunning, ServerCompleted = availableWorker.ReportedFinished }).ToArray();
        }

        public NetworkEvent[] Events() {
            lock (directoryLock)
                return activityLog.Reverse().ToArray();
        }

        public void Record(string workerEndpoint, string packetId, string activityText) {
            lock (directoryLock) {
                activityLog.Enqueue(new NetworkEvent { Time = DateTime.Now, Server = workerEndpoint, JobId = packetId, Message = activityText });
                while (activityLog.Count > 500) {
                    activityLog.Dequeue();
                }

                if (availabilityChanged.CurrentCount == 0) {
                    availabilityChanged.Release();
                }
            }
        }

        internal Task WaitForChangeAsync(CancellationToken abortSignal) {
            return availabilityChanged.WaitAsync(50, abortSignal);
        }

        internal void Wake() {
            lock (directoryLock)
                if (!isDisposed && availabilityChanged.CurrentCount == 0) {
                    availabilityChanged.Release();
                }
        }

        internal Worker Acquire(HashSet<string> skippedEndpoints, out bool pendingWait) {
            lock (directoryLock) {
                pendingWait = false;
                if (isDisposed) {
                    throw new ObjectDisposedException(nameof(ServerRegistry));
                }

                for (int cursorIndex = 0; cursorIndex < workerEntries.Count; cursorIndex++) {
                    int entryNumber = (entryCursor + cursorIndex) % workerEntries.Count;
                    Worker workerEntry = workerEntries[entryNumber];
                    if (!workerEntry.SelectedForWork || skippedEndpoints.Contains(workerEntry.Endpoint) || workerEntry.NextAttemptAt > DateTime.UtcNow) {
                        continue;
                    }

                    if (workerEntry.InUse) {
                        pendingWait = true;
                        continue;
                    }

                    workerEntry.InUse = true;
                    workerEntry.SentPackets++;
                    workerEntry.Availability = "Вычисление";
                    entryCursor = (entryNumber + 1) % workerEntries.Count;
                    if (workerEntries.Any(availableWorker => availableWorker.SelectedForWork && !availableWorker.InUse && availableWorker.NextAttemptAt <= DateTime.UtcNow) && availabilityChanged.CurrentCount == 0) {
                        availabilityChanged.Release();
                    }

                    return workerEntry;
                }

                return null;
            }
        }

        internal void Release(Worker workerEntry, string packetJob, double durationMs, int genomeTasks, string failureText, bool wasStopped = false) {
            lock (directoryLock) {
                workerEntry.InUse = false;
                workerEntry.LastDuration = durationMs;
                if (wasStopped) {
                    workerEntry.Availability = "Остановлен";
                    Record(workerEntry.Endpoint, packetJob, "Запрос отменён клиентом");
                } else if (failureText == null) {
                    workerEntry.FinishedPackets++;
                    workerEntry.DecodedLayouts += genomeTasks;
                    workerEntry.LastIssue = "";
                    workerEntry.Availability = "Доступен";
                    workerEntry.NextAttemptAt = DateTime.MinValue;
                    Record(workerEntry.Endpoint, packetJob, "Принято экземпляров: " + genomeTasks);
                } else {
                    workerEntry.FaultCount++;
                    workerEntry.LastIssue = failureText;
                    workerEntry.Availability = "Ошибка";
                    workerEntry.NextAttemptAt = DateTime.UtcNow.AddSeconds(3);
                    Record(workerEntry.Endpoint, packetJob, failureText);
                }
            }
        }

        public async Task ProbeAsync(CancellationToken stopSignal = default(CancellationToken)) {
            Worker[] activeEntry;
            lock (directoryLock)
                activeEntry = workerEntries.Where(availableWorker => availableWorker.SelectedForWork).ToArray();
            await Task.WhenAll(activeEntry.Select(async workerEntry => {
                using var waitDuration = CancellationTokenSource.CreateLinkedTokenSource(stopSignal);
                waitDuration.CancelAfter(TimeSpan.FromSeconds(2));
                Stopwatch elapsedClock = Stopwatch.StartNew();
                try {
                    ServiceMetrics serverMetrics = await workerEntry.Transport.GetFromJsonAsync<ServiceMetrics>("health", waitDuration.Token);
                    if (serverMetrics == null || serverMetrics.ProtocolVersion != 1) {
                        throw new HttpRequestException("Несовместимый протокол.");
                    }

                    lock (directoryLock) {
                        workerEntry.ReportedRunning = serverMetrics.Active;
                        workerEntry.ReportedFinished = serverMetrics.Completed;
                        if (!workerEntry.InUse) {
                            workerEntry.Availability = serverMetrics.Active == 0 ? "Доступен" : "Занят";
                            workerEntry.LastIssue = "";
                            workerEntry.LastDuration = elapsedClock.Elapsed.TotalMilliseconds;
                            workerEntry.NextAttemptAt = serverMetrics.Active == 0 ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(1);
                        }
                    }
                } catch (Exception caughtError)when (caughtError is HttpRequestException || caughtError is OperationCanceledException || caughtError is System.Text.Json.JsonException || caughtError is NotSupportedException) {
                    if (stopSignal.IsCancellationRequested) {
                        return;
                    }

                    lock (directoryLock)
                        if (!workerEntry.InUse) {
                            workerEntry.Availability = "Недоступен";
                            workerEntry.LastIssue = caughtError is OperationCanceledException ? "Тайм-аут проверки" : caughtError.Message;
                            workerEntry.NextAttemptAt = DateTime.UtcNow.AddSeconds(3);
                        }
                }
            }));
        }

        public void Dispose() {
            lock (directoryLock) {
                if (isDisposed) {
                    return;
                }

                isDisposed = true;
                foreach (Worker workerEntry in workerEntries.Concat(retiredWorkers)) {
                    workerEntry.Transport.Dispose();
                }
            }
        }
    }
}
