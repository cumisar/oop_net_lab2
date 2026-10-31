using System;
using System.Diagnostics;
using System.Threading;
using Packing.Network;

namespace Packing.Service {
    public sealed class ServerState {
        private readonly Stopwatch serviceUptime = Stopwatch.StartNew();
        public long FinishedPackets, FaultedPackets, StoppedPackets, DecodedLayouts;
        public int RunningPackets;
        public int MaxThreads { get; }

        public ServerState(int workerParallelLimit) {
            MaxThreads = Math.Clamp(workerParallelLimit, 1, Math.Min(256, Environment.ProcessorCount));
        }

        public ServiceMetrics Snapshot() {
            return new ServiceMetrics {
                Name = Environment.MachineName,
                Processors = Environment.ProcessorCount,
                MaxThreads = MaxThreads,
                Active = Volatile.Read(ref RunningPackets),
                Completed = Interlocked.Read(ref FinishedPackets),
                Failed = Interlocked.Read(ref FaultedPackets),
                Cancelled = Interlocked.Read(ref StoppedPackets),
                Candidates = Interlocked.Read(ref DecodedLayouts),
                UptimeSeconds = serviceUptime.Elapsed.TotalSeconds
            };
        }
    }
}
