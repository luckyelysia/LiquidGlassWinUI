using System;
using System.Collections.Generic;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Two-stage chain where EACH stage is an independent FlattenSource effect with
    // TWO named sources. Stage 0 binds "Backdrop"=backdrop + "ColorSrc"=color_A.
    // Stage 1 binds "Backdrop"=stage_0_output + "ColorSrc"=color_B.
    //
    // This validates the "2N → 2N" cascade: both effects have sourceCount=2 with
    // FlattenSource, and the downstream effect consumes the upstream effect's output
    // as its primary source. The FlattenSource materialization at each stage must
    // handle the fact that the upstream output already went through its own flatten
    // pipeline.
    public sealed class TwoStageFlattenChainBrush : XamlCompositionBrushBase
    {
        // Stage 0: builds an IGraphicsEffect whose named sources are "Backdrop" and
        // "ColorSrc". "Backdrop" is auto-wired to CreateBackdropBrush. "ColorSrc" is
        // bound to ColorSrcA.
        public Func<IGraphicsEffect> Stage0Factory { get; set; }
        public string[] Stage0AnimatablePaths { get; set; }
        public Windows.UI.Color ColorSrcA { get; set; }

        // Stage 1: builds an IGraphicsEffect whose named sources are "Backdrop" and
        // "ColorSrc". "Backdrop" is auto-wired to stage 0's output brush. "ColorSrc"
        // is bound to ColorSrcB.
        public Func<IGraphicsEffect> Stage1Factory { get; set; }
        public string[] Stage1AnimatablePaths { get; set; }
        public Windows.UI.Color ColorSrcB { get; set; }

        // Returns the stage 0 brush so callers can set scalars on it (e.g. Factor).
        public CompositionEffectBrush Stage0Brush { get; private set; }
        // Returns the stage 1 (terminal) brush so callers can set scalars on it.
        public CompositionEffectBrush Stage1Brush { get; private set; }

        private readonly Dictionary<string, float> _pendingScalars0 = new();
        private readonly Dictionary<string, float> _pendingScalars1 = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || Stage0Factory == null || Stage1Factory == null)
            {
                return;
            }

            try
            {
                Compositor c = CompositionTarget.GetCompositorForCurrentThread();

                // ---- Stage 0 --------------------------------------------------
                IGraphicsEffect effect0 = Stage0Factory();
                CompositionEffectFactory factory0 =
                    (Stage0AnimatablePaths != null && Stage0AnimatablePaths.Length > 0)
                        ? c.CreateEffectFactory(effect0, Stage0AnimatablePaths)
                        : c.CreateEffectFactory(effect0);

                Stage0Brush = factory0.CreateBrush();
                Stage0Brush.SetSourceParameter("Backdrop", c.CreateBackdropBrush());
                Stage0Brush.SetSourceParameter("ColorSrc", c.CreateColorBrush(ColorSrcA));

                // ---- Stage 1 --------------------------------------------------
                IGraphicsEffect effect1 = Stage1Factory();
                CompositionEffectFactory factory1 =
                    (Stage1AnimatablePaths != null && Stage1AnimatablePaths.Length > 0)
                        ? c.CreateEffectFactory(effect1, Stage1AnimatablePaths)
                        : c.CreateEffectFactory(effect1);

                Stage1Brush = factory1.CreateBrush();
                Stage1Brush.SetSourceParameter("Backdrop", Stage0Brush);
                Stage1Brush.SetSourceParameter("ColorSrc", c.CreateColorBrush(ColorSrcB));

                CompositionBrush = Stage1Brush;

                foreach (KeyValuePair<string, float> kv in _pendingScalars0)
                    Stage0Brush?.Properties.InsertScalar(kv.Key, kv.Value);
                foreach (KeyValuePair<string, float> kv in _pendingScalars1)
                    Stage1Brush?.Properties.InsertScalar(kv.Key, kv.Value);
            }
            catch
            {
                CompositionBrush = null;
                Stage0Brush = null;
                Stage1Brush = null;
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            Stage0Brush = null;
            Stage1Brush = null;
        }

        public void SetScalar0(string path, float value)
        {
            _pendingScalars0[path] = value;
            Stage0Brush?.Properties.InsertScalar(path, value);
        }

        public void SetScalar1(string path, float value)
        {
            _pendingScalars1[path] = value;
            Stage1Brush?.Properties.InsertScalar(path, value);
        }
    }
}
