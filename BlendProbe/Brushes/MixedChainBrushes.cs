using System;
using System.Collections.Generic;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Card 5 — Stage 0 is a single-source non-flatten effect (Backdrop only),
    // Stage 1 is an N=2 FlattenSource effect (Backdrop ← stage 0 output,
    // ColorSrc ← user color). Tests: can a FlattenSource effect materialize the
    // output of a plain color-route effect?
    public sealed class ColorToFlattenChainBrush : XamlCompositionBrushBase
    {
        public Func<IGraphicsEffect> Stage0Factory { get; set; }

        public Func<IGraphicsEffect> Stage1Factory { get; set; }
        public string[] Stage1AnimatablePaths { get; set; }
        public Windows.UI.Color Stage1ColorSrc { get; set; }

        private CompositionEffectBrush _brush0;
        private CompositionEffectBrush _brush1;
        private readonly Dictionary<string, float> _pendingScalars = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || Stage0Factory == null || Stage1Factory == null)
                return;

            try
            {
                Compositor c = CompositionTarget.GetCompositorForCurrentThread();

                // Stage 0: single-source non-flatten effect
                var effect0 = Stage0Factory();
                var factory0 = c.CreateEffectFactory(effect0);
                _brush0 = factory0.CreateBrush();
                _brush0.SetSourceParameter("Backdrop", c.CreateBackdropBrush());

                // Stage 1: FlattenSource effect with "Backdrop" + "ColorSrc"
                var effect1 = Stage1Factory();
                var factory1 = (Stage1AnimatablePaths != null && Stage1AnimatablePaths.Length > 0)
                    ? c.CreateEffectFactory(effect1, Stage1AnimatablePaths)
                    : c.CreateEffectFactory(effect1);
                _brush1 = factory1.CreateBrush();
                _brush1.SetSourceParameter("Backdrop", _brush0);
                _brush1.SetSourceParameter("ColorSrc", c.CreateColorBrush(Stage1ColorSrc));

                CompositionBrush = _brush1;

                foreach (var kv in _pendingScalars)
                    _brush1?.Properties.InsertScalar(kv.Key, kv.Value);
            }
            catch
            {
                CompositionBrush = null;
                _brush0 = null;
                _brush1 = null;
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _brush0 = null;
            _brush1 = null;
        }

        public void SetScalar(string path, float value)
        {
            _pendingScalars[path] = value;
            _brush1?.Properties.InsertScalar(path, value);
        }
    }

    // Card 4 — Stage 0: N=2 FlattenSource (Backdrop+ColorSrc), Stage 1: N1Relay
    // (FlattenSource, keepAsFragmentOutput=false), Stage 2: single-source color
    // effect. The relay "sanitizes" the 0x8 fragment output so a non-flatten
    // downstream effect can consume it.
    public sealed class FlattenRelayToColorChainBrush : XamlCompositionBrushBase
    {
        public Func<IGraphicsEffect> Stage0Factory { get; set; }
        public string[] Stage0AnimatablePaths { get; set; }
        public Windows.UI.Color Stage0ColorSrc { get; set; }

        public Func<IGraphicsEffect> Stage1RelayFactory { get; set; }

        public Func<IGraphicsEffect> Stage2Factory { get; set; }

        private CompositionEffectBrush _brush0;
        private CompositionEffectBrush _brush1;
        private readonly Dictionary<string, float> _pendingScalars = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || Stage0Factory == null || Stage1RelayFactory == null || Stage2Factory == null)
                return;

            try
            {
                Compositor c = CompositionTarget.GetCompositorForCurrentThread();

                // Stage 0: FlattenSource effect with "Backdrop" + "ColorSrc"
                var effect0 = Stage0Factory();
                var factory0 = (Stage0AnimatablePaths != null && Stage0AnimatablePaths.Length > 0)
                    ? c.CreateEffectFactory(effect0, Stage0AnimatablePaths)
                    : c.CreateEffectFactory(effect0);
                _brush0 = factory0.CreateBrush();
                _brush0.SetSourceParameter("Backdrop", c.CreateBackdropBrush());
                _brush0.SetSourceParameter("ColorSrc", c.CreateColorBrush(Stage0ColorSrc));

                // Stage 1: Relay (FlattenSource, single source "Backdrop")
                var effect1 = Stage1RelayFactory();
                var factory1 = c.CreateEffectFactory(effect1);
                _brush1 = factory1.CreateBrush();
                _brush1.SetSourceParameter("Backdrop", _brush0);

                // Stage 2: single-source non-flatten effect, "Backdrop" ← relay output
                var effect2 = Stage2Factory();
                var factory2 = c.CreateEffectFactory(effect2);
                var brush2 = factory2.CreateBrush();
                brush2.SetSourceParameter("Backdrop", _brush1);

                CompositionBrush = brush2;

                foreach (var kv in _pendingScalars)
                    _brush0?.Properties.InsertScalar(kv.Key, kv.Value);
            }
            catch
            {
                CompositionBrush = null;
                _brush0 = null;
                _brush1 = null;
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _brush0 = null;
            _brush1 = null;
        }

        public void SetScalar(string path, float value)
        {
            _pendingScalars[path] = value;
            _brush0?.Properties.InsertScalar(path, value);
        }
    }
}
