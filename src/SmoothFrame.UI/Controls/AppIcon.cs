using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmoothFrame.UI.Controls;

/// <summary>
/// Lightweight vector icon host (Fluent-style glyphs from Resources/Icons.xaml).
/// Renders a 24x24 geometry scaled to <see cref="Size"/>, stroked or filled with <see cref="Foreground"/>.
/// </summary>
public class AppIcon : Control
{
    static AppIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(typeof(AppIcon)));
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(AppIcon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(AppIcon),
            new PropertyMetadata(16.0));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(AppIcon),
            new PropertyMetadata(1.6));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(AppIcon),
            new PropertyMetadata(false));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>When true the geometry is filled instead of outlined.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }
}
