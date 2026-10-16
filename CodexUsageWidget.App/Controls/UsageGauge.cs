using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CodexUsageWidget.App.Controls;

/// <summary>
/// 二段ゲージ。上の太いバー = 残り利用量、下の細いバー = リセットまでの残り時間。
/// どちらも実測値で、時間とともに同じ向きへ縮む。上が下より長ければ余裕、短ければ使いすぎ。
/// </summary>
public sealed class UsageGauge : FrameworkElement
{
    private const double UsageHeight = 8;
    private const double Gap = 2;
    private const double TimeHeight = 3;
    private static readonly Brush TrackBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TimeBrush = Frozen(Color.FromArgb(0xB3, 0xC9, 0xD1, 0xDC));

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

    /// <summary>リセットまでの残り時間の割合（0〜100）。不明ならNaNで下のバーを出さない。</summary>
    public double TimeRemaining { get => (double)GetValue(TimeRemainingProperty); set => SetValue(TimeRemainingProperty, value); }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(0, UsageHeight + Gap + TimeHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        if (width <= 0) return;

        DrawBar(drawingContext, 0, UsageHeight, width, Remaining, Fill);
        if (!double.IsNaN(TimeRemaining))
        {
            DrawBar(drawingContext, UsageHeight + Gap, TimeHeight, width, TimeRemaining, TimeBrush);
        }
    }

    private static void DrawBar(DrawingContext drawingContext, double top, double height, double width, double percent, Brush fill)
    {
        var radius = height / 2;
        drawingContext.DrawRoundedRectangle(TrackBrush, null, new Rect(0, top, width, height), radius, radius);
        var fillWidth = width * Math.Clamp(percent / 100d, 0d, 1d);
        if (fillWidth <= 0) return;
        var fillRadius = Math.Min(radius, fillWidth / 2);
        drawingContext.DrawRoundedRectangle(fill, null, new Rect(0, top, fillWidth, height), fillRadius, fillRadius);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
