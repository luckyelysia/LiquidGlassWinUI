using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace LiquidGlassDemo.Pages;

/// <summary>
/// Exercises driving <see cref="LiquidGlassWinUI.LiquidGlassBrush"/> from XAML
/// <see cref="Storyboard"/>s. See StoryboardPage.xaml for the full write-up.
/// </summary>
public sealed partial class StoryboardPage : Page
{
    private DispatcherQueueTimer? _readoutTimer;

    public StoryboardPage()
    {
        InitializeComponent();
    }

    // ── lifecycle ────────────────────────────────────────────────────────

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Page_Loaded;

        // Kick off everything so the effect is alive on entry.
        BackdropSpin.Begin();
        MorphStoryboard.Begin();
        GlareSpinStoryboard.Begin();

        // Live readout: GetValue returns the storyboard's animated value
        // (animation precedence sits above local value), so this independently
        // confirms the DPs are being driven even if you only watch the numbers.
        _readoutTimer = DispatcherQueue.CreateTimer();
        _readoutTimer.Interval = TimeSpan.FromMilliseconds(50);
        _readoutTimer.Tick += Readout_Tick;
        _readoutTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _readoutTimer?.Stop();
        _readoutTimer = null;
        BackdropSpin.Stop();
        MorphStoryboard.Stop();
        GlareSpinStoryboard.Stop();
    }

    private void Readout_Tick(DispatcherQueueTimer sender, object args)
    {
        RefThicknessText.Text = GlassBrush.RefThickness.ToString("F1");
        GlareAngleText.Text = GlassBrush.GlareAngle.ToString("F0") + "°";
        BlurAmountText.Text = GlassBrush.BlurAmount.ToString("F2");
        TintAText.Text = GlassBrush.TintA.ToString("F2");
        MagnificationText.Text = GlassBrush.Magnification.ToString("F2");
    }

    // ── transport ────────────────────────────────────────────────────────

    private void Begin_Click(object sender, RoutedEventArgs e)
    {
        MorphStoryboard.Begin();
        if (GlareSpinCheck.IsChecked == true) GlareSpinStoryboard.Begin();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        MorphStoryboard.Pause();
        GlareSpinStoryboard.Pause();
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        MorphStoryboard.Resume();
        if (GlareSpinCheck.IsChecked == true) GlareSpinStoryboard.Resume();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        // Stop() releases the hold; properties revert to their base (XAML) values
        // and the changed callbacks fire once with those values.
        MorphStoryboard.Stop();
        GlareSpinStoryboard.Stop();
    }

    // ── speed ────────────────────────────────────────────────────────────

    private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // The Slider's Value="1" fires this synchronously during InitializeComponent,
        // before the later-declared SpeedText / storyboards exist. Bail out until the
        // page is constructed; XAML already supplies the "1.0x" text and SpeedRatio=1.
        if (SpeedText == null || MorphStoryboard == null || GlareSpinStoryboard == null)
            return;

        double ratio = e.NewValue > 0 ? e.NewValue : 1.0;
        MorphStoryboard.SpeedRatio = ratio;
        GlareSpinStoryboard.SpeedRatio = ratio;
        SpeedText.Text = ratio.ToString("0.0") + "x";
    }

    private void GlareSpinCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // Same loading-window guard as the slider: IsChecked="True" may fire this
        // during InitializeComponent before the storyboard field is assigned.
        if (GlareSpinStoryboard == null)
            return;

        if (GlareSpinCheck.IsChecked == true)
            GlareSpinStoryboard.Begin();
        else
            GlareSpinStoryboard.Stop();
    }

    // ── code-built one-shot ──────────────────────────────────────────────
    //
    // A Storyboard constructed in C# targeting a brush DP — proves the same
    // path works programmatically, not just from declared XAML. Magnification
    // is left out of MorphStoryboard precisely so it is free to own here.
    private void Spike_Click(object sender, RoutedEventArgs e)
    {
        var anim = new DoubleAnimation
        {
            From = GlassBrush.Magnification,
            To = 2.2,
            Duration = TimeSpan.FromMilliseconds(280),
            AutoReverse = true,
            EnableDependentAnimation = true, // required: Magnification is a custom DP
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, GlassBrush);
        Storyboard.SetTargetProperty(anim, "Magnification");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }
}
