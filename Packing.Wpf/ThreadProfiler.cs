using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Packing.Core;

namespace Packing.Wpf {
    public sealed class ThreadMeasurement {
        public int ThreadCount { get; }
        public double Milliseconds { get; }
        public double Speedup { get; }
        public long Evaluations { get; }

        public ThreadMeasurement(int parallelLimit, double durationMs, double relativeSpeed, long decodedCount) {
            ThreadCount = parallelLimit;
            Milliseconds = durationMs;
            Speedup = relativeSpeed;
            Evaluations = decodedCount;
        }
    }

    public sealed class ThreadProfiler {
        public IReadOnlyList<ThreadMeasurement> Run(PackingProblem packingTask, GeneticSettings searchOptions, int[] parallelLevels, IProgress<ThreadMeasurement> progressSink, CancellationToken stopSignal) {
            var timingRuns = new List<ThreadMeasurement>();
            double referenceRun = 0;
            foreach (int parallelLimit in parallelLevels) {
                stopSignal.ThrowIfCancellationRequested();
                var parallelOptions = new GeneticSettings {
                    PopulationSize = searchOptions.PopulationSize,
                    MaxGenerations = searchOptions.MaxGenerations,
                    EliteCount = searchOptions.EliteCount,
                    MutationProbability = searchOptions.MutationProbability,
                    ThreadCount = parallelLimit,
                    Seed = searchOptions.Seed
                };
                var packingSearch = new GeneticSolver();
                SearchResult warmupRun = packingSearch.Solve(packingTask, parallelOptions, null, stopSignal);
                stopSignal.ThrowIfCancellationRequested();
                if (warmupRun.Reason == StopReason.Cancelled) {
                    throw new OperationCanceledException(stopSignal);
                }

                double[] elapsedSamples = new double[3];
                long decodedCount = 0;
                for (int cursorIndex = 0; cursorIndex < elapsedSamples.Length; cursorIndex++) {
                    stopSignal.ThrowIfCancellationRequested();
                    Stopwatch elapsedClock = Stopwatch.StartNew();
                    SearchResult searchOutcome = packingSearch.Solve(packingTask, parallelOptions, null, stopSignal);
                    elapsedClock.Stop();
                    stopSignal.ThrowIfCancellationRequested();
                    if (searchOutcome.Reason == StopReason.Cancelled) {
                        throw new OperationCanceledException(stopSignal);
                    }

                    elapsedSamples[cursorIndex] = elapsedClock.Elapsed.TotalMilliseconds;
                    decodedCount = searchOutcome.Progress.Evaluations;
                }

                Array.Sort(elapsedSamples);
                double middleTime = elapsedSamples[1];
                if (parallelLimit == 1) {
                    referenceRun = middleTime;
                }

                var timingSample = new ThreadMeasurement(parallelLimit, middleTime, middleTime > 0 ? referenceRun / middleTime : 1, decodedCount);
                timingRuns.Add(timingSample);
                progressSink?.Report(timingSample);
            }

            return timingRuns.AsReadOnly();
        }
    }
}
