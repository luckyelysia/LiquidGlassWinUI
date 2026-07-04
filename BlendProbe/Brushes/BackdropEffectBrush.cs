using System;
using System.Collections.Generic;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Generic backdrop-effect brush for the BlendProbe probe cards. Built imperatively
    // in BlendPage code-behind (it is never declared in XAML): the page sets
    // EffectFactory + (optional) AnimatablePaths + SourceBinders, then assigns the
    // brush to a preview Border's Background. OnConnected (when the brush attaches)
    // builds compositor.CreateEffectFactory(effect, paths) -> CreateBrush() and binds
    // each named source via SetSourceParameter. try/catch -> null keeps one failing
    // card from crashing the page (same pattern as CSharpDemo.LiquidGlassBrush).
    //
    // This covers the six single-effect cards (1,2,3,4,7,8). The two blur-chain
    // cards (5,6) use BlurChainBrush instead, because they own an upstream
    // GaussianBlur whose BlurAmount must be animatable.
    public sealed class BackdropEffectBrush : XamlCompositionBrushBase
    {
        // Builds the terminal IGraphicsEffect. Its source parameters are resolved by
        // SourceBinders. Set before the brush is assigned to a target.
        public Func<IGraphicsEffect> EffectFactory { get; set; }

        // Animatable effect-property paths handed to CreateEffectFactory (null/empty =>
        // no animatable properties). e.g. { "TwoColorBlendEffect.Factor" }.
        public string[] AnimatablePaths { get; set; }

        // Source-parameter name -> provider that returns the CompositionBrush for that
        // name from the live Compositor. Most cards bind {"Backdrop", c => c.CreateBackdropBrush()}.
        // Card 8 binds Tex0=backdrop and Tex1=CompositionColorBrush.
        public Dictionary<string, Func<Compositor, CompositionBrush>> SourceBinders { get; set; }

        private CompositionEffectBrush _effectBrush;

        // Scalar values set before OnConnected ran (or captured at hot-reload rebuild
        // time). Flushed onto the effect brush once it exists, so a slider's last value
        // survives a rebuild even though the brush instance is brand new.
        private readonly Dictionary<string, float> _pendingScalars = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || EffectFactory == null)
            {
                return;
            }

            try
            {
                Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
                IGraphicsEffect effect = EffectFactory();

                CompositionEffectFactory factory =
                    (AnimatablePaths != null && AnimatablePaths.Length > 0)
                        ? compositor.CreateEffectFactory(effect, AnimatablePaths)
                        : compositor.CreateEffectFactory(effect);

                _effectBrush = factory.CreateBrush();

                if (SourceBinders != null)
                {
                    foreach (KeyValuePair<string, Func<Compositor, CompositionBrush>> kv in SourceBinders)
                    {
                        _effectBrush.SetSourceParameter(kv.Key, kv.Value(compositor));
                    }
                }

                // Apply any scalar set before the brush connected (e.g. a slider value
                // captured at rebuild time) so it does not reset to the cbuffer default.
                foreach (KeyValuePair<string, float> kv in _pendingScalars)
                {
                    _effectBrush.Properties.InsertScalar(kv.Key, kv.Value);
                }

                CompositionBrush = _effectBrush;
            }
            catch
            {
                // Effect failed to compile/link (DWM rejected the shader): leave the
                // brush transparent instead of crashing. The page shows a result label.
                CompositionBrush = null;
                _effectBrush = null;
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _effectBrush = null;
        }

        // Drives an animatable path on the connected effect brush. No-op until
        // OnConnected has run (matches CSharpDemo.LiquidGlassBrush.ApplyValue).
        public void SetScalar(string path, float value)
        {
            _pendingScalars[path] = value;
            _effectBrush?.Properties.InsertScalar(path, value);
        }
    }
}
