using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SmoothFrame.UI.Controls;

public class SmoothScrollViewer : ScrollViewer
{
    private double _targetVerticalOffset;
    private bool _isAnimating;

    public SmoothScrollViewer()
    {
        CanContentScroll = false;
        PanningMode = PanningMode.VerticalOnly;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (ScrollableHeight <= 0)
        {
            base.OnPreviewMouseWheel(e);
            return;
        }

        // Standard mouse wheel delta is typically 120 per notch.
        // Convert to smooth pixel steps: 120 delta -> ~80 pixels scroll.
        double wheelDelta = e.Delta;
        double scrollChange = -(wheelDelta / 120.0) * 80.0;

        double baseOffset = _isAnimating ? _targetVerticalOffset : VerticalOffset;
        _targetVerticalOffset = Math.Clamp(baseOffset + scrollChange, 0, ScrollableHeight);

        AnimateTo(_targetVerticalOffset);
        e.Handled = true;
    }

    private void AnimateTo(double targetOffset)
    {
        _isAnimating = true;

        var animation = new DoubleAnimation
        {
            From = VerticalOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };

        animation.Completed += (s, e) =>
        {
            _isAnimating = false;
        };

        BeginAnimation(SmoothVerticalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static readonly DependencyProperty SmoothVerticalOffsetProperty =
        DependencyProperty.Register(
            nameof(SmoothVerticalOffset),
            typeof(double),
            typeof(SmoothScrollViewer),
            new PropertyMetadata(0.0, OnSmoothVerticalOffsetChanged));

    public double SmoothVerticalOffset
    {
        get => (double)GetValue(SmoothVerticalOffsetProperty);
        set => SetValue(SmoothVerticalOffsetProperty, value);
    }

    private static void OnSmoothVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmoothScrollViewer sv && e.NewValue is double newOffset)
        {
            sv.ScrollToVerticalOffset(newOffset);
        }
    }
}
