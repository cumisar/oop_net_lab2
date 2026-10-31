using System;
using System.Collections.Generic;
using System.Linq;
using Packing.Core;

namespace Packing.Network {
    public sealed class FigureDto {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class CandidateDto {
        public int Index { get; set; }
        public int[] Order { get; set; }
        public int StripWidth { get; set; }
    }

    public sealed class BatchRequest {
        public int ProtocolVersion { get; set; } = 1;
        public Guid JobId { get; set; }
        public int Threads { get; set; } = 1;
        public FigureDto[] Figures { get; set; }
        public CandidateDto[] Candidates { get; set; }

        public PackingProblem Validate() {
            if (ProtocolVersion != 1 || JobId == Guid.Empty) {
                throw new ArgumentException("Неверная версия протокола или JobId.");
            }

            if (Threads < 1 || Threads > 256) {
                throw new ArgumentException("Threads: от 1 до 256.");
            }

            if (Figures == null || Figures.Length == 0 || Figures.Length > PackingProblem.FigureLimit || Figures.Any(sourceFigure => sourceFigure == null)) {
                throw new ArgumentException("Figures: от 1 до 300 фигур.");
            }

            var packingTask = new PackingProblem(Figures.Select(sourceFigure => new Figure(sourceFigure.Width, sourceFigure.Height)));
            if (Candidates == null || Candidates.Length == 0 || Candidates.Length > 32 || Candidates.Any(comparisonScore => comparisonScore == null)) {
                throw new ArgumentException("Candidates: от 1 до 32 экземпляров.");
            }

            var entryNumbers = new HashSet<int>();
            foreach (CandidateDto entry in Candidates) {
                if (entry.Index < 0 || !entryNumbers.Add(entry.Index) || entry.Order == null || entry.Order.Length != Figures.Length || entry.StripWidth < packingTask.MinWidth || entry.StripWidth > packingTask.MaxWidth) {
                    throw new ArgumentException("Некорректный экземпляр.");
                }

                var alreadyPlaced = new bool[Figures.Length];
                foreach (int entryNumber in entry.Order) {
                    if (entryNumber < 0 || entryNumber >= alreadyPlaced.Length || alreadyPlaced[entryNumber]) {
                        throw new ArgumentException("Некорректная перестановка.");
                    }

                    alreadyPlaced[entryNumber] = true;
                }
            }

            return packingTask;
        }
    }

    public sealed class SolutionDto {
        public int Index { get; set; }
        public int[] X { get; set; }
        public int[] Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Area { get; set; }

        public static SolutionDto FromSolution(int entryNumber, PackingSolution packingLayout) {
            return new SolutionDto {
                Index = entryNumber,
                X = packingLayout.Placements.Select(reportedStep => reportedStep.X).ToArray(),
                Y = packingLayout.Placements.Select(reportedStep => reportedStep.Y).ToArray(),
                Width = packingLayout.Width,
                Height = packingLayout.Height,
                Area = packingLayout.Area
            };
        }
    }

    public sealed class BatchResponse {
        public int ProtocolVersion { get; set; } = 1;
        public Guid JobId { get; set; }
        public SolutionDto[] Solutions { get; set; }
    }

    public sealed class ServiceMetrics {
        public int ProtocolVersion { get; set; } = 1;
        public string Name { get; set; }
        public int Processors { get; set; }
        public int MaxThreads { get; set; }
        public int Active { get; set; }
        public long Completed { get; set; }
        public long Failed { get; set; }
        public long Cancelled { get; set; }
        public long Candidates { get; set; }
        public double UptimeSeconds { get; set; }
    }
}
