using System;

namespace Packing.Core {
    public static class SolutionFactory {
        public static PackingSolution FromCoordinates(PackingProblem packingTask, int[] horizontalCoordinates, int[] verticalCoordinates, int laneLimit) {
            if (packingTask == null || horizontalCoordinates == null || verticalCoordinates == null || horizontalCoordinates.Length != packingTask.Figures.Count || verticalCoordinates.Length != horizontalCoordinates.Length) {
                throw new ArgumentException("Неполный ответ сервера.");
            }

            if (laneLimit < packingTask.MinWidth || laneLimit > packingTask.MaxWidth) {
                throw new ArgumentException("Недопустимая ширина ответа.");
            }

            var tilePositions = new Placement[horizontalCoordinates.Length];
            int horizontalSize = 0, verticalSize = 0;
            for (int cursorIndex = 0; cursorIndex < horizontalCoordinates.Length; cursorIndex++) {
                Figure tile = packingTask.Figures[cursorIndex];
                if (horizontalCoordinates[cursorIndex] < 0 || verticalCoordinates[cursorIndex] < 0 || horizontalCoordinates[cursorIndex] > laneLimit - tile.Width || verticalCoordinates[cursorIndex] > 3 * horizontalCoordinates.Length - tile.Height) {
                    throw new ArgumentException("Некорректные координаты ответа.");
                }

                tilePositions[cursorIndex] = new Placement(cursorIndex, horizontalCoordinates[cursorIndex], verticalCoordinates[cursorIndex], tile);
                horizontalSize = Math.Max(horizontalSize, horizontalCoordinates[cursorIndex] + tile.Width);
                verticalSize = Math.Max(verticalSize, verticalCoordinates[cursorIndex] + tile.Height);
                for (int innerIndex = 0; innerIndex < cursorIndex; innerIndex++) {
                    Placement oppositeTile = tilePositions[innerIndex];
                    if (!(horizontalCoordinates[cursorIndex] + tile.Width <= oppositeTile.X || oppositeTile.X + oppositeTile.Width <= horizontalCoordinates[cursorIndex] || verticalCoordinates[cursorIndex] + tile.Height <= oppositeTile.Y || oppositeTile.Y + oppositeTile.Height <= verticalCoordinates[cursorIndex])) {
                        throw new ArgumentException("Сервер вернул пересекающиеся фигуры.");
                    }
                }
            }

            return new PackingSolution(tilePositions, horizontalSize, verticalSize, packingTask.TotalArea);
        }
    }
}
