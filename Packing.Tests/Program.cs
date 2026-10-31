using System;
using System.Linq;
using System.Threading;
using Packing.Core;

namespace Packing.Tests {
    internal static class Program {
        private static void Require(bool isValid, string activityText) {
            if (!isValid) {
                throw new InvalidOperationException(activityText);
            }
        }

        private static void CheckLayout(PackingProblem packingTask, PackingSolution packingLayout) {
            Require(packingLayout.Placements.Count == packingTask.Figures.Count, "Количество фигур");
            Require(packingLayout.Area >= packingTask.TotalArea, "Площадь");
            for (int firstPosition = 0; firstPosition < packingLayout.Placements.Count; firstPosition++) {
                var placedTile = packingLayout.Placements[firstPosition];
                Require(placedTile.FigureIndex == firstPosition && placedTile.Width == packingTask.Figures[firstPosition].Width && placedTile.Height == packingTask.Figures[firstPosition].Height, "Размеры и номера фигур");
                Require(placedTile.X >= 0 && placedTile.Y >= 0 && placedTile.X + placedTile.Width <= packingLayout.Width && placedTile.Y + placedTile.Height <= packingLayout.Height, "Границы");
                for (int secondPosition = firstPosition + 1; secondPosition < packingLayout.Placements.Count; secondPosition++) {
                    var adjacentTile = packingLayout.Placements[secondPosition];
                    Require(placedTile.X + placedTile.Width <= adjacentTile.X || adjacentTile.X + adjacentTile.Width <= placedTile.X || placedTile.Y + placedTile.Height <= adjacentTile.Y || adjacentTile.Y + adjacentTile.Height <= placedTile.Y, "Пересечение");
                }
            }
        }

        private static void Main() {
            var sampleProblem = new PackingProblem(new[] { new Figure(1, 1), new Figure(1, 1), new Figure(2, 2), new Figure(2, 2), new Figure(3, 3) });
            var sampleRun = new GeneticSolver().Solve(sampleProblem, new GeneticSettings { MaxGenerations = 20 });
            CheckLayout(sampleProblem, sampleRun.Progress.Best);
            Require(sampleRun.Progress.Best.Area == 20, "Ответ примера");
            Console.WriteLine("PASS: example, bounds, sizes and no overlaps");
            var packingTask = new PackingProblem(new[] { new Figure(3, 3), new Figure(2, 2) });
            var singleThreadRun = new GeneticSolver().Solve(packingTask, new GeneticSettings { MaxGenerations = 5, ThreadCount = 1 });
            var parallelRun = new GeneticSolver().Solve(packingTask, new GeneticSettings { MaxGenerations = 5, ThreadCount = 4 });
            CheckLayout(packingTask, parallelRun.Progress.Best);
            Require(singleThreadRun.Progress.Best.Area == parallelRun.Progress.Best.Area && singleThreadRun.Progress.Best.Placements.Select(placedTile => (placedTile.X, placedTile.Y)).SequenceEqual(parallelRun.Progress.Best.Placements.Select(placedTile => (placedTile.X, placedTile.Y))), "Результат по потокам");
            Console.WriteLine("PASS: one and four threads give the same layout");
            using var searchStop = new CancellationTokenSource();
            searchStop.Cancel();
            var cancelledRun = new GeneticSolver().Solve(packingTask, new GeneticSettings(), stopSignal: searchStop.Token);
            Require(cancelledRun.Reason == StopReason.Cancelled, "Остановка");
            CheckLayout(packingTask, cancelledRun.Progress.Best);
            Console.WriteLine("PASS: cancellation keeps a valid layout");
            Console.WriteLine("PASS: 3 scenarios");
        }
    }
}
