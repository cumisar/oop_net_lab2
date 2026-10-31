using System;
using System.Collections.Generic;
using System.Threading;

namespace Packing.Core {
    public static class BottomLeftDecoder {
        private sealed class Gap {
            public int GapLeft, GapBottom, GapWidth, GapHeight;
            public Gap(int columnCoordinate, int rowCoordinate, int horizontalSize, int verticalSize) {
                GapLeft = columnCoordinate;
                GapBottom = rowCoordinate;
                GapWidth = horizontalSize;
                GapHeight = verticalSize;
            }
        }

        public static PackingSolution Decode(PackingProblem packingTask, int[] tileOrder, int laneLimit, CancellationToken stopSignal = default(CancellationToken)) {
            if (packingTask == null || tileOrder == null) {
                throw new ArgumentNullException(packingTask == null ? nameof(packingTask) : nameof(tileOrder));
            }

            if (tileOrder.Length != packingTask.Figures.Count) {
                throw new ArgumentException("Перестановка должна содержать все фигуры.");
            }

            if (laneLimit < packingTask.MinWidth || laneLimit > packingTask.MaxWidth) {
                throw new ArgumentOutOfRangeException(nameof(laneLimit));
            }

            int[] topContour = new int[laneLimit];
            bool[] alreadyPlaced = new bool[tileOrder.Length];
            Placement[] tilePositions = new Placement[tileOrder.Length];
            var freePockets = new List<Gap>();
            int occupiedWidth = 0, occupiedHeight = 0;
            for (int cursorIndex = 0; cursorIndex < tileOrder.Length; cursorIndex++) {
                stopSignal.ThrowIfCancellationRequested();
                int entryNumber = tileOrder[cursorIndex];
                if (entryNumber < 0 || entryNumber >= tileOrder.Length || alreadyPlaced[entryNumber]) {
                    throw new ArgumentException("Некорректная перестановка.");
                }

                alreadyPlaced[entryNumber] = true;
                Figure tile = packingTask.Figures[entryNumber];
                int chosenColumn = 0, chosenRow = int.MaxValue;
                for (int columnCoordinate = 0; columnCoordinate <= laneLimit - tile.Width; columnCoordinate++) {
                    int rowCoordinate = 0;
                    for (int innerIndex = columnCoordinate; innerIndex < columnCoordinate + tile.Width; innerIndex++) {
                        rowCoordinate = Math.Max(rowCoordinate, topContour[innerIndex]);
                    }

                    if (rowCoordinate < chosenRow) {
                        chosenRow = rowCoordinate;
                        chosenColumn = columnCoordinate;
                    }
                }

                int pocketPosition = -1;
                for (int innerIndex = 0; innerIndex < freePockets.Count; innerIndex++) {
                    Gap freePocket = freePockets[innerIndex];
                    if (freePocket.GapWidth >= tile.Width && freePocket.GapHeight >= tile.Height && (freePocket.GapBottom < chosenRow || (freePocket.GapBottom == chosenRow && freePocket.GapLeft < chosenColumn))) {
                        pocketPosition = innerIndex;
                        chosenColumn = freePocket.GapLeft;
                        chosenRow = freePocket.GapBottom;
                    }
                }

                if (pocketPosition >= 0) {
                    Gap freePocket = freePockets[pocketPosition];
                    freePockets.RemoveAt(pocketPosition);
                    if (freePocket.GapWidth > tile.Width) {
                        freePockets.Add(new Gap(freePocket.GapLeft + tile.Width, freePocket.GapBottom, freePocket.GapWidth - tile.Width, freePocket.GapHeight));
                    }

                    if (freePocket.GapHeight > tile.Height) {
                        freePockets.Add(new Gap(freePocket.GapLeft, freePocket.GapBottom + tile.Height, tile.Width, freePocket.GapHeight - tile.Height));
                    }
                } else {
                    int columnCoordinate = chosenColumn;
                    while (columnCoordinate < chosenColumn + tile.Width) {
                        int leftEdge = columnCoordinate, previousContour = topContour[columnCoordinate];
                        while (columnCoordinate < chosenColumn + tile.Width && topContour[columnCoordinate] == previousContour) {
                            columnCoordinate++;
                        }

                        if (previousContour < chosenRow) {
                            freePockets.Add(new Gap(leftEdge, previousContour, columnCoordinate - leftEdge, chosenRow - previousContour));
                        }
                    }

                    for (int innerIndex = chosenColumn; innerIndex < chosenColumn + tile.Width; innerIndex++) {
                        topContour[innerIndex] = chosenRow + tile.Height;
                    }
                }

                tilePositions[entryNumber] = new Placement(entryNumber, chosenColumn, chosenRow, tile);
                occupiedWidth = Math.Max(occupiedWidth, chosenColumn + tile.Width);
                occupiedHeight = Math.Max(occupiedHeight, chosenRow + tile.Height);
            }

            return new PackingSolution(tilePositions, occupiedWidth, occupiedHeight, packingTask.TotalArea);
        }
    }
}
