using System;
using System.Collections.Generic;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LiquidGlassDemo.Pages;

public sealed partial class DialPage : Page
{
    private readonly List<Button> _dialButtons = new();

    public DialPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FindDialButtons(DialPadGrid);
    }

    private void FindDialButtons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn && btn.Background is LiquidGlassBrush)
            {
                _dialButtons.Add(btn);

                // handledEventsToo: true — Button handles PointerPressed internally
                // (for Click + visual state), so the default CLR event doesn't fire.
                btn.AddHandler(
                    UIElement.PointerPressedEvent,
                    new PointerEventHandler(DialKey_PointerPressed),
                    handledEventsToo: true);

                btn.AddHandler(
                    UIElement.PointerReleasedEvent,
                    new PointerEventHandler(DialKey_PointerReleased),
                    handledEventsToo: true);

                btn.PointerExited += DialKey_PointerExited;
                btn.PointerCaptureLost += DialKey_PointerCaptureLost;
            }
            FindDialButtons(child);
        }
    }

    // ── pointer events — animate tint via compositor-thread animation ────

    private static void DialKey_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (((Button)sender).Background is LiquidGlassBrush brush)
            brush.AnimateScalar("Exposure", 1.8f, 220);
    }

    private static void DialKey_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (((Button)sender).Background is LiquidGlassBrush brush)
            brush.AnimateScalar("Exposure", 1, 150);
    }

    private static void DialKey_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (((Button)sender).Background is LiquidGlassBrush brush)
            brush.AnimateScalar("Exposure", 1, 150);
    }

    private static void DialKey_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (((Button)sender).Background is LiquidGlassBrush brush)
            brush.AnimateScalar("Exposure", 1, 150);
    }

    // ── glare follow ────────────────────────────────────────────────────

    private void DialPad_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(DialPadGrid).Position;

        foreach (var btn in _dialButtons)
        {
            if (btn.Background is not LiquidGlassBrush brush) continue;

            var transform = btn.TransformToVisual(DialPadGrid);
            var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var cx = origin.X + btn.ActualWidth / 2;
            var cy = origin.Y + btn.ActualHeight / 2;
            var angle = Math.Atan2(pos.Y - cy, pos.X - cx) * 180 / Math.PI;
            brush.GlareAngle = 90 - angle;
        }
    }
}
