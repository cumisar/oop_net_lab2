using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Packing.Core;
using Packing.Network;

namespace Packing.Service {
    public sealed class ServiceBusyException : Exception {
    }

    public class BatchProcessor {
        private readonly SemaphoreSlim computePermit = new SemaphoreSlim(1);
        private readonly ServerState serviceCounters;
        public BatchProcessor(ServerState serviceCounters) {
            this.serviceCounters = serviceCounters;
        }

        public async Task<BatchResponse> ExecuteAsync(BatchRequest computeRequest, CancellationToken stopSignal) {
            PackingProblem packingTask;
            try {
                packingTask = computeRequest.Validate();
            } catch (ArgumentException) {
                Interlocked.Increment(ref serviceCounters.FaultedPackets);
                throw;
            }

            if (!await computePermit.WaitAsync(0, stopSignal)) {
                throw new ServiceBusyException();
            }

            Interlocked.Increment(ref serviceCounters.RunningPackets);
            try {
                BatchResponse httpReply = await ComputeAsync(packingTask, computeRequest, stopSignal);
                Interlocked.Increment(ref serviceCounters.FinishedPackets);
                Interlocked.Add(ref serviceCounters.DecodedLayouts, httpReply.Solutions.Length);
                return httpReply;
            } catch (OperationCanceledException) {
                Interlocked.Increment(ref serviceCounters.StoppedPackets);
                throw;
            } catch {
                Interlocked.Increment(ref serviceCounters.FaultedPackets);
                throw;
            } finally {
                Interlocked.Decrement(ref serviceCounters.RunningPackets);
                computePermit.Release();
            }
        }

        protected virtual Task<BatchResponse> ComputeAsync(PackingProblem packingTask, BatchRequest computeRequest, CancellationToken stopSignal) {
            return Task.Run(() => {
                var timingRuns = new SolutionDto[computeRequest.Candidates.Length];
                Parallel.For(0, timingRuns.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(computeRequest.Threads, serviceCounters.MaxThreads), CancellationToken = stopSignal }, cursorIndex => {
                    CandidateDto entry = computeRequest.Candidates[cursorIndex];
                    PackingSolution packingLayout = BottomLeftDecoder.Decode(packingTask, entry.Order, entry.StripWidth, stopSignal);
                    timingRuns[cursorIndex] = SolutionDto.FromSolution(entry.Index, packingLayout);
                });
                return new BatchResponse {
                    JobId = computeRequest.JobId,
                    Solutions = timingRuns
                };
            }, stopSignal);
        }
    }
}
