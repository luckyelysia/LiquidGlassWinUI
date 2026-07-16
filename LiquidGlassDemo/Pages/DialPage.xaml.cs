using System;
using System.Collections.Generic;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LiquidGlassDemo.Pages;

public sealed partial class DialPage : Page
{
    /// <summary>
    /// Each dial button paired with its LiquidGlassBrush and the registration
    /// token for the IsPressed property-change callback (so we can unregister).
    /// </summary>
    private readonly List<(Button Button, LiquidGlassBrush Brush, long Token)> _dialButtons = new();

    public DialPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        FindDialButtons(DialPadGrid);
    }

    // ── discover dial buttons & wire up IsPressed → Exposure ────────────

    private void FindDialButtons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn)
            {
                var brush = FindGlassBrush(btn);
                if (brush != null)
                {
                    // ButtonBase.IsPressed is managed by the VSM CommonStates
                    // group — it tracks Normal / PointerOver / Pressed correctly
                    // including capture-lost and pointer-exit edge cases.
                    var token = btn.RegisterPropertyChangedCallback(
                        ButtonBase.IsPressedProperty,
                        OnDialKeyIsPressedChanged);

                    _dialButtons.Add((btn, brush, token));
                }
            }
            FindDialButtons(child);
        }
    }

    /// <summary>
    /// Walk inside a Button's ControlTemplate to find the Border named
    /// "GlassBorder" and return its LiquidGlassBrush Background.
    /// </summary>
    private static LiquidGlassBrush? FindGlassBrush(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Name: "GlassBorder" } border)
                return border.Background as LiquidGlassBrush;
            var found = FindGlassBrush(child);
            if (found != null) return found;
        }
        return null;
    }

    // ── Exposure animation via compositor-thread animation ───────────────
    //     VSM DoubleAnimation does NOT work for XamlCompositionBrushBase
    //     subclass DPs (confirmed in WinUI 3 AcrylicBrush source — only
    //     CompositionAnimation drives brush properties correctly).

    private static void OnDialKeyIsPressedChanged(DependencyObject d, DependencyProperty dp)
    {
        var btn = (Button)d;

        // Navigate into the applied template to find the named GlassBrush.
        // This is fast — the template tree is shallow.
        var brush = FindGlassBrush(btn);
        if (brush == null) return;

        bool pressed = (bool)btn.GetValue(dp);
        brush.AnimateScalar("Exposure", pressed ? 1.8f : 1f, pressed ? 220 : 150);
    }

    // ── glare follow — continuous mouse tracking; VSM cannot express this ──

    private void DialPad_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(DialPadGrid).Position;

        foreach (var (btn, brush, _) in _dialButtons)
        {
            var transform = btn.TransformToVisual(DialPadGrid);
            var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var cx = origin.X + btn.ActualWidth / 2;
            var cy = origin.Y + btn.ActualHeight / 2;
            var angle = Math.Atan2(pos.Y - cy, pos.X - cx) * 180 / Math.PI;
            brush.GlareAngle = 90 - angle;
        }
    }
}
