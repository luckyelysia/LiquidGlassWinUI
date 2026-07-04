using BlendProbe.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Graphics.Effects;
//using Windows.UI.Composition;

namespace BlendProbe.Pages
{
    // Diagnostic page: renders the RAW backdrop through GaussianBlur(BlurAmount=0)
    // — the simplest possible compositor effect graph — to test whether
    // CreateBackdropBrush has inherent frame latency. If the backdrop lags
    // behind the scene, the overlay content will visibly shift relative to
    // the scrolled content behind it.
    public sealed partial class BackdropLatencyPage : Page
    {
        public BackdropLatencyPage() => InitializeComponent();

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Start compositor-thread animations (same as DemoPage)
            RotateAnim.Begin();
            PulseAnim.Begin();
            BounceAnim.Begin();

            Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();

            var brush = new BackdropEffectBrush
            {
                EffectFactory = () => new GaussianBlurEffect
                {
                    Name = "Passthrough",
                    BlurAmount = 0.0f,               // identity — zero blur = passthrough
                    Source = new CompositionEffectSourceParameter("Backdrop"),
                    Optimization = EffectOptimization.Speed,
                    CacheOutput = true,
                    BorderMode = EffectBorderMode.Hard,
                },
                // No animatable paths — nothing to tweak
                SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                {
                    { "Backdrop", c => c.CreateBackdropBrush() },
                },
            };

            LatencyOverlay.Background = brush;
        }

        private void Overlay_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            OverlayTransform.X += e.Delta.Translation.X;
            OverlayTransform.Y += e.Delta.Translation.Y;
        }
    }
}
