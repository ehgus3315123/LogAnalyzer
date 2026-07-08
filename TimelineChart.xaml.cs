using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LogAnalyzer
{
    public partial class TimelineChart : UserControl
    {
        public static readonly DependencyProperty TimelineProperty =
            DependencyProperty.Register(nameof(Timeline), typeof(ObservableCollection<TimePoint>),
                typeof(TimelineChart), new PropertyMetadata(null, OnTimelineChanged));

        public ObservableCollection<TimePoint> Timeline
        {
            get => (ObservableCollection<TimePoint>)GetValue(TimelineProperty);
            set => SetValue(TimelineProperty, value);
        }

        private static void OnTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = (TimelineChart)d;
            if (e.OldValue is INotifyCollectionChanged oldColl)
                oldColl.CollectionChanged -= chart.OnCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newColl)
                newColl.CollectionChanged += chart.OnCollectionChanged;
            chart.Redraw();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Redraw();

        public TimelineChart()
        {
            InitializeComponent();
            SizeChanged += (s, e) => Redraw();
        }

        private void Redraw()
        {
            ChartCanvas.Children.Clear();
            var points = Timeline;
            if (points == null || points.Count == 0)
            {
                EmptyLabel.Visibility = Visibility.Visible;
                return;
            }
            EmptyLabel.Visibility = Visibility.Collapsed;

            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight;
            if (w < 10 || h < 10) return;

            const double padLeft = 10, padRight = 10, padTop = 8, padBottom = 22;
            double chartW = w - padLeft - padRight;
            double chartH = h - padTop - padBottom;

            int maxVal = 1;
            foreach (var tp in points) if (tp.Total > maxVal) maxVal = tp.Total;

            var errBrush = TryBrush("ErrorBrush", Colors.Red);
            var wrnBrush = TryBrush("WarnBrush", Colors.Orange);
            var infBrush = TryBrush("InfoBrush", Colors.SteelBlue);
            var mutedBrush = TryBrush("ForegroundMutedBrush", Colors.Gray);

            int n = points.Count;
            double barW = Math.Max(2, (chartW / n) - 2);

            for (int i = 0; i < n; i++)
            {
                var tp = points[i];
                double x = padLeft + i * (chartW / n);
                double totalH = (double)tp.Total / maxVal * chartH;

                double errH = tp.Total > 0 ? (double)tp.ErrorCount / tp.Total * totalH : 0;
                double wrnH = tp.Total > 0 ? (double)tp.WarnCount / tp.Total * totalH : 0;
                double infH = totalH - errH - wrnH;

                double yBottom = padTop + chartH;

                // INFO segment
                if (infH > 0.5)
                    AddRect(x, yBottom - infH, barW, infH, infBrush);
                // WARN segment
                if (wrnH > 0.5)
                    AddRect(x, yBottom - infH - wrnH, barW, wrnH, wrnBrush);
                // ERROR segment
                if (errH > 0.5)
                    AddRect(x, yBottom - infH - wrnH - errH, barW, errH, errBrush);

                // X-axis label (every nth)
                int labelStep = Math.Max(1, n / 8);
                if (i % labelStep == 0)
                {
                    var tb = new TextBlock
                    {
                        Text = points[i].Label,
                        FontSize = 9,
                        Foreground = mutedBrush,
                        RenderTransform = new RotateTransform(-35, 0, 0)
                    };
                    Canvas.SetLeft(tb, x);
                    Canvas.SetTop(tb, yBottom + 3);
                    ChartCanvas.Children.Add(tb);
                }
            }

            // Baseline
            var line = new Line
            {
                X1 = padLeft, Y1 = padTop + chartH,
                X2 = w - padRight, Y2 = padTop + chartH,
                Stroke = mutedBrush, StrokeThickness = 1, Opacity = 0.4
            };
            ChartCanvas.Children.Add(line);
        }

        private void AddRect(double x, double y, double w, double h, Brush brush)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = brush, Opacity = 0.85,
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            ChartCanvas.Children.Add(rect);
        }

        private Brush TryBrush(string key, Color fallback)
        {
            return Application.Current?.TryFindResource(key) as Brush
                   ?? new SolidColorBrush(fallback);
        }
    }
}
