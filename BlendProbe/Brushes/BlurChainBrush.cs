using System;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Effect-chain brush for probe cards 5 and 6. Owns the two-link pipeline
    //   backdrop -> GaussianBlurEffect(Name="Blur") -> <downstream custom effect>
    // and exposes BlurAmount as an animatable dependency property driving
    // "Blur.BlurAmount" on the upstream blur brush. The downstream effect (a plain
    // color-input effect for card 5, or a FlattenSource custom-sampler effect for
    // card 6) is supplied via DownstreamFactory and bound as the blur's consumer.
    //
    // Mirrors CSharpDemo.LiquidGlassBrush's blur->glass wiring but with a swappable
    // terminal effect and no glass parameter surface.
    public sealed class BlurChainBrush : XamlCompositionBrushBase
    {
        // Builds the terminal effect whose single source ("Backdrop") is fed the
        // blurred intermediate. Set before the brush attaches.
        public Func<IGraphicsEffect> DownstreamFactory { get; set; }

        // Upstream blur radius (texels). Bound TwoWay from a slider in BlendPage.
        public static readonly DependencyProperty BlurAmountProperty = DependencyProperty.Register(
            nameof(BlurAmount), typeof(double), typeof(BlurChainBrush),
            new PropertyMetadata(4.0, OnBlurAmountChanged));

        public double BlurAmount
        {
            get => (double)GetValue(BlurAmountProperty);
            set => SetValue(BlurAmountProperty, value);
        }

        private CompositionEffectBrush _blurBrush;
        private CompositionEffectBrush _terminalBrush;

        protected override void OnConnected()
        {
            if (CompositionBrush != null || DownstreamFactory == null)
            {
                return;
            }

            try
            {
                Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();

                // Upstream: backdrop -> Win2D GaussianBlur. Animatable on Blur.BlurAmount.
                GaussianBlurEffect blurEffect = new GaussianBlurEffect
                {
                    Name = "Blur",
                    BlurAmount = (float)BlurAmount,
                    Source = new CompositionEffectSourceParameter("Backdrop"),
                };
                CompositionEffectFactory blurFactory =
                    compositor.CreateEffectFactory(blurEffect, new[] { "Blur.BlurAmount" });
                _blurBrush = blurFactory.CreateBrush();
                _blurBrush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());

                // Downstream: blur -> terminal custom effect. The terminal effect declares
                // its source as "Backdrop"; we feed it the blurred intermediate.
                IGraphicsEffect terminal = DownstreamFactory();
                CompositionEffectFactory terminalFactory = compositor.CreateEffectFactory(terminal);
                _terminalBrush = terminalFactory.CreateBrush();
                _terminalBrush.SetSourceParameter("Backdrop", _blurBrush);

                CompositionBrush = _terminalBrush;
            }
            catch
            {
                CompositionBrush = null;
                _blurBrush = null;
                _terminalBrush = null;
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _blurBrush = null;
            _terminalBrush = null;
        }

        private static void OnBlurAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var brush = (BlurChainBrush)d;
            brush._blurBrush?.Properties.InsertScalar("Blur.BlurAmount", (float)(double)e.NewValue);
        }
    }
}
