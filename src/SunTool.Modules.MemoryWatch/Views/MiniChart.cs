using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SunTool.Modules.MemoryWatch.Logic;

namespace SunTool.Modules.MemoryWatch.Views;

/// <summary>
/// 시간에 따른 값을 선으로 그리는 작은 그래프. 외부 라이브러리 없이 OnRender 로 그립니다.
/// 점이 많으면 가로 픽셀마다 최저·최고를 남겨 줄여서 그립니다 (<see cref="Downsampler"/>).
/// 프로그램 종료·다시 시작은 세로 점선으로 표시합니다.
/// </summary>
public sealed class MiniChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = Register(nameof(Samples));
    public static readonly DependencyProperty MetricsProperty = Register(nameof(Metrics));
    public static readonly DependencyProperty ColorsProperty = Register(nameof(Colors));
    public static readonly DependencyProperty EventsProperty = Register(nameof(Events));
    public static readonly DependencyProperty RevisionProperty = Register(nameof(Revision));

    private static readonly Brush GridBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));
    private static readonly Pen GridPen = Frozen(new Pen(GridBrush, 1));
    private static readonly Pen EventPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xD1, 0x24, 0x2F))), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen RestartPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37))), 1) { DashStyle = DashStyles.Dash });
    private static readonly Typeface LabelFont = new("Segoe UI");

    /// <summary>IReadOnlyList&lt;MemorySample&gt;.</summary>
    public object? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    /// <summary>MetricKind[] (그릴 값).</summary>
    public object? Metrics { get => GetValue(MetricsProperty); set => SetValue(MetricsProperty, value); }

    /// <summary>string[] (선 색, "#RRGGBB").</summary>
    public object? Colors { get => GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }

    /// <summary>IReadOnlyList&lt;WatchEvent&gt;.</summary>
    public object? Events { get => GetValue(EventsProperty); set => SetValue(EventsProperty, value); }

    /// <summary>바뀌면 다시 그립니다.</summary>
    public object? Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(object), typeof(MiniChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (width < 80 || height < 50 || Samples is not IReadOnlyList<MemorySample> samples || Metrics is not MetricKind[] metrics || samples.Count == 0)
        {
            DrawMessage(dc, Loc.T("기록이 없습니다"), new Point(width / 2, height / 2));
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double left = 62;
        const double bottom = 18;
        var plot = new Rect(left, 6, Math.Max(10, width - left - 8), Math.Max(10, height - bottom - 10));
        var colors = Colors as string[] ?? ["#2F6FEB", "#E8912D"];

        var series = metrics.Select(m => Downsampler.Reduce(samples, m, (int)plot.Width)).ToList();
        var all = series.SelectMany(s => s).ToList();
        if (all.Count == 0)
        {
            DrawMessage(dc, Loc.T("읽을 수 없음 (권한 부족)"), new Point(width / 2, height / 2));
            return;
        }

        var t0 = samples[0].Time;
        var t1 = samples[^1].Time;
        if (Events is IReadOnlyList<WatchEvent> evs && evs.Count > 0 && evs[^1].Time > t1)
        {
            t1 = evs[^1].Time;
        }

        var spanTicks = Math.Max(TimeSpan.TicksPerSecond, (t1 - t0).Ticks);
        var min = all.Min(p => p.Value);
        var max = all.Max(p => p.Value);
        NiceRange(ref min, ref max, metrics[0]);

        double X(DateTime t) => plot.Left + plot.Width * (t - t0).Ticks / spanTicks;
        double Y(double v) => plot.Bottom - plot.Height * (v - min) / (max - min);

        // 가로 눈금 3줄 + 값
        for (var i = 0; i <= 2; i++)
        {
            var v = min + (max - min) * i / 2;
            var y = Y(v);
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, AxisText(metrics[0], v), new Point(plot.Left - 6, y), TextAlignment.Right, center: true);
        }

        // 시간 (시작 · 끝)
        DrawText(dc, t0.ToString("HH:mm:ss", CultureInfo.InvariantCulture), new Point(plot.Left, plot.Bottom + 2), TextAlignment.Left);
        DrawText(dc, t1.ToString(t1.Date == t0.Date ? "HH:mm:ss" : "MM-dd HH:mm", CultureInfo.InvariantCulture), new Point(plot.Right, plot.Bottom + 2), TextAlignment.Right);

        // 종료·다시 시작 표시
        if (Events is IReadOnlyList<WatchEvent> events)
        {
            foreach (var e in events.Skip(1))
            {
                var x = X(e.Time);
                if (x >= plot.Left && x <= plot.Right)
                {
                    dc.DrawLine(e.IsRestart ? RestartPen : EventPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                }
            }
        }

        // 선 (나중 것이 아래에 깔리도록 뒤에서부터)
        for (var s = series.Count - 1; s >= 0; s--)
        {
            var points = series[s];
            if (points.Count == 0)
            {
                continue;
            }

            var brush = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[s % colors.Length])));
            var pen = Frozen(new Pen(brush, s == 0 ? 1.6 : 1.2) { LineJoin = PenLineJoin.Round });
            // 프로그램이 다시 켜진 곳에서는 선을 끊음 (꺼져 있던 동안을 잇지 않도록)
            var breaks = (Events as IReadOnlyList<WatchEvent>)?.Where(e => e.IsRestart).Select(e => e.Time).ToList() ?? [];
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var run = new List<Point>();
                void Flush()
                {
                    if (run.Count == 0)
                    {
                        return;
                    }

                    ctx.BeginFigure(run[0], false, false);
                    if (run.Count == 1)
                    {
                        ctx.LineTo(new Point(run[0].X + 1, run[0].Y), true, false);
                    }
                    else
                    {
                        ctx.PolyLineTo(run.Skip(1).ToList(), true, true);
                    }

                    run.Clear();
                }

                for (var i = 0; i < points.Count; i++)
                {
                    if (i > 0 && breaks.Any(b => b > points[i - 1].Time && b <= points[i].Time))
                    {
                        Flush();
                    }

                    run.Add(new Point(X(points[i].Time), Y(points[i].Value)));
                }

                Flush();
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        void DrawText(DrawingContext d, string text, Point at, TextAlignment align, bool center = false)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, 10.5, LabelBrush, dpi) { TextAlignment = align };
            d.DrawText(ft, new Point(at.X, center ? at.Y - ft.Height / 2 : at.Y));
        }
    }

    private void DrawMessage(DrawingContext dc, string text, Point center)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, 12, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = TextAlignment.Center };
        dc.DrawText(ft, new Point(center.X, center.Y - ft.Height / 2));
    }

    /// <summary>위아래 여유를 두고, 값이 하나뿐이어도 선이 가운데 보이도록 범위를 잡습니다.</summary>
    private static void NiceRange(ref double min, ref double max, MetricKind metric)
    {
        // 값의 10% 보다 좁게 확대하지 않음: 안정된 값의 작은 출렁임이 크게 보여 오해하지 않도록
        var floor = metric switch
        {
            MetricKind.PagedPool or MetricKind.NonPagedPool => 64 * 1024,
            _ when MetricInfo.IsBytes(metric) => 1024 * 1024,
            _ => 10,
        };
        var minSpan = Math.Max(floor, Math.Abs(max) * 0.10);
        if (max - min < minSpan)
        {
            var mid = (max + min) / 2;
            min = mid - minSpan / 2.0;
            max = mid + minSpan / 2.0;
        }

        var pad = (max - min) * 0.08;
        min = Math.Max(0, min - pad);
        max += pad;
    }

    private static string AxisText(MetricKind metric, double value) =>
        !MetricInfo.IsBytes(metric) ? $"{value:N0}"
        : metric is MetricKind.PagedPool or MetricKind.NonPagedPool && value < 10 * 1024 * 1024 ? $"{value / 1024:N0} KB"
        : value >= 10.0 * 1024 * 1024 * 1024 ? $"{value / (1024.0 * 1024 * 1024):N0} GB"
        : $"{value / (1024 * 1024):N0} MB";
}
