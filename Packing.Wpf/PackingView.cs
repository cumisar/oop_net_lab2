using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Packing.Core;

namespace Packing.Wpf {
    public sealed class PackingView : FrameworkElement {
        private PackingSolution packingLayout;
        private static readonly Brush[] pieceTones = {
            new SolidColorBrush(Color.FromRgb(192, 145, 115)),
            new SolidColorBrush(Color.FromRgb(159, 175, 151)),
            new SolidColorBrush(Color.FromRgb(210, 187, 126)),
            new SolidColorBrush(Color.FromRgb(168, 153, 178)),
            new SolidColorBrush(Color.FromRgb(142, 174, 175)),
            new SolidColorBrush(Color.FromRgb(197, 151, 155)),
            new SolidColorBrush(Color.FromRgb(180, 169, 133))
        };
        public PackingSolution Solution {
            get {
                return packingLayout;
            }

            set {
                packingLayout = value;
                InvalidateVisual();
            }
        }

        public PackingView() {
            SizeChanged += (eventSource, eventData) => InvalidateVisual();
        }

        private void Text(DrawingContext canvas, string caption, Point insertPosition, double captionSize) {
            var captionText = new FormattedText(caption, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), captionSize, new SolidColorBrush(Color.FromRgb(117, 108, 98)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            canvas.DrawText(captionText, insertPosition);
        }

        protected override void OnRender(DrawingContext canvas) {
            base.OnRender(canvas);
            if (packingLayout == null || ActualWidth < 80 || ActualHeight < 80) {
                return;
            }

            const double edgeSpace = 32;
            double cellScale = Math.Min((ActualWidth - 2 * edgeSpace) / packingLayout.Width, (ActualHeight - 2 * edgeSpace) / packingLayout.Height);
            double leftEdge = edgeSpace + (ActualWidth - 2 * edgeSpace - packingLayout.Width * cellScale) / 2;
            double plotBottom = ActualHeight - edgeSpace - (ActualHeight - 2 * edgeSpace - packingLayout.Height * cellScale) / 2;
            Rect frameBounds = new Rect(leftEdge, plotBottom - packingLayout.Height * cellScale, packingLayout.Width * cellScale, packingLayout.Height * cellScale);
            canvas.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 241, 236)), null, frameBounds);
            foreach (Placement entry in packingLayout.Placements) {
                Rect tileBounds = new Rect(leftEdge + entry.X * cellScale, plotBottom - (entry.Y + entry.Height) * cellScale, entry.Width * cellScale, entry.Height * cellScale);
                int toneIndex = entry.Width == entry.Height ? entry.Width - 1 : entry.Width == 1 ? (entry.Height == 2 ? 3 : 5) : (entry.Width == 2 ? 4 : 6);
                canvas.DrawRectangle(pieceTones[toneIndex], new Pen(Brushes.White, Math.Min(1, cellScale / 5)), tileBounds);
                if (cellScale >= 22) {
                    string pieceLabel = (entry.FigureIndex + 1).ToString(CultureInfo.InvariantCulture);
                    var captionText = new FormattedText(pieceLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Min(15, cellScale * 0.38), new SolidColorBrush(Color.FromRgb(48, 45, 41)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    canvas.DrawText(captionText, new Point(tileBounds.Left + (tileBounds.Width - captionText.Width) / 2, tileBounds.Top + (tileBounds.Height - captionText.Height) / 2));
                }
            }

            canvas.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(117, 108, 98)), 1), frameBounds);
            Text(canvas, "0", new Point(leftEdge - 16, plotBottom + 4), 12);
            Text(canvas, packingLayout.Width + " → x", new Point(frameBounds.Right - 30, plotBottom + 4), 12);
            Text(canvas, packingLayout.Height + " ↑ y", new Point(leftEdge, frameBounds.Top - 22), 12);
        }
    }
}
