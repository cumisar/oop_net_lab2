using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Packing.Core {
    public sealed class GeneticSolver {
        private sealed class Individual {
            public int[] PieceSequence;
            public int LaneWidth;
            public PackingSolution DecodedLayout;
        }

        private static int Compare(Individual firstChoice, Individual secondChoice) {
            int comparisonScore = firstChoice.DecodedLayout.Area.CompareTo(secondChoice.DecodedLayout.Area);
            if (comparisonScore != 0) {
                return comparisonScore;
            }

            comparisonScore = (firstChoice.DecodedLayout.Width + firstChoice.DecodedLayout.Height).CompareTo(secondChoice.DecodedLayout.Width + secondChoice.DecodedLayout.Height);
            if (comparisonScore != 0) {
                return comparisonScore;
            }

            comparisonScore = firstChoice.LaneWidth.CompareTo(secondChoice.LaneWidth);
            if (comparisonScore != 0) {
                return comparisonScore;
            }

            for (int cursorIndex = 0; cursorIndex < firstChoice.PieceSequence.Length; cursorIndex++) {
                comparisonScore = firstChoice.PieceSequence[cursorIndex].CompareTo(secondChoice.PieceSequence[cursorIndex]);
                if (comparisonScore != 0) {
                    return comparisonScore;
                }
            }

            return 0;
        }

        private static Random CreateRandom(int randomSeed, int epochNumber, int entryNumber) {
            unchecked {
                uint parsedValue = (uint)randomSeed ^ ((uint)epochNumber * 0x9E3779B9u) ^ ((uint)entryNumber * 0x85EBCA6Bu);
                parsedValue ^= parsedValue >> 16;
                parsedValue *= 0x7FEB352Du;
                parsedValue ^= parsedValue >> 15;
                parsedValue *= 0x846CA68Bu;
                parsedValue ^= parsedValue >> 16;
                return new Random((int)parsedValue);
            }
        }

        private static void Shuffle(int[] tileOrder, Random geneRandom) {
            for (int cursorIndex = tileOrder.Length - 1; cursorIndex > 0; cursorIndex--) {
                int innerIndex = geneRandom.Next(cursorIndex + 1);
                int swappedEntry = tileOrder[cursorIndex];
                tileOrder[cursorIndex] = tileOrder[innerIndex];
                tileOrder[innerIndex] = swappedEntry;
            }
        }

        private static int RandomWidth(PackingProblem packingTask, Random geneRandom) {
            if (geneRandom.NextDouble() < 0.25) {
                return geneRandom.Next(packingTask.MinWidth, packingTask.MaxWidth + 1);
            }

            int targetCenter = (int)Math.Ceiling(Math.Sqrt(packingTask.TotalArea));
            return geneRandom.Next(packingTask.MinWidth, Math.Max(packingTask.MinWidth, Math.Min(packingTask.MaxWidth, targetCenter * 2)) + 1);
        }

        private static Individual Tournament(Individual[] genomePool, Random geneRandom) {
            Individual leadingGenome = genomePool[geneRandom.Next(genomePool.Length)];
            for (int cursorIndex = 1; cursorIndex < 3; cursorIndex++) {
                Individual upcomingEntries = genomePool[geneRandom.Next(genomePool.Length)];
                if (Compare(upcomingEntries, leadingGenome) < 0) {
                    leadingGenome = upcomingEntries;
                }
            }

            return leadingGenome;
        }

        private static int[] Crossover(int[] firstChoice, int[] secondChoice, Random geneRandom) {
            int sequenceLength = firstChoice.Length;
            int leftEdge = geneRandom.Next(sequenceLength), rightEdge = geneRandom.Next(leftEdge, sequenceLength);
            int[] offspring = Enumerable.Repeat(-1, sequenceLength).ToArray();
            bool[] alreadyPlaced = new bool[sequenceLength];
            for (int cursorIndex = leftEdge; cursorIndex <= rightEdge; cursorIndex++) {
                offspring[cursorIndex] = firstChoice[cursorIndex];
                alreadyPlaced[firstChoice[cursorIndex]] = true;
            }

            int insertPosition = (rightEdge + 1) % sequenceLength;
            for (int cursorIndex = 0; cursorIndex < sequenceLength; cursorIndex++) {
                int parsedValue = secondChoice[(rightEdge + 1 + cursorIndex) % sequenceLength];
                if (alreadyPlaced[parsedValue]) {
                    continue;
                }

                offspring[insertPosition] = parsedValue;
                insertPosition = (insertPosition + 1) % sequenceLength;
            }

            return offspring;
        }

        private static void Mutate(Individual entry, PackingProblem packingTask, Random geneRandom) {
            int firstChoice = geneRandom.Next(entry.PieceSequence.Length), secondChoice = geneRandom.Next(entry.PieceSequence.Length);
            if (geneRandom.Next(2) == 0) {
                int swappedEntry = entry.PieceSequence[firstChoice];
                entry.PieceSequence[firstChoice] = entry.PieceSequence[secondChoice];
                entry.PieceSequence[secondChoice] = swappedEntry;
            } else {
                Array.Reverse(entry.PieceSequence, Math.Min(firstChoice, secondChoice), Math.Abs(firstChoice - secondChoice) + 1);
            }

            int widthDelta = Math.Max(1, entry.LaneWidth / 8);
            entry.LaneWidth = geneRandom.NextDouble() < 0.15 ? RandomWidth(packingTask, geneRandom) : Math.Clamp(entry.LaneWidth + geneRandom.Next(-widthDelta, widthDelta + 1), packingTask.MinWidth, packingTask.MaxWidth);
        }

        public SearchResult Solve(PackingProblem packingTask, GeneticSettings searchOptions, IProgress<SearchProgress> progressSink = null, CancellationToken stopSignal = default(CancellationToken), IPackingEvaluator layoutExecutor = null) {
            if (packingTask == null || searchOptions == null) {
                throw new ArgumentNullException(packingTask == null ? nameof(packingTask) : nameof(searchOptions));
            }

            searchOptions = searchOptions.Copy();
            searchOptions.Validate();
            Stopwatch elapsedClock = Stopwatch.StartNew();
            long decodedCount = 0;
            int epochNumber = 0, idleEpochs = 0;
            double lastUpdateAt = 0;
            int[] areaOrder = Enumerable.Range(0, packingTask.Figures.Count).OrderByDescending(cursorIndex => packingTask.Figures[cursorIndex].Area).ThenBy(cursorIndex => cursorIndex).ToArray();
            Individual leadingGenome = new Individual {
                PieceSequence = areaOrder,
                LaneWidth = Math.Clamp((int)Math.Ceiling(Math.Sqrt(packingTask.TotalArea)), packingTask.MinWidth, packingTask.MaxWidth)
            };
            leadingGenome.DecodedLayout = BottomLeftDecoder.Decode(packingTask, leadingGenome.PieceSequence, leadingGenome.LaneWidth);
            decodedCount++;
            SearchProgress currentSnapshot = new SearchProgress(0, leadingGenome.DecodedLayout, elapsedClock.Elapsed, decodedCount);
            progressSink?.Report(currentSnapshot);
            Individual[] genomePool = null;
            StopReason finishCause = StopReason.GenerationLimit;
            var parallelOptions = new ParallelOptions {
                MaxDegreeOfParallelism = searchOptions.ThreadCount,
                CancellationToken = stopSignal
            };
            while (epochNumber < searchOptions.MaxGenerations) {
                if (stopSignal.IsCancellationRequested) {
                    finishCause = StopReason.Cancelled;
                    break;
                }

                if (leadingGenome.DecodedLayout.IsAreaOptimal) {
                    finishCause = StopReason.AreaLowerBound;
                    break;
                }

                int upcomingEpoch = epochNumber + 1;
                Individual[] upcomingEntries = new Individual[searchOptions.PopulationSize];
                Individual[] pendingGenomes = layoutExecutor == null ? null : new Individual[upcomingEntries.Length];
                Individual[] parentPool = genomePool;
                bool wasStopped = false;
                double mutationChance = Math.Min(1, searchOptions.MutationProbability * (idleEpochs >= 30 ? 2 : 1));
                try {
                    Parallel.For(0, upcomingEntries.Length, parallelOptions, cursorIndex => {
                        Random geneRandom = CreateRandom(searchOptions.Seed, upcomingEpoch, cursorIndex);
                        Individual offspring;
                        if (parentPool == null) {
                            offspring = new Individual {
                                PieceSequence = (int[])areaOrder.Clone()
                            };
                            if (cursorIndex % 3 == 1) {
                                Array.Reverse(offspring.PieceSequence);
                            }

                            if (cursorIndex % 3 == 2) {
                                Shuffle(offspring.PieceSequence, geneRandom);
                            }

                            offspring.LaneWidth = cursorIndex < upcomingEntries.Length / 2 ? Math.Min(packingTask.MaxWidth, packingTask.MinWidth + cursorIndex) : RandomWidth(packingTask, geneRandom);
                            if (cursorIndex == 0) {
                                offspring = leadingGenome;
                            }
                        } else if (cursorIndex < searchOptions.EliteCount) {
                            upcomingEntries[cursorIndex] = parentPool[cursorIndex];
                            return;
                        } else if (cursorIndex >= upcomingEntries.Length - Math.Max(1, upcomingEntries.Length / 20)) {
                            offspring = new Individual {
                                PieceSequence = (int[])areaOrder.Clone(),
                                LaneWidth = RandomWidth(packingTask, geneRandom)
                            };
                            Shuffle(offspring.PieceSequence, geneRandom);
                        } else {
                            Individual firstChoice = Tournament(parentPool, geneRandom), secondChoice = Tournament(parentPool, geneRandom);
                            offspring = new Individual {
                                PieceSequence = Crossover(firstChoice.PieceSequence, secondChoice.PieceSequence, geneRandom),
                                LaneWidth = geneRandom.Next(2) == 0 ? firstChoice.LaneWidth : secondChoice.LaneWidth
                            };
                            if (geneRandom.NextDouble() < mutationChance) {
                                Mutate(offspring, packingTask, geneRandom);
                            }
                        }

                        if (layoutExecutor != null && offspring.DecodedLayout == null) {
                            pendingGenomes[cursorIndex] = offspring;
                            return;
                        }

                        if (offspring.DecodedLayout == null) {
                            offspring.DecodedLayout = BottomLeftDecoder.Decode(packingTask, offspring.PieceSequence, offspring.LaneWidth, stopSignal);
                            Interlocked.Increment(ref decodedCount);
                        }

                        upcomingEntries[cursorIndex] = offspring;
                    });
                    if (layoutExecutor != null) {
                        PackingCandidate[] genomeTasks = Enumerable.Range(0, pendingGenomes.Length).Where(cursorIndex => pendingGenomes[cursorIndex] != null).Select(cursorIndex => new PackingCandidate(cursorIndex, pendingGenomes[cursorIndex].PieceSequence, pendingGenomes[cursorIndex].LaneWidth)).ToArray();
                        layoutExecutor.Evaluate(packingTask, genomeTasks, searchOptions.ThreadCount, (entryNumber, packingLayout) => {
                            pendingGenomes[entryNumber].DecodedLayout = packingLayout;
                            Interlocked.Increment(ref decodedCount);
                            upcomingEntries[entryNumber] = pendingGenomes[entryNumber];
                        }, stopSignal);
                    }
                } catch (OperationCanceledException) {
                    wasStopped = true;
                }

                Individual previousLeader = leadingGenome;
                foreach (Individual entry in upcomingEntries) {
                    if (entry != null && Compare(entry, leadingGenome) < 0) {
                        leadingGenome = entry;
                    }
                }

                if (!wasStopped) {
                    Array.Sort(upcomingEntries, Compare);
                    genomePool = upcomingEntries;
                    epochNumber = upcomingEpoch;
                    idleEpochs = leadingGenome.DecodedLayout.Area < previousLeader.DecodedLayout.Area ? 0 : idleEpochs + 1;
                }

                currentSnapshot = new SearchProgress(epochNumber, leadingGenome.DecodedLayout, elapsedClock.Elapsed, decodedCount);
                if (wasStopped || epochNumber == 1 || elapsedClock.Elapsed.TotalMilliseconds - lastUpdateAt >= 100 || leadingGenome.DecodedLayout.IsAreaOptimal) {
                    progressSink?.Report(currentSnapshot);
                    lastUpdateAt = elapsedClock.Elapsed.TotalMilliseconds;
                }

                if (wasStopped) {
                    finishCause = StopReason.Cancelled;
                    break;
                }
            }

            if (finishCause != StopReason.Cancelled && leadingGenome.DecodedLayout.IsAreaOptimal) {
                finishCause = StopReason.AreaLowerBound;
            }

            currentSnapshot = new SearchProgress(epochNumber, leadingGenome.DecodedLayout, elapsedClock.Elapsed, decodedCount);
            progressSink?.Report(currentSnapshot);
            return new SearchResult(currentSnapshot, finishCause);
        }
    }
}
