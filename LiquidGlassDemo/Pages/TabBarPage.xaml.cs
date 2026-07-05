using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace LiquidGlassDemo.Pages;

public sealed partial class TabBarPage : Page
{
    private readonly List<RadioButton> _tabs = new();
    private int _selectedIndex;
    private double _dragOriginX;
    private double _dragStartPointerX;
    private bool _dragging;

    public TabBarPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        RotateAnim.Begin();
        PulseAnim.Begin();
        BounceAnim.Begin();

        _tabs.Clear();
        foreach (var child in TabBarGrid.Children)
        {
            if (child is Grid tabGrid)
            {
                foreach (var tabChild in tabGrid.Children)
                {
                    if (tabChild is RadioButton rb)
                        _tabs.Add(rb);
                }
            }
        }

        TabIndicator.SizeChanged += (_, _) => MoveIndicator(_selectedIndex, animated: false);
    }

    // ── tab click ────────────────────────────────────────────────────────

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        var rb = (RadioButton)sender;
        var index = _tabs.IndexOf(rb);
        if (index < 0 || index == _selectedIndex) return;
        _selectedIndex = index;
        MoveIndicator(index, animated: true);
    }

    // ── indicator drag ────────────────────────────────────────────────────

    private void Indicator_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        TabIndicator.CapturePointer(e.Pointer);
        _dragOriginX = IndicatorTransform.X;
        _dragStartPointerX = e.GetCurrentPoint(TabBarGrid).Position.X;
        _dragging = true;
        AnimateScale(to: 1.8, durationMs: 120);
        ReleasedBrush.TransitionTo(PressedBrush, 120);
    }

    private void Indicator_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var posX = e.GetCurrentPoint(TabBarGrid).Position.X;
        var delta = posX - _dragStartPointerX;
        var colW = TabBarGrid.ActualWidth / 4;
        var maxX = colW * 3;
        IndicatorTransform.X = Math.Clamp(_dragOriginX + delta, 0.0, maxX);
    }

    private void Indicator_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_dragging) return;
        var colW = TabBarGrid.ActualWidth / 4;
        var maxX = colW * 3;
        IndicatorTransform.X = Math.Clamp(_dragOriginX + e.Cumulative.Translation.X, 0.0, maxX);
    }

    private void Indicator_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        TabIndicator.ReleasePointerCapture(e.Pointer);
        FinishDrag();
    }

    private void Indicator_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        FinishDrag();
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;

        var colW = TabBarGrid.ActualWidth / 4;
        var centerX = IndicatorTransform.X + TabIndicator.ActualWidth / 2;
        var index = (int)Math.Round(centerX / colW);
        index = Math.Clamp(index, 0, _tabs.Count - 1);

        _selectedIndex = index;
        _tabs[index].IsChecked = true;
        MoveIndicator(index, animated: true);
        AnimateScale(to: 1.0, durationMs: 200);
        ReleasedBrush.TransitionTo(ReleasedBrush, 200);
    }

    // ── scale animation ──────────────────────────────────────────────────

    private void AnimateScale(double to, int durationMs)
    {
        var animX = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animX, IndicatorScale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        var animY = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animY, IndicatorScale);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        var sb = new Storyboard();
        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Begin();
    }

    // ── indicator positioning ─────────────────────────────────────────────

    private void MoveIndicator(int index, bool animated)
    {
        var colW = TabBarGrid.ActualWidth / 4;
        var targetX = colW * index + (colW - TabIndicator.ActualWidth) / 2;

        if (animated)
        {
            var anim = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, IndicatorTransform);
            Storyboard.SetTargetProperty(anim, "X");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
        else
        {
            IndicatorTransform.X = targetX;
        }
    }
}
