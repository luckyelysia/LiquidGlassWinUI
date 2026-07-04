using System;
using System.Collections.Generic;
using BlendProbe.Brushes;
using BlendProbe.Effects;
using BlendProbe.MaskBaking;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.UI;

namespace BlendProbe.Pages
{
    // Mask-binding diagnostic — a focused, hot-reload-free harness to settle card 11's
    // open question: in the 2-source topology (baked mask = color src0, live backdrop =
    // sampler src1), WHY did `texture0` return the mask instead of the backdrop, and is
    // the backdrop reachable at all?
    //
    // Three cards, one scene (with text) under each overlay:
    //   A — 1 source = mask surface, return texture0.   Does a LONE surface bind texture0?
    //        (root-cause test for card 11's "texture0 = mask").
    //   B — 1 source = backdrop,      return texture0.   Backdrop-alone control (expect text).
    //   C — 2 sources (mask color + backdrop sampler), Mode 0..5 selector.   THE decisive probe.
    //
    // No hot-reload here — the Mode slider gives live input switching instead. Mask re-bake
    // uses the Surface-swap pattern (no source re-bind -> no animatable-scalar reset); see
    // memory setsourceparameter-resets-animatable-scalar.
    public sealed partial class MaskBindingPage : Page
    {
        public MaskBindingPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();

            AddLegend();

            // ---- Card A: single source = MASK SURFACE, return texture0 -----------------
            // Root-cause test: a CompositionSurfaceBrush bound as the SOLE sampler source —
            // does it claim texture0 even with no backdrop competing for the slot?
            var maskBrushA = compositor.CreateSurfaceBrush();
            maskBrushA.Stretch = CompositionStretch.Fill;
            Border overlayA = AddCard(
                "A", "单源 · mask 表面 → texture0",
                "1 source (MaskSamplerEffect) · args {0x0100,0x0400} · 源=烘焙 mask 表面 · return texture0",
                "显示 mask(R/G渐变+圆角矩形,无文字) → 表面画笔单独即可占据 texture0（card 11 现象根因）");
            {
                var effect = new MaskSamplerEffect();
                overlayA.Background = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Backdrop", c => maskBrushA },
                    },
                };
            }
            // Size the mask to this card's rect + re-bake on resize (Surface-swap only).
            CpuMaskBaker.BakeToBrush(compositor, overlayA, scale: 0.5f, maskBrushA);

            // ---- Card B: single source = BACKDROP, return texture0 (control) ----------
            Border overlayB = AddCard(
                "B", "单源 · backdrop → texture0（对照）",
                "1 source (MaskSamplerEffect) · args {0x0100,0x0400} · 源=CreateBackdropBrush · return texture0",
                "显示场景文字 → 背景单独即可到达 texture0（基线对照）");
            {
                var effect = new MaskSamplerEffect();
                overlayB.Background = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Backdrop", c => c.CreateBackdropBrush() },
                    },
                };
            }

            // ---- Card C: 2 sources, Mode selector (THE probe) ------------------------
            var maskBrushC = compositor.CreateSurfaceBrush();
            maskBrushC.Stretch = CompositionStretch.Fill;

            BackdropEffectBrush brushC = null;
            var modeSlider = new Slider
            {
                Header = "Mode  0=sample0 · 1=texture0 · 2=UV · 3=samplerDataExt · 4=左右分屏 · 5=品红",
                Minimum = 0,
                Maximum = 5,
                Value = 4,
                StepFrequency = 0.1,
            };
            modeSlider.ValueChanged += (s, args) =>
                brushC?.SetScalar(MaskBindingProbeEffect.ModePropertyPath, (float)args.NewValue);

            Border overlayC = AddCard(
                "C", "双源 · mask(色route) + backdrop(采样route) · Mode 切换",
                "2 sources (MaskBindingProbeEffect) · args {0x0100,0x0200,0x0401} · src0=Mask(色) src1=Backdrop(采样)",
                "Mode=4 分屏：左=sample0 右=texture0。看哪边有文字（=背景）哪边是 mask",
                modeSlider);
            {
                var effect = new MaskBindingProbeEffect();
                brushC = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    AnimatablePaths = new[] { MaskBindingProbeEffect.ModePropertyPath },
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Mask", c => maskBrushC },
                        { "Backdrop", c => c.CreateBackdropBrush() },
                    },
                };
                // Pre-seed Mode (applied in OnConnected via _pendingScalars).
                brushC.SetScalar(MaskBindingProbeEffect.ModePropertyPath, (float)modeSlider.Value);
                overlayC.Background = brushC;
            }
            CpuMaskBaker.BakeToBrush(compositor, overlayC, scale: 0.5f, maskBrushC);

            // ---- Card D: SWAP — Backdrop(src0) + Mask(src1) -------------------------
            // The decisive swap. Card C had Mask=src0 and the mask surface stole texture0
            // (backdrop was relegated to sample1). Here the order is reversed. Read the RIGHT
            // band of the Mode=3 split (= texture0): if it has text -> texture0 is the
            // backdrop -> mask-bake is viable (texture0 refraction + sample1 mask fields). If
            // the right band is the mask -> the surface claimed texture0 even from src1 ->
            // single-sampler hard wall blocks lossless bake.
            var maskBrushD = compositor.CreateSurfaceBrush();
            maskBrushD.Stretch = CompositionStretch.Fill;

            BackdropEffectBrush brushD = null;
            var swapSlider = new Slider
            {
                Header = "Mode  0=sample0(应背景) · 1=sample1(应mask) · 2=texture0(关键) · 3=三分屏 · 4=uv · 5=品红",
                Minimum = 0,
                Maximum = 5,
                Value = 3,
                StepFrequency = 1,
            };
            swapSlider.ValueChanged += (s, args) =>
                brushD?.SetScalar(MaskBindingSwapProbeEffect.ModePropertyPath, (float)args.NewValue);

            Border overlayD = AddCard(
                "D", "交换源序 · backdrop(src0) + mask(src1) · Mode 切换",
                "2 sources (MaskBindingSwapProbeEffect) · src0=Backdrop src1=Mask · args {0x0100,0x0200,0x0201,0x0300,0x0400}",
                "Mode=3 三分屏：左=sample0 中=sample1 右=texture0。【右band有文字=背景→交换有效，烘焙可行】【右band是mask→表面仍抢占texture0→硬墙】",
                swapSlider);
            {
                var effect = new MaskBindingSwapProbeEffect();
                brushD = new BackdropEffectBrush
                {
                    EffectFactory = () => effect.Create(),
                    AnimatablePaths = new[] { MaskBindingSwapProbeEffect.ModePropertyPath },
                    SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                    {
                        { "Backdrop", c => c.CreateBackdropBrush() },
                        { "Mask", c => maskBrushD },
                    },
                };
                // Pre-seed Mode (applied in OnConnected via _pendingScalars).
                brushD.SetScalar(MaskBindingSwapProbeEffect.ModePropertyPath, (float)swapSlider.Value);
                overlayD.Background = brushD;
            }
            CpuMaskBaker.BakeToBrush(compositor, overlayD, scale: 0.5f, maskBrushD);
        }

        // ---- card scaffolding ------------------------------------------------------

        // Builds a labeled card with a scene+overlay preview and appends it to CardHost.
        // Returns the overlay Border (its Background = the effect brush) so the caller can
        // size the mask brush to its rect. `extra` is an optional control under the preview.
        private Border AddCard(string idx, string title, string abi, string expected, FrameworkElement extra = null)
        {
            var content = new StackPanel { Spacing = 8 };

            content.Children.Add(new TextBlock
            {
                Text = idx + ". " + title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            });
            content.Children.Add(new TextBlock
            {
                Text = "判读：" + expected,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC8)),
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = abi,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x9A, 0xCD, 0xFF)),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
            });

            // Preview = scene (sampled by backdrop effects) with the effect overlay on top.
            var preview = new Grid ();
            preview.Children.Add(BuildScene());
            var overlay = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                MinHeight = 800,
                MinWidth = 800,
                MaxWidth =800,
                MaxHeight = 800,
                //BorderThickness = new Thickness(1),
                //CornerRadius = new CornerRadius(8),
            };
            preview.Children.Add(overlay);
            content.Children.Add(preview);

            if (extra != null)
            {
                content.Children.Add(extra);
            }

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x26, 0x26, 0x26)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Child = content,
            };
            CardHost.Children.Add(card);
            return overlay;
        }

        private void AddLegend()
        {
            var legend = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC)),
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "如何判读：mask（烘焙图样）= R 沿水平渐变、G 沿垂直渐变、B 为圆角矩形，【无文字】。" +
                    "背景（场景）= 紫→青→蓝对角渐变 + 白色文字 “BACKDROP SAMPLE”。" +
                    "所以【有文字 = 背景】【R/G 渐变+圆角 = mask】。\n" +
                    "Card C 是决定性测试，拖 Mode=4 看左右分屏：" +
                    "两边都是 mask → 背景被丢弃（生产拓扑对“表面 mask”不成立）；" +
                    "左 mask 右文字 → 绑定符合预期；左文字 右 mask → 两路绑定互换。",
            };
            var box = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x26, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x4A, 0x5A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Child = legend,
            };
            CardHost.Children.Add(box);
        }

        // The scene under each overlay: diagonal gradient + text so the backdrop is
        // recognizable (text = backdrop; gradients-only = mask).
        private static Border BuildScene()
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            gradient.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xFF, 0x7E, 0x1F, 0xA8) });
            gradient.GradientStops.Add(new GradientStop { Offset = 0.5, Color = Color.FromArgb(0xFF, 0x0E, 0x8A, 0xA7) });
            gradient.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(0xFF, 0x13, 0x57, 0xB0) });

            var scene = new Border
            {
                Background = gradient,
                CornerRadius = new CornerRadius(8),
            };
            scene.Child = new TextBlock
            {
                Text = "BACKDROP SAMPLE\n（这是背景：有文字）",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
            };
            return scene;
        }
    }
}
