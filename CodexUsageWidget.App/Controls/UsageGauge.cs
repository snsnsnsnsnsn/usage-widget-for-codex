using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CodexUsageWidget.App.Controls;

/// <summary>
/// 残量ゲージ。塗り = 残り利用量、縦線 = 残り時間。
/// 塗りが縦線より右に伸びていれば、リセットまで今のペースで持つ（余裕）。左で止まっていれば使いすぎ。
/// </summary>
public sealed class UsageGauge : FrameworkElement
{
    private const double BarHeight = 8;
    private const double MarkerOverhang = 3;
    private const double MarkerWidth = 2;
    private static readonly Brush TrackBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush MarkerBrush = Frozen(Color.FromRgb(0xF4, 0xF7, 0xFB));
    private static readonly Brush GapBrush = Frozen(Color.FromRgb(0x1A, 0x1E, 0x25));

    public static readonly DependencyProperty RemainingProperty = DependencyProperty.Register(
        nameof(Remaining), typeof(double), typeof(UsageGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeRemainingProperty = DependencyProperty.Register(
        nameof(TimeRemaining), typeof(double), typeof(UsageGauge),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(UsageGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>残り利用量（0〜100）。</summary>
    public double Remaining { get => (double)GetValue(RemainingProperty); set => SetValue(RemainingProperty, value); }

    /// <summary>リセットまでの残り時間の割合（0〜100）。不明ならNaNで縦線を出さない。</summary>
    public double TimeRemaining { get => (double)GetValue(TimeRemainingProperty); set => SetValue(TimeRemainingProperty, value); }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(0, BarHeight + MarkerOverhang * 2);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        if (width <= 0) return;

        var radius = BarHeight / 2;
        var track = new Rect(0, MarkerOverhang, width, BarHeight);
        drawingContext.DrawRoundedRectangle(TrackBrush, null, track, radius, radius);

        var fillWidth = width * Math.Clamp(Remaining / 100d, 0d, 1d);
        if (fillWidth > 0)
        {
            var fillRadius = Math.Min(radius, fillWidth / 2);
            drawingContext.DrawRoundedRectangle(Fill, null, new Rect(0, MarkerOverhang, fillWidth, BarHeight), fillRadius, fillRadius);
        }

        if (double.IsNaN(TimeRemaining)) return;
        var x = Math.Clamp(width * Math.Clamp(TimeRemaining / 100d, 0d, 1d), MarkerWidth / 2, width - MarkerWidth / 2);
        var height = BarHeight + MarkerOverhang * 2;
        // 塗りの上でも埋もれないよう、背景色の細い隙間で挟んでから白線を引く。
        drawingContext.DrawRectangle(GapBrush, null, new Rect(x - MarkerWidth / 2 - 1, MarkerOverhang, MarkerWidth + 2, BarHeight));
        drawingContext.DrawRoundedRectangle(MarkerBrush, null, new Rect(x - MarkerWidth / 2, 0, MarkerWidth, height), 1, 1);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
