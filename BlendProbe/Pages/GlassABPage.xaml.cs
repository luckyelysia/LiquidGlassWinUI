using System;
using System.Collections.Generic;
using BlendProbe.Brushes;
using BlendProbe.Effects;
using BlendProbe.MaskBaking;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.UI;

namespace BlendProbe.Pages
{
    // Glass A/B — the one-shot mask-bake go/no-go. Two glass rects over ONE shared rich
    // backdrop:
    //   left  = GlassRefEffect   (1 source Backdrop -> texture0; every field computed
    //                             inline in float — the reference)
    //   right = GlassBakedEffect (3 sources: Backdrop -> texture0 + Mask0 -> sample1 +
    //                             Mask1 -> sample2; fields decoded from 8-bit baked
    //                             textures — the reconstruction under test)
    // Both refract the same texture0; visual identity => the lossless bake is viable.
    //
    // The baked masks are CPU-baked (GlassFieldBaker) into two CompositionSurfaceBrushes
    // and re-baked on resize / scale change via the Surface-swap pattern (no source
    // re-bind -> no animatable-scalar reset; see memory setsourceparameter-resets-
    // animatable-scalar). The Mode slider drives the baked rect's diagnostic selector.
    public sealed partial class GlassABPage : Page
    {
        private Compositor _compositor;
        private CompositionSurfaceBrush _mask0, _mask1;
        private BackdropEffectBrush _baked;
        private float _scale = 1.0f;

        public GlassABPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _compositor = CompositionTarget.GetCompositorForCurrentThread();

            BuildBackdrop();

            // ---- left: analytic reference (GlassRef) ----
            {
                var effect = new GlassRefEffect();
                GlassRefBorder.Background = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Backdrop", c => c.CreateBackdropBrush() },
                    },
                };
            }

            // ---- right: 8-bit baked (GlassBaked) ----
            _mask0 = _compositor.CreateSurfaceBrush();
            _mask0.Stretch = CompositionStretch.Fill;
            _mask1 = _compositor.CreateSurfaceBrush();
            _mask1.Stretch = CompositionStretch.Fill;

            {
                var effect = new GlassBakedEffect();
                _baked = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    AnimatablePaths = new[] { GlassBakedEffect.ModePropertyPath },
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Backdrop", c => c.CreateBackdropBrush() },
                        { "Mask0", c => _mask0 },
                        { "Mask1", c => _mask1 },
                    },
                };
                _baked.SetScalar(GlassBakedEffect.ModePropertyPath, (float)ModeSlider.Value);
                GlassBakedBorder.Background = _baked;
            }

            // Bake masks now (ActualSize may be 0 pre-layout -> SizeChanged re-bakes).
            ReBake();
            GlassBakedBorder.SizeChanged += (s, args) => { try { ReBake(); } catch { } };
        }

        // Bake both mask textures at the current scale + the baked rect's real size.
        private void ReBake()
        {
            if (_compositor == null || _mask0 == null) return;
            double w = GlassBakedBorder.ActualWidth > 0 ? GlassBakedBorder.ActualWidth : 360;
            double h = GlassBakedBorder.ActualHeight > 0 ? GlassBakedBorder.ActualHeight : 360;
            float dpi = 96f;
            try { dpi = 96f * (float)(GlassBakedBorder.XamlRoot?.RasterizationScale ?? 1.0); } catch { }
            var (s0, s1) = GlassFieldBaker.BakeToSurfaces(_compositor, w, h, _scale, dpi);
            _mask0.Surface = s0;
            _mask1.Surface = s1;
        }

        private void OnScaleChanged(object sender, RoutedEventArgs e)
        {
            if (Scale1 == null) return; // pre-Loaded event
            float s = Scale1.IsChecked == true ? 1.0f : (Scale05.IsChecked == true ? 0.5f : 0.25f);
            if (Math.Abs(s - _scale) < 1e-3) return;
            _scale = s;
            try { ReBake(); } catch { }
        }

        private void OnModeChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _baked?.SetScalar(GlassBakedEffect.ModePropertyPath, (float)ModeSlider.Value);
        }

        // ---- shared rich backdrop (gradient + repeated text + stripes) ------------
        // Strong features so refraction has something to bend; identical behind both glass
        // rects so the A/B differs only in the static-field source.
        private void BuildBackdrop()
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            gradient.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xFF, 0x6A, 0x1B, 0x9A) });
            gradient.GradientStops.Add(new GradientStop { Offset = 0.5, Color = Color.FromArgb(0xFF, 0x0D, 0x7A, 0x84) });
            gradient.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(0xFF, 0x12, 0x4E, 0xB0) });
            BackdropLayer.Background = gradient;

            var grid = new Grid();
            // repeated text rows — high-contrast content for refraction to warp visibly
            string[] lines = { "GLASS A/B · REFRACTION", "液态玻璃 · 折射测试", "BACKDROP SAMPLE", "8-BIT vs FLOAT" };
            for (int r = 0; r < 6; r++)
            {
                var tb = new TextBlock
                {
                    Text = lines[r % lines.Length],
                    FontSize = 11 + (r % 3) * 7,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(0x50 + r * 0x12), 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, (r - 2.5) * 46, 0, 0),
                    IsHitTestVisible = false,
                };
                grid.Children.Add(tb);
            }
            // thin horizontal stripes — high-frequency vertical detail
            for (int i = 0; i < 14; i++)
            {
                grid.Children.Add(new Rectangle
                {
                    Height = i % 2 == 0 ? 2 : 1,
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 20 + i * 32, 0, 0),
                    IsHitTestVisible = false,
                });
            }
            BackdropLayer.Child = grid;
        }
    }
}
