using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Packing.Wpf {
    public sealed class ThreadProfileView : FrameworkElement {
        private readonly List<ThreadMeasurement> timingSamples = new List<ThreadMeasurement>();
        private readonly List<Rect> columnBounds = new List<Rect>();
        private static readonly Brush chartInk = new SolidColorBrush(Color.FromRgb(152, 92, 67));
        public ThreadProfileView() {
            SizeChanged += (eventSource, eventData) => InvalidateVisual();
            MouseMove += OnMouseMove;
            MouseLeave += (eventSource, eventData) => ToolTip = null;
        }

        public void Clear() {
            timingSamples.Clear();
            columnBounds.Clear();
            ToolTip = null;
            InvalidateVisual();
        }

        public void Add(ThreadMeasurement timingSample) {
            int entryNumber = timingSamples.FindIndex(entry => entry.ThreadCount == timingSample.ThreadCount);
            if (entryNumber >= 0) {
                timingSamples[entryNumber] = timingSample;
            } else {
                timingSamples.Add(timingSample);
            }

            timingSamples.Sort((firstChoice, secondChoice) => firstChoice.ThreadCount.CompareTo(secondChoice.ThreadCount));
            InvalidateVisual();
        }

        private void OnMouseMove(object eventSource, MouseEventArgs eventData) {
            Point insertPosition = eventData.GetPosition(this);
            for (int cursorIndex = 0; cursorIndex < columnBounds.Count; cursorIndex++) {
                if (columnBounds[cursorIndex].Contains(insertPosition)) {
                    ThreadMeasurement entry = timingSamples[cursorIndex];
                    ToolTip = string.Format(CultureInfo.GetCultureInfo("ru-RU"), "Потоки: {0}\nВремя: {1:F3} мс\nУскорение: {2:F2}×", entry.ThreadCount, entry.Milliseconds, entry.Speedup);
                    return;
                }
            }

            ToolTip = null;
        }

        private void Text(DrawingContext canvas, string caption, Point insertPosition, bool alignCenter = false) {
            var parsedValue = new FormattedText(caption, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, new SolidColorBrush(Color.FromRgb(117, 108, 98)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (alignCenter) {
                insertPosition.X -= parsedValue.Width / 2;
            }

            canvas.DrawText(parsedValue, insertPosition);
        }

        protected override void OnRender(DrawingContext canvas) {
            base.OnRender(canvas);
            columnBounds.Clear();
            if (timingSamples.Count == 0 || ActualWidth < 120 || ActualHeight < 80) {
                return;
            }

            double highestValue = Math.Max(0.001, timingSamples.Max(entry => entry.Milliseconds));
            double leftEdge = 52, plotTop = 28, plotBottom = ActualHeight - 28;
            double horizontalSize = ActualWidth - leftEdge - 12;
            double verticalSize = plotBottom - plotTop;
            double columnStep = horizontalSize / timingSamples.Count;
            double columnThickness = Math.Min(64, columnStep * 0.55);
            var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(207, 199, 189)), 1);
            canvas.DrawLine(axisPen, new Point(leftEdge, plotTop), new Point(leftEdge, plotBottom));
            canvas.DrawLine(axisPen, new Point(leftEdge, plotBottom), new Point(ActualWidth - 12, plotBottom));
            Text(canvas, "мс", new Point(0, 0));
            Text(canvas, highestValue.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU")), new Point(0, plotTop - 6));
            Text(canvas, "0", new Point(0, plotBottom - 8));
            for (int cursorIndex = 0; cursorIndex < timingSamples.Count; cursorIndex++) {
                ThreadMeasurement entry = timingSamples[cursorIndex];
                double targetCenter = leftEdge + columnStep * (cursorIndex + 0.5);
                double columnHeight = Math.Max(1, entry.Milliseconds / highestValue * verticalSize);
                Rect tileBounds = new Rect(targetCenter - columnThickness / 2, plotBottom - columnHeight, columnThickness, columnHeight);
                columnBounds.Add(tileBounds);
                canvas.DrawRectangle(chartInk, null, tileBounds);
                Text(canvas, entry.Milliseconds.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU")), new Point(targetCenter, tileBounds.Top - 18), true);
                Text(canvas, entry.ThreadCount.ToString(CultureInfo.InvariantCulture), new Point(targetCenter, plotBottom + 5), true);
            }
        }
    }
}
