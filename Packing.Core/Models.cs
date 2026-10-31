using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Packing.Core {
    public sealed class Figure {
        public int Width { get; }
        public int Height { get; }
        public int Area => Width * Height;

        public Figure(int horizontalSize, int verticalSize) {
            if (!((horizontalSize == 1 && verticalSize == 1) || (horizontalSize == 1 && verticalSize == 2) || (horizontalSize == 2 && verticalSize == 1) || (horizontalSize == 2 && verticalSize == 2) || (horizontalSize == 1 && verticalSize == 3) || (horizontalSize == 3 && verticalSize == 1) || (horizontalSize == 3 && verticalSize == 3))) {
                throw new ArgumentException("Допустимы размеры 1X1, 1X2, 2X1, 2X2, 1X3, 3X1, 3X3");
            }

            Width = horizontalSize;
            Height = verticalSize;
        }
    }

    public sealed class PackingProblem {
        public const int FigureLimit = 300;
        public ReadOnlyCollection<Figure> Figures { get; }
        public int TotalArea { get; }
        public int MinWidth { get; }
        public int MaxWidth { get; }

        public PackingProblem(IEnumerable<Figure> tiles) {
            if (tiles == null) {
                throw new ArgumentNullException(nameof(tiles));
            }

            var entries = new List<Figure>(tiles);
            if (entries.Count == 0 || entries.Count > FigureLimit) {
                throw new ArgumentException("Количество фигур должно быть от 1 до 300");
            }

            int coveredCells = 0, narrowestLane = 0, widestLane = 0;
            foreach (Figure entry in entries) {
                if (entry == null) {
                    throw new ArgumentException("Фигура не может быть null");
                }

                coveredCells += entry.Area;
                narrowestLane = Math.Max(narrowestLane, entry.Width);
                widestLane += entry.Width;
            }

            Figures = entries.AsReadOnly();
            TotalArea = coveredCells;
            MinWidth = narrowestLane;
            MaxWidth = widestLane;
        }
    }

    public sealed class Placement {
        public int FigureIndex { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        internal Placement(int entryNumber, int columnCoordinate, int rowCoordinate, Figure tile) {
            FigureIndex = entryNumber;
            X = columnCoordinate;
            Y = rowCoordinate;
            Width = tile.Width;
            Height = tile.Height;
        }
    }

    public sealed class PackingSolution {
        public ReadOnlyCollection<Placement> Placements { get; }
        public int Width { get; }
        public int Height { get; }
        public int Area => Width * Height;
        public int FigureArea { get; }
        public double Density => FigureArea / (double)Area;
        public bool IsAreaOptimal => Area == FigureArea;

        internal PackingSolution(Placement[] tilePositions, int horizontalSize, int verticalSize, int tileCells) {
            Placements = Array.AsReadOnly((Placement[])tilePositions.Clone());
            Width = horizontalSize;
            Height = verticalSize;
            FigureArea = tileCells;
        }
    }

    public sealed class GeneticSettings {
        public int PopulationSize { get; set; } = 120;
        public int MaxGenerations { get; set; } = 1000;
        public int EliteCount { get; set; } = 4;
        public double MutationProbability { get; set; } = 0.25;
        public int ThreadCount { get; set; } = Math.Min(256, Environment.ProcessorCount);
        public int Seed { get; set; } = 42;

        public void Validate() {
            if (PopulationSize < 8 || PopulationSize > 2000) {
                throw new ArgumentException("Размер популяции: от 8 до 2000.");
            }

            if (MaxGenerations < 1 || MaxGenerations > 1000000) {
                throw new ArgumentException("Число поколений: от 1 до 1000000.");
            }

            if (EliteCount < 1 || EliteCount >= PopulationSize) {
                throw new ArgumentException("Элита: от 1 до размера популяции минус 1.");
            }

            if (double.IsNaN(MutationProbability) || MutationProbability < 0 || MutationProbability > 1) {
                throw new ArgumentException("Вероятность мутации: от 0 до 1.");
            }

            if (ThreadCount < 1 || ThreadCount > 256) {
                throw new ArgumentException("Количество потоков: от 1 до 256.");
            }
        }

        internal GeneticSettings Copy() {
            return (GeneticSettings)MemberwiseClone();
        }
    }

    public sealed class SearchProgress {
        public int Generation { get; }
        public PackingSolution Best { get; }
        public TimeSpan Elapsed { get; }
        public long Evaluations { get; }

        internal SearchProgress(int epochNumber, PackingSolution leadingGenome, TimeSpan runningTime, long decodedCount) {
            Generation = epochNumber;
            Best = leadingGenome;
            Elapsed = runningTime;
            Evaluations = decodedCount;
        }
    }

    public enum StopReason {
        AreaLowerBound,
        GenerationLimit,
        Cancelled
    }

    public sealed class SearchResult {
        public SearchProgress Progress { get; }
        public StopReason Reason { get; }

        internal SearchResult(SearchProgress progressSink, StopReason finishCause) {
            Progress = progressSink;
            Reason = finishCause;
        }
    }
}
