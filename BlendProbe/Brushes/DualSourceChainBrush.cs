using System;
using System.Collections.Generic;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Effects;

namespace BlendProbe.Brushes
{
    // Two-branch topology (Combo group 5): backdrop feeds two independent upstream
    // branches (each a single Win2D effect, e.g. GaussianBlur and HueRotation), whose
    // outputs are composited by a Win2D BlendEffect (Background=branchA, Foreground=
    // branchB), then optionally passed through a terminal custom color-input effect
    // (e.g. ColorInvert). Each branch is its own CompositionEffectFactory with its own
    // animatable paths; the terminal effect (if any) declares its source as "Backdrop"
    // and is fed the blended result.
    //
    // Path routing mirrors EffectChainBrush: SetScalar(path, value) writes onto whichever
    // brush (branchA / branchB / terminal) owns that path, and _pendingScalars survives a
    // hot-reload rebuild. Note BlendEffect itself is NOT animatable here — its two source
    // names are Background / Foreground, not "Backdrop".
    public sealed class DualSourceChainBrush : XamlCompositionBrushBase
    {
        // Branch A: a single effect whose source is CompositionEffectSourceParameter("Backdrop").
        public Func<IGraphicsEffect> BranchAFactory { get; set; }
        public string[] BranchAPaths { get; set; }

        // Branch B: same shape as A; both branches sample the live backdrop independently.
        public Func<IGraphicsEffect> BranchBFactory { get; set; }
        public string[] BranchBPaths { get; set; }

        // How the two branches combine.
        public BlendEffectMode BlendMode { get; set; } = BlendEffectMode.Multiply;

        // Optional terminal color-input effect applied AFTER the blend (source "Backdrop"
        // = blend output). Null => the blended result is the final brush.
        public Func<IGraphicsEffect> TerminalFactory { get; set; }
        public string[] TerminalPaths { get; set; }

        private readonly Dictionary<string, CompositionEffectBrush> _pathToBrush = new();
        private readonly Dictionary<string, float> _pendingScalars = new();

        protected override void OnConnected()
        {
            if (CompositionBrush != null || BranchAFactory == null || BranchBFactory == null)
            {
                return;
            }

            try
            {
                Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
                CompositionBrush backdrop = compositor.CreateBackdropBrush();
                _pathToBrush.Clear();

                CompositionEffectBrush branchA = BuildBrush(compositor, BranchAFactory, BranchAPaths);
                branchA.SetSourceParameter("Backdrop", backdrop);
                RegisterPaths(BranchAPaths, branchA);

                CompositionEffectBrush branchB = BuildBrush(compositor, BranchBFactory, BranchBPaths);
                branchB.SetSourceParameter("Backdrop", backdrop);
                RegisterPaths(BranchBPaths, branchB);

                // Blend the two branches. Source names are Background / Foreground.
                BlendEffect blend = new BlendEffect
                {
                    Mode = BlendMode,
                    Background = new CompositionEffectSourceParameter("Background"),
                    Foreground = new CompositionEffectSourceParameter("Foreground"),
                };
                CompositionEffectBrush blendBrush =
                    compositor.CreateEffectFactory(blend).CreateBrush();
                blendBrush.SetSourceParameter("Background", branchA);
                blendBrush.SetSourceParameter("Foreground", branchB);

                if (TerminalFactory != null)
                {
                    CompositionEffectBrush terminal = BuildBrush(compositor, TerminalFactory, TerminalPaths);
                    terminal.SetSourceParameter("Backdrop", blendBrush);
                    RegisterPaths(TerminalPaths, terminal);
                    CompositionBrush = terminal;
                }
                else
                {
                    CompositionBrush = blendBrush;
                }

                foreach (KeyValuePair<string, float> kv in _pendingScalars)
                {
                    ApplyScalar(kv.Key, kv.Value);
                }
            }
            catch
            {
                CompositionBrush = null;
                _pathToBrush.Clear();
            }
        }

        protected override void OnDisconnected()
        {
            CompositionBrush = null;
            _pathToBrush.Clear();
        }

        private CompositionEffectBrush BuildBrush(
            Compositor compositor, Func<IGraphicsEffect> factory, string[] paths)
        {
            IGraphicsEffect effect = factory();
            CompositionEffectFactory f =
                (paths != null && paths.Length > 0)
                    ? compositor.CreateEffectFactory(effect, paths)
                    : compositor.CreateEffectFactory(effect);
            return f.CreateBrush();
        }

        private void RegisterPaths(string[] paths, CompositionEffectBrush brush)
        {
            if (paths == null) return;
            foreach (string path in paths)
            {
                _pathToBrush[path] = brush;
            }
        }

        private void ApplyScalar(string path, float value)
        {
            if (_pathToBrush.TryGetValue(path, out CompositionEffectBrush brush))
            {
                brush.Properties.InsertScalar(path, value);
            }
        }

        public void SetScalar(string path, float value)
        {
            _pendingScalars[path] = value;
            ApplyScalar(path, value);
        }
    }
}
