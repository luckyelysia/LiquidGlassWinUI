using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlendProbe.Brushes;
using BlendProbe.Effects;
using BlendProbe.Interop;
using BlendProbe.MaskBaking;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace BlendProbe.Pages
{
    // Blend verification matrix. Eleven independent probe cards, each isolating ONE
    // capability of the custom-effect runtime:
    //   1  color sampling, single source            {0x0200}                  color ABI
    //   2  custom sampler, single source            {0x0100,0x0400}           sampler ABI (UV correct)
    //   3  samplerData content rect                 {0x0100,0x0300}           sampler ABI
    //   4  color sampling + UV (limitation)         {0x0100,0x0200}           color ABI  (UV WRONG)
    //   5  effect chain: Win2D blur -> color                                   animatable BlurAmount
    //   6  effect chain + FlattenSource            blur -> sampler(Flatten)   materialized intermediate
    //   7  custom + color SIMULTANEOUSLY (headline){0x0100,0x0200,0x0400}    unknown until run
    //   8  two-source color sampling               {0x0200,0x0200}            Tex0=backdrop, Tex1=color
    //   9  two-source MIXED (color src0 + sampler src1) {0x0100,0x0200,0x0401} Mask=color, Backdrop=sampler (mask-bake gate)
    //  10  three-source MIXED (2 color + 1 sampler) {0x0100,0x0200,0x0201,0x0402} Tex0,Tex1=color, Backdrop=sampler (full-bake topology)
    //  11  CPU-baked mask as a COLOR source 2-source MIXED {0x0100,0x0200,0x0401} Mask=surface(color), Backdrop=blur(sampler) — mask-production de-risk
    //
    // Cards are built in code-behind because each brush carries an effect factory and
    // source binders. Each preview overlays a shared colorful scene so backdrop-sampling
    // effects have real content to sample; the overlay shows the effect output.
    //
    // SHADER HOT-RELOAD: a FileSystemWatcher monitors the live shader source dir. On a
    // .hlsl save it (debounced ~300ms, marshaled to the UI thread) copies the file into
    // the output dir, refreshes that card's fxc status, and rebuilds the card's brush
    // with a FRESH effect GUID. The fresh GUID is essential — the runtime's RegisterEffect
    // is idempotent and ignores same-GUID re-registration, so only a new GUID forces DWM
    // to recompile the edited shader (see CustomEffectBase.RuntimeId).
    public sealed partial class BlendPage : Page
    {
        // shader filename -> per-card rebuild actions (a shader can back several cards,
        // e.g. ColorInvert.hlsl backs both card 1 and card 5)
        private readonly Dictionary<string, List<Action>> _reloadByShader = new();
        // shader filename -> per-card fxc-status TextBlocks to refresh on reload
        private readonly Dictionary<string, List<TextBlock>> _statusByShader = new();
        // shaders changed since the last debounce tick (batched + deduped)
        private readonly HashSet<string> _pendingShaders = new();

        private DispatcherQueue _uiQueue;
        private DispatcherTimer _debounce;
        private FileSystemWatcher _watcher;
        private string _sourceShaderDir;   // where the user edits (repo sources)
        private string _outputShaderDir;   // where the running app reads (bin copy)
        private TextBlock _hotReloadStatus;

        // Card 11's mask brush, held across hot-reload: the rebuilt effect re-binds this
        // same brush; re-bakes only swap its .Surface (never re-binding the source, which
        // would reset the animatable Factor scalar). See CpuMaskBaker for the rationale.
        private CompositionSurfaceBrush _card11MaskBrush;

        public BlendPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Hot-reload banner sits at the very top of the scroll content.
            _hotReloadStatus = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB9, 0x4D)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            CardHost.Children.Insert(0, _hotReloadStatus);

            // ---- Q1: which samplings -------------------------------------------------

            // 1. Color sampling, single source.
            AddCard(
                "1", "颜色采样 · 单源",
                "args {0x0200} · linkingArgType=0 · 颜色输入 ABI · PSBody(float4 sample0)",
                "✓ 应将背景反相",
                "ColorInvert.hlsl",
                () =>
                {
                    var e = new ColorInvertEffect();
                    e.RuntimeId = Guid.NewGuid();
                    return new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        SourceBinders = Back(c => c.CreateBackdropBrush()),
                    };
                });

            // 2. Custom sampler, single source — prove UV spans [0,1].
            AddCard(
                "2", "自定义采样 · 单源（UV 正确？）",
                "args {0x0100,0x0400} · linkingArgType=0x0200 · 自定义采样 ABI · texture0.Sample",
                "✓ 应为绿→品红渐变（uv.x→R, uv.y→G），证明 UV∈[0,1]",
                "CustomSamplerUv.hlsl",
                () =>
                {
                    var e = new CustomSamplerUvEffect();
                    e.RuntimeId = Guid.NewGuid();
                    return new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        SourceBinders = Back(c => c.CreateBackdropBrush()),
                    };
                });

            // 3. samplerData content rect.
            AddCard(
                "3", "samplerData · 内容矩形",
                "args {0x0100,0x0300,0x0400} · linkingArgType=0x0200 · PSBody(uv, samplerData, samplerDataExt)",
                "✓ 应为青/绿色调（宽→R, 高→G）；纯蓝=未绑定",
                "SamplerData.hlsl",
                () =>
                {
                    var e = new SamplerDataEffect();
                    e.RuntimeId = Guid.NewGuid();
                    return new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        SourceBinders = Back(c => c.CreateBackdropBrush()),
                    };
                });

            // 4. Color sampling + UV — VERIFIED at runtime: the UV is correct even
            //    under the color-input ABI (linkingArgType=0). 0x0100 (UV) does NOT
            //    require the custom-sampler ABI; the original "UV wrong here" hypothesis
            //    was disproven — this card renders the same clean gradient as card 2.
            AddCard(
                "4", "颜色采样 + UV（已验证：UV 正确）",
                "args {0x0100,0x0200} · linkingArgType=0 · 颜色输入 ABI 下显式 UV",
                "✓ UV 正确（与 #2 渐变一致）—— 0x0100 UV 不依赖 linkingArgType=0x0200",
                "ColorInputUv.hlsl",
                () =>
                {
                    var e = new ColorInputUvEffect();
                    e.RuntimeId = Guid.NewGuid();
                    return new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        SourceBinders = Back(c => c.CreateBackdropBrush()),
                    };
                });

            // ---- Q2: effect chain combinations ---------------------------------------

            // 5. blur -> color-input effect (animatable blur).
            BlurChainBrush blur5 = null;
            var slider5 = new Slider { Header = "BlurAmount（Win2D 上游模糊半径）", Minimum = 0, Maximum = 40, Value = 4, StepFrequency = 0.5 };
            slider5.ValueChanged += (s, args) => { if (blur5 != null) blur5.BlurAmount = args.NewValue; };
            AddCard(
                "5", "effect 链 · 模糊→颜色",
                "backdrop → GaussianBlur(Blur) → ColorInvert(0x0200) · 可动画 Blur.BlurAmount",
                "✓ 应为模糊后再反相；滑动可见模糊变化",
                "ColorInvert.hlsl",
                () =>
                {
                    var downstream = new ColorInvertEffect();
                    downstream.RuntimeId = Guid.NewGuid();
                    blur5 = new BlurChainBrush
                    {
                        DownstreamFactory = () => downstream.Create(),
                        BlurAmount = blur5?.BlurAmount ?? 4.0,
                    };
                    return blur5;
                },
                slider5);

            // 6. blur -> custom-sampler effect WITH FlattenSource.
            BlurChainBrush blur6 = null;
            var slider6 = new Slider { Header = "BlurAmount（FlattenSource 物化的模糊）", Minimum = 0, Maximum = 40, Value = 6, StepFrequency = 0.5 };
            slider6.ValueChanged += (s, args) => { if (blur6 != null) blur6.BlurAmount = args.NewValue; };
            AddCard(
                "6", "effect 链 + FlattenSource",
                "backdrop → GaussianBlur → FlattenCustomSampler(FlattenSource) · 物化中间纹理",
                "✓ 应显示模糊纹理（几何可能被拉伸，属预期）",
                "FlattenCustomSampler.hlsl",
                () =>
                {
                    var downstream = new FlattenCustomSamplerEffect();
                    downstream.RuntimeId = Guid.NewGuid();
                    blur6 = new BlurChainBrush
                    {
                        DownstreamFactory = () => downstream.Create(),
                        BlurAmount = blur6?.BlurAmount ?? 6.0,
                    };
                    return blur6;
                },
                slider6);

            // ---- Q3: custom + color simultaneously (headline) ------------------------

            // 7. One shader requesting BOTH a 0x0200 color sample AND texture0. The
            //    slider drives the texture sample's horizontal offset (cbuffer _Params.x);
            //    the shader blends the backdrop color with the offset texture sample.
            BackdropEffectBrush blend7 = null;
            var slider7 = new Slider { Header = "Offset（纹理采样横向偏移量）", Minimum = 0, Maximum = 0.5, Value = 0.05, StepFrequency = 0.01 };
            slider7.ValueChanged += (s, args) => blend7?.SetScalar(ColorAndCustomSamplerEffect.OffsetPropertyPath, (float)args.NewValue);
            AddCard(
                "7", "自定义 + 颜色 · 同时跑（头条）",
                "args {0x0100,0x0200,0x0400} · linkingArgType=0x0200 · lerp(sample0, texture0.Sample(uv+offset), 0.5)",
                "？ 混合（随滑块偏移）=两者都支持；像背景=仅颜色；偏移图=仅纹理；空白=被丢弃",
                "ColorAndCustomSampler.hlsl",
                () =>
                {
                    var e = new ColorAndCustomSamplerEffect();
                    e.RuntimeId = Guid.NewGuid();
                    blend7 = new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        AnimatablePaths = new[] { ColorAndCustomSamplerEffect.OffsetPropertyPath },
                        SourceBinders = Back(c => c.CreateBackdropBrush()),
                    };
                    blend7.SetScalar(ColorAndCustomSamplerEffect.OffsetPropertyPath, (float)slider7.Value);
                    return blend7;
                },
                slider7);

            // 8. Two-source color sampling (animatable Factor).
            BackdropEffectBrush blend8 = null;
            var slider8 = new Slider { Header = "Factor（Tex0 背景 ↔ Tex1 红）", Minimum = 0, Maximum = 1, Value = 0.5, StepFrequency = 0.01 };
            slider8.ValueChanged += (s, args) => blend8?.SetScalar(TwoColorBlendEffect.FactorPropertyPath, (float)args.NewValue);
            AddCard(
                "8", "双源颜色采样",
                "args {0x0200,0x0200} · 2 sources(Tex0,Tex1) · 颜色输入 · lerp(sample0,sample1,Factor)",
                "✓ 滑动应在背景↔红之间过渡（注：编码 {0x0200,0x0200} 依参考代码；若仅见背景则改试 0x0201）",
                "TwoColorBlend.hlsl",
                () =>
                {
                    var e = new TwoColorBlendEffect();
                    e.RuntimeId = Guid.NewGuid();
                    blend8 = new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        AnimatablePaths = new[] { TwoColorBlendEffect.FactorPropertyPath },
                        SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                        {
                            { "Tex0", c => c.CreateBackdropBrush() },
                            { "Tex1", c => c.CreateColorBrush(Color.FromArgb(0xFF, 0xE5, 0x3B, 0x3B)) },
                        },
                    };
                    blend8.SetScalar(TwoColorBlendEffect.FactorPropertyPath, (float)slider8.Value);
                    return blend8;
                },
                slider8);

            // ---- Q4: two sources from DIFFERENT routes (mask-bake gating experiment) ----

            // 9. Two-source MIXED wiring — THE gating experiment for the mask-bake port.
            //    src0 "Mask" (color route -> sample0) + src1 "Backdrop" (custom-sampler
            //    route -> texture0). Card 7 proved both routes from ONE source; this
            //    proves them from TWO distinct sources. The slider wipes mask<->backdrop
            //    so each route is probed in isolation: Factor=0 shows the Mask color
            //    (color route), Factor=1 shows the live backdrop (sampler route), 0.5 is
            //    the blend (both bound). See MaskPlusBackdropEffect for the outcome map.
            BackdropEffectBrush blend9 = null;
            var slider9 = new Slider { Header = "Factor（Mask 色 ↔ 背景纹理）", Minimum = 0, Maximum = 1, Value = 0.5, StepFrequency = 0.01 };
            slider9.ValueChanged += (s, args) => blend9?.SetScalar(MaskPlusBackdropEffect.FactorPropertyPath, (float)args.NewValue);
            AddCard(
                "9", "双源 · 颜色 + 自定义采样（mask-bake 关键）",
                "args {0x0100,0x0200,0x0401} · 2 sources(Mask,Backdrop) · linkingArgType=0x0200 · lerp(sample0, texture0.Sample, Factor)",
                "？ 滑动 Mask色↔背景=双源都绑；卡在Mask色=仅颜色；卡在背景=仅采样；空白=丢弃",
                "MaskPlusBackdrop.hlsl",
                () =>
                {
                    var e = new MaskPlusBackdropEffect();
                    e.RuntimeId = Guid.NewGuid();
                    blend9 = new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        AnimatablePaths = new[] { MaskPlusBackdropEffect.FactorPropertyPath },
                        SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                        {
                            { "Mask", c => c.CreateColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)) },
                            { "Backdrop", c => c.CreateBackdropBrush() },
                        },
                    };
                    blend9.SetScalar(MaskPlusBackdropEffect.FactorPropertyPath, (float)slider9.Value);
                    return blend9;
                },
                slider9);

            // 10. Three-source MIXED wiring — closes the last topology cell. 2 color
            //     sources (Tex0, Tex1) + 1 custom-sampler source (Backdrop) in one shader:
            //     the production packing for the FULL bake (two static masks + live
            //     backdrop). Card 8 proved 2 color; card 9 proved 1 color + 1 sampler;
            //     this proves them combined. The slider blends the two mask colors 50/50,
            //     then lerps with the backdrop texture: Factor=0 shows the mask blend
            //     (periwinkle = magenta+cyan => BOTH color sources bound), Factor=1 shows
            //     the live backdrop (sampler bound). See ThreeSourceMixedEffect.
            BackdropEffectBrush blend10 = null;
            var slider10 = new Slider { Header = "Factor（mask 混色 ↔ 背景纹理）", Minimum = 0, Maximum = 1, Value = 0.5, StepFrequency = 0.01 };
            slider10.ValueChanged += (s, args) => blend10?.SetScalar(ThreeSourceMixedEffect.FactorPropertyPath, (float)args.NewValue);
            AddCard(
                "10", "三源 · 2 颜色 + 1 自定义采样（全烘焙拓扑）",
                "args {0x0100,0x0200,0x0201,0x0402} · 3 sources(Tex0,Tex1,Backdrop) · linkingArgType=0x0200 · lerp(lerp(sample0,sample1,0.5), texture0.Sample, Factor)",
                "？ Factor=0=混色(品红+青=蓝紫，证明两颜色源都绑)；Factor=1=背景(证明采样源绑)；滑动过渡=三源都绑",
                "ThreeSourceMixed.hlsl",
                () =>
                {
                    var e = new ThreeSourceMixedEffect();
                    e.RuntimeId = Guid.NewGuid();
                    blend10 = new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        AnimatablePaths = new[] { ThreeSourceMixedEffect.FactorPropertyPath },
                        SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                        {
                            { "Tex0", c => c.CreateColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)) }, // magenta
                            { "Tex1", c => c.CreateColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xFF)) }, // cyan
                            { "Backdrop", c => c.CreateBackdropBrush() },
                        },
                    };
                    blend10.SetScalar(ThreeSourceMixedEffect.FactorPropertyPath, (float)slider10.Value);
                    return blend10;
                },
                slider10);

            // 11. CPU-baked mask as a COLOR source — the mask-production de-risk probe.
            //     The mask is computed on the CPU (CpuMaskBaker, same rounded-rect SDF as
            //     LiquidGlass.hlsl), uploaded via Win2D, and bound as src0 (color route);
            //     the raw backdrop is src1 (sampler). Topology is the verified card-9 wiring.
            //     The mask is baked at a FRACTION of the overlay's physical px (scale 0.5)
            //     and at the overlay's real aspect ratio, then re-baked on SizeChanged, so
            //     the rounded rect fills the rect correctly (no stretch) — exactly like the
            //     shader's res = control px size. DWM bilinearly upsamples the sub-1:1 field.
            //     The shader lerps sample0 <-> texture0.Sample by Factor:
            //       Factor=0 -> the baked mask (R/G position gradient + B rounded-rect).
            //       Factor=1 -> the raw backdrop (whatever is behind the card).
            BackdropEffectBrush blend11 = null;
            var slider11 = new Slider { Header = "Factor（烘焙 mask 色 ↔ 背景）", Minimum = 0, Maximum = 1, Value = 0.0, StepFrequency = 0.01 };
            slider11.ValueChanged += (s, args) => blend11?.SetScalar(CpuMaskSourceEffect.FactorPropertyPath, (float)args.NewValue);
            Border overlay11 = AddCard(
                "11", "CPU 烘焙 mask 作为颜色源（mask 生产路径 de-risk）",
                "args {0x0100,0x0200,0x0401} · 2 sources(Mask=surface颜色,Backdrop=背景采样) · CPU bake(比例一致,0.5x)->Win2D upload->sample0",
                "？ Factor=0=烘焙图样(R/G位置渐变+B圆角矩形,圆角不拉伸=比例✓);  Factor=1=卡片背后的背景;  拖动窗口=圆角矩形跟随重烘焙",
                "CpuMaskSource.hlsl",
                () =>
                {
                    var e = new CpuMaskSourceEffect();
                    e.RuntimeId = Guid.NewGuid();

                    // One mask brush, held in a field so it survives hot-reload (the
                    // rebuilt effect just re-binds it). Re-bakes swap its .Surface, NEVER
                    // re-binding the source — post-connection SetSourceParameter would reset
                    // the animatable Factor scalar (the slider-vs-mask mutual-exclusion bug).
                    if (_card11MaskBrush == null)
                    {
                        Compositor c0 = CompositionTarget.GetCompositorForCurrentThread();
                        _card11MaskBrush = c0.CreateSurfaceBrush();
                        _card11MaskBrush.Stretch = CompositionStretch.Fill;
                    }

                    blend11 = new BackdropEffectBrush
                    {
                        EffectFactory = () => e.Create(),
                        AnimatablePaths = [CpuMaskSourceEffect.FactorPropertyPath],
                        SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                        {
                            { "Mask", c => _card11MaskBrush },
                            { "Backdrop", c => c.CreateBackdropBrush() },
                        },
                    };
                    blend11.SetScalar(CpuMaskSourceEffect.FactorPropertyPath, (float)slider11.Value);
                    return blend11;
                },
                slider11);

            // Drive the mask brush from the overlay's real rect: bake at × 0.5 (aspect-
            // correct => no stretch), re-bake on SizeChanged by swapping .Surface. Set up
            // ONCE (overlay11 persists across hot-reload; the field brush is reused), so a
            // hot-reloaded effect still sees the live mask + a working slider.
            if (overlay11 != null)
            {
                Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
                CpuMaskBaker.BakeToBrush(compositor, overlay11, scale: 0.5f, _card11MaskBrush);
            }

            SetupHotReload();
        }

        // ---- card building --------------------------------------------------------

        // Builds one probe card and appends it to CardHost. makeBrush builds a fresh
        // brush (bumping the effect's RuntimeId to a new GUID each call) and is invoked
        // both for the initial render and on every hot-reload of `shader`. extra is an
        // optional slider (cards 5,6,8). The card's fxc-status TextBlock is registered
        // so it can be refreshed when the shader changes. Returns the effect preview
        // overlay Border (its Background = the effect brush) so a caller can subscribe to
        // its SizeChanged (e.g. card 11 re-bakes its mask when the overlay resizes).
        private Border AddCard(string idx, string title, string abi, string expected, string shader, Func<Brush> makeBrush, FrameworkElement extra = null)
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
                Text = "预期：" + expected,
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

            // Preview = colorful scene (sampled by backdrop effects) with the effect
            // overlay on top. The overlay Border is captured so hot-reload can swap its
            // Background without rebuilding the whole card.
            var preview = new Grid { Height = 130 };
            preview.Children.Add(BuildScene());
            var overlay = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
            };
            preview.Children.Add(overlay);
            content.Children.Add(preview);

            var statusBox = new TextBlock
            {
                Text = HslStatus(shader, "PSBody"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x99, 0x99, 0x99)),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            content.Children.Add(statusBox);

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

            // Initial render (safe: a brush whose OnConnected later fails just renders null).
            try { overlay.Background = makeBrush(); } catch { }

            // Register for hot-reload keyed by shader file.
            if (!_reloadByShader.ContainsKey(shader))
            {
                _reloadByShader[shader] = new List<Action>();
                _statusByShader[shader] = new List<TextBlock>();
            }
            _reloadByShader[shader].Add(() => { try { overlay.Background = makeBrush(); } catch { } });
            _statusByShader[shader].Add(statusBox);

            return overlay;
        }

        // The colorful scene the backdrop-sampling effects refract/sample: a vibrant
        // diagonal gradient with text so invert has something recognizable to invert.
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
                Text = "BACKDROP SAMPLE\n拖拽此处内容到玻璃下对比",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
            };
            return scene;
        }

        // Single "Backdrop" source binder (the common case for cards 1-4,7).
        private static Dictionary<string, Func<Compositor, CompositionBrush>> Back(Func<Compositor, CompositionBrush> binder)
        {
            return new Dictionary<string, Func<Compositor, CompositionBrush>> { { "Backdrop", binder } };
        }

        // ---- shader hot-reload ----------------------------------------------------

        private void SetupHotReload()
        {
            _uiQueue = DispatcherQueue.GetForCurrentThread();
            _outputShaderDir = Path.Combine(AppContext.BaseDirectory, "Effects", "Shaders");

            // In a dev build the app runs from bin/<plat>/<cfg>/<tfm>; the LIVE shader
            // sources live four levels up at <project>/Effects/Shaders. Watch that dir
            // and mirror each edited .hlsl into the output dir so the runtime (which
            // reads the output copy) sees it. If the source dir isn't found (running
            // elsewhere), fall back to watching the output dir directly.
            string devDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Effects", "Shaders"));
            _sourceShaderDir = Directory.Exists(devDir) ? devDir : _outputShaderDir;

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += OnDebounceTick;

            try
            {
                _watcher = new FileSystemWatcher(_sourceShaderDir, "*.hlsl")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnShaderFileChanged;
                _watcher.Created += OnShaderFileChanged;
                _watcher.Renamed += OnShaderFileChanged;
            }
            catch
            {
                // Watcher setup failure (e.g. dir missing) is non-fatal; cards just won't hot-reload.
            }

            if (_hotReloadStatus != null)
            {
                bool dev = _sourceShaderDir != _outputShaderDir;
                _hotReloadStatus.Text =
                    "🔥 热更新已启用：监听 " + _sourceShaderDir +
                    (dev ? "\n  （编辑源 .hlsl → 自动镜像到输出目录 → 对应卡片重渲染）"
                         : "\n  （编辑输出目录 .hlsl → 对应卡片重渲染）");
            }
        }

        // Raised on a FileSystemWatcher thread (background). Just record the file and
        // (re)start the debounce timer on the UI thread (DispatcherTimer is UI-affined).
        private void OnShaderFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_uiQueue == null) return;
            string file = Path.GetFileName(e.FullPath);
            if (string.IsNullOrEmpty(file)) return;
            lock (_pendingShaders) { _pendingShaders.Add(file); }
            _uiQueue.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); });
        }

        // Raised on the UI thread after ~300ms of quiescence. Reload every shader that
        // changed during the window (a save often fires several events; they dedupe via
        // the HashSet).
        private void OnDebounceTick(object sender, object e)
        {
            _debounce.Stop();
            List<string> batch;
            lock (_pendingShaders) { batch = _pendingShaders.ToList(); _pendingShaders.Clear(); }
            foreach (string file in batch) ReloadShader(file);
        }

        private void ReloadShader(string file)
        {
            if (!_reloadByShader.TryGetValue(file, out var reloads) || reloads.Count == 0) return;

            // Dev mode: the watcher fired on the SOURCE file; mirror it into the output
            // dir so ShaderSourceLoader (which reads the output copy) and the fxc check
            // pick it up. If the editor still holds the file, skip — the next save retries.
            if (_sourceShaderDir != _outputShaderDir)
            {
                string src = Path.Combine(_sourceShaderDir, file);
                string dst = Path.Combine(_outputShaderDir, file);
                try { if (File.Exists(src)) File.Copy(src, dst, overwrite: true); }
                catch { return; }
            }

            // Refresh the per-card fxc syntax status (now reads the updated output copy).
            string status = HslStatus(file, "PSBody");
            if (_statusByShader.TryGetValue(file, out var boxes))
                foreach (var b in boxes) b.Text = status;

            // Rebuild each card's brush. makeBrush bumps the effect's RuntimeId to a
            // fresh GUID so the runtime re-registers (it ignores same-GUID
            // re-registration) and DWM recompiles the edited shader.
            int n = 0;
            foreach (var r in reloads) { try { r(); n++; } catch { } }

            if (_hotReloadStatus != null)
            {
                _hotReloadStatus.Text =
                    "🔥 [" + DateTime.Now.ToString("HH:mm:ss") + "] 已热更新 " + file +
                    " → " + n + " 张卡片重渲染\n  监听 " + _sourceShaderDir;
            }
        }

        // ---- fxc syntax check -----------------------------------------------------

        // Compiles the shader source with D3DCompile purely to surface fxc syntax
        // errors (DWM silently drops shaders that fail to compile). Returns a one-line
        // status string. The entry here is "PSBody"; edge-mode aliases are appended by
        // DWM's linker at runtime and are invisible to a plain fxc compile.
        //
        // IMPORTANT: PSBody is a FRAGMENT function, not a standalone ps_5_0 entry. DWM's
        // linker wraps it and injects input semantics at link time. A direct fxc compile
        // therefore always reports "X3502 ... missing semantics" on the params — even for
        // the working reference effects (CustomInvertEffect / CustomBlurEffect), which
        // render correctly. X3502 fires AFTER syntax parsing, so a semantics-only failure
        // means "syntax OK"; we surface it as success and only report genuine syntax/type
        // errors (anything other than X3502 / missing semantics).
        private static string HslStatus(string file, string entry)
        {
            try
            {
                string source = ShaderSourceLoader.Load(file);
                string error = CustomEffectInterop.CompileShader(source, entry, "ps_5_0");

                if (error == null)
                {
                    return "HLSL: ✓ " + file + " 编译通过";
                }

                // A semantics-only failure (every non-empty error line is X3502 / missing
                // semantics) is expected for a fragment entry — treat as syntax-clean.
                bool onlySemantics = true;
                foreach (string raw in error.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.Contains("X3502") || line.Contains("missing semantics")) continue;
                    onlySemantics = false;
                    break;
                }

                return onlySemantics
                    ? "HLSL: ✓ " + file + " 语法通过（X3502 语义缺失属预期，由 DWM 链接器注入）"
                    : "HLSL: ✗ " + file + "\n" + error;
            }
            catch (Exception ex)
            {
                return "HLSL: ! " + file + " — " + ex.Message;
            }
        }
    }
}
