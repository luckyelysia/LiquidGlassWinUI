using System;
using System.Collections.Generic;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Generalized BlurChainBrush: a LINEAR chain of N independent CompositionEffectFactory
    // stages, cascaded backdrop -> stage0 -> stage1 -> ... -> stageN-1. Each stage builds
    // its own IGraphicsEffect (a Win2D built-in or a custom DWM effect) whose single source
    // parameter is named "Backdrop"; stage0 is fed compositor.CreateBackdropBrush() and
    // every later stage is fed the previous stage's CompositionEffectBrush.
    //
    // Animatable property paths (e.g. "Blur.BlurAmount", "Sat.Saturation",
    // "ColorTintEffect.Amount") are declared per-stage and routed by path: SetScalar(path,
    // value) writes onto whichever stage owns that path. _pendingScalars caches values set
    // before OnConnected (or captured at hot-reload rebuild) and flushes them once the
    // stage brushes exist, so a slider's last value survives a fresh-GUID rebuild.
    //
    // Each stage's effect .Name must be unique within the chain so its animatable path is
    // unambiguous for routing (short names: Blur / Sat / Exp / Hue / Contrast / Matrix).
    public sealed class ChainStage
    {
        // Builds a fresh terminal IGraphicsEffect whose source is wired to the
        // "Backdrop" source parameter (either a CompositionEffectSourceParameter("Backdrop")
        // for Win2D built-ins, or the custom effect's internal Backdrop source).
        public Func<IGraphicsEffect> EffectFactory { get; set; }

        // Animatable paths handed to CreateEffectFactory for THIS stage (null/empty =>
        // CreateEffectFactory without an animatable-property set).
        public string[] AnimatablePaths { get; set; }
    }

    public sealed class EffectChainBrush : XamlCompositionBrushBase
    {
        // Ordered stages, upstream first. Set before the brush attaches.
        public IReadOnlyList<ChainStage> Stages { get; set; }

        private CompositionEffectBrush[] _stageBrushes;
        private readonly Dictionary<string, int> _pathToStage = new();
        private readonly Dictionary<string, float> _pendingScalars = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || Stages == null || Stages.Count == 0)
            {
                return;
            }

            try
            {
                Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
                int n = Stages.Count;
                _stageBrushes = new CompositionEffectBrush[n];
                _pathToStage.Clear();

                for (int i = 0; i < n; i++)
                {
                    ChainStage stage = Stages[i];
                    IGraphicsEffect effect = stage.EffectFactory();

                    CompositionEffectFactory factory =
                        (stage.AnimatablePaths != null && stage.AnimatablePaths.Length > 0)
                            ? compositor.CreateEffectFactory(effect, stage.AnimatablePaths)
                            : compositor.CreateEffectFactory(effect);

                    _stageBrushes[i] = factory.CreateBrush();

                    if (stage.AnimatablePaths != null)
                    {
                        foreach (string path in stage.AnimatablePaths)
                        {
                            _pathToStage[path] = i;
                        }
                    }
                }

                // Cascade sources: stage0 <- backdrop, stage i <- stage i-1.
                _stageBrushes[0].SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
                for (int i = 1; i < n; i++)
                {
                    _stageBrushes[i].SetSourceParameter("Backdrop", _stageBrushes[i - 1]);
                }

                CompositionBrush = _stageBrushes[n - 1];

                // Flush any scalars captured before the brushes existed (slider values
                // recorded during a hot-reload rebuild).
                foreach (KeyValuePair<string, float> kv in _pendingScalars)
                {
                    ApplyScalar(kv.Key, kv.Value);
                }
            }
            catch
            {
                // DWM rejected a stage's shader or a source failed to bind: leave the
                // brush transparent rather than crashing the page.
                CompositionBrush = null;
                _stageBrushes = null;
                _pathToStage.Clear();
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _stageBrushes = null;
            _pathToStage.Clear();
        }

        private void ApplyScalar(string path, float value)
        {
            if (_stageBrushes != null && _pathToStage.TryGetValue(path, out int i))
            {
                _stageBrushes[i].Properties.InsertScalar(path, value);
            }
        }

        // Drives an animatable path on whichever stage owns it. Cached until the stage
        // brushes exist, then flushed by OnConnected.
        public void SetScalar(string path, float value)
        {
            _pendingScalars[path] = value;
            ApplyScalar(path, value);
        }
    }
}
