using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlendProbe.Brushes;
using BlendProbe.Effects;
using BlendProbe.Interop;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics.Effects;
using Windows.UI;

namespace BlendProbe.Pages
{
    // Multi-effect combination showcase: six cards, each a >=3-stage effect pipeline
    // chaining Win2D built-ins with custom DWM effects. Stages are independent
    // CompositionEffectFactory instances cascaded via EffectChainBrush (linear) or
    // DualSourceChainBrush (group 5's two-branch topology). Each stage's animatable
    // property is driven by a slider through chain.SetScalar(path, value).
    //
    //   1  电影负片  Saturation -> Exposure -> ColorInvert(custom)
    //   2  梦幻光晕  GaussianBlur -> Saturation -> ColorTint(custom)
    //   3  色散玻璃  GaussianBlur -> ChromaticAberration(custom,FlattenSource) -> ColorTint(custom)
    //   4  复古胶片  HueRotation -> BrightnessContrast -> Sepia -> ColorInvert(custom)
    //   5  双源叠影  (GaussianBlur) + (HueRotation) -> Blend(Multiply) -> ColorInvert(custom)
    //   6  通道矩阵  ColorMatrix(sepia) -> GaussianBlur -> ColorTint(custom)
    //
    // SHADER HOT-RELOAD: identical machinery to BlendPage. A FileSystemWatcher watches the
    // live shader source dir; on a .hlsl save it (debounced ~300ms, marshaled to the UI
    // thread) mirrors the file into the output dir and rebuilds each dependent card's
    // chain with FRESH effect GUIDs (RegisterEffect is idempotent, so a new GUID is the
    // only way to force DWM to recompile the edited shader). Group 3 depends on TWO
    // shaders; AddCard registers its rebuild under each.
    public sealed partial class ComboPage : Page
    {
        // shader filename -> per-card rebuild actions (a shader can back several cards;
        // ColorInvert.hlsl backs groups 1, 4 and 5; ColorTint.hlsl backs 2, 3 and 6).
        private readonly Dictionary<string, List<Action>> _reloadByShader = new();
        // shader filename -> per-card fxc-status TextBlocks refreshed on reload.
        private readonly Dictionary<string, List<TextBlock>> _statusByShader = new();
        // shaders changed since the last debounce tick (batched + deduped).
        private readonly HashSet<string> _pendingShaders = new();

        private DispatcherQueue _uiQueue;
        private DispatcherTimer _debounce;
        private FileSystemWatcher _watcher;
        private string _sourceShaderDir;
        private string _outputShaderDir;
        private TextBlock _hotReloadStatus;

        public ComboPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _hotReloadStatus = new TextBlock
            {
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB9, 0x4D)),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            CardHost.Children.Insert(0, _hotReloadStatus);

            // 1. Cinematic Invert: Saturation -> Exposure -> ColorInvert(custom).
            EffectChainBrush chain1 = null;
            var s1Sat = MakeSlider("饱和度 Saturation（上游）", 0, 3, 1.6, 0.05);
            var s1Exp = MakeSlider("曝光 Exposure（中游）", 0, 2, 1.0, 0.05);
            s1Sat.ValueChanged += (s, a) => chain1?.SetScalar("Sat.Saturation", (float)a.NewValue);
            s1Exp.ValueChanged += (s, a) => chain1?.SetScalar("Exp.Exposure", (float)a.NewValue);
            AddCard(
                "1", "电影负片 Cinematic",
                "backdrop → Saturation(Sat) → Exposure(Exp) → ColorInvert(custom 0x0200)",
                "✓ 饱和+曝光后再反相；两滑块各自改变对应级",
                new[] { "ColorInvert.hlsl" },
                () =>
                {
                    var inv = new ColorInvertEffect();
                    inv.RuntimeId = Guid.NewGuid();
                    chain1 = new EffectChainBrush
                    {
                        Stages = new[]
                        {
                            Stage(() => new SaturationEffect { Name = "Sat", Saturation = (float)s1Sat.Value, Source = BackdropSource() }, "Sat.Saturation"),
                            Stage(() => new ExposureEffect { Name = "Exp", Exposure = (float)s1Exp.Value, Source = BackdropSource() }, "Exp.Exposure"),
                            Stage(() => inv.Create(), null),
                        },
                    };
                    chain1.SetScalar("Sat.Saturation", (float)s1Sat.Value);
                    chain1.SetScalar("Exp.Exposure", (float)s1Exp.Value);
                    return chain1;
                },
                Stack(s1Sat, s1Exp));

            // 2. Dreamy Bloom: GaussianBlur -> Saturation -> ColorTint(custom).
            EffectChainBrush chain2 = null;
            var s2Blur = MakeSlider("模糊 BlurAmount（上游）", 0, 40, 8, 0.5);
            var s2Sat = MakeSlider("饱和度 Saturation（中游）", 0, 3, 1.8, 0.05);
            var s2Tint = MakeSlider("染色强度 Amount（终端）", 0, 1, 0.4, 0.01);
            s2Blur.ValueChanged += (s, a) => chain2?.SetScalar("Blur.BlurAmount", (float)a.NewValue);
            s2Sat.ValueChanged += (s, a) => chain2?.SetScalar("Sat.Saturation", (float)a.NewValue);
            s2Tint.ValueChanged += (s, a) => chain2?.SetScalar(ColorTintEffect.AmountPropertyPath, (float)a.NewValue);
            AddCard(
                "2", "梦幻光晕 Bloom",
                "backdrop → GaussianBlur(Blur) → Saturation(Sat) → ColorTint(custom 0x0200)",
                "✓ 模糊光晕 + 增饱和 + 暖色染色；三滑块独立",
                new[] { "ColorTint.hlsl" },
                () =>
                {
                    var tint = new ColorTintEffect();
                    tint.RuntimeId = Guid.NewGuid();
                    chain2 = new EffectChainBrush
                    {
                        Stages = new[]
                        {
                            Stage(() => new GaussianBlurEffect { Name = "Blur", BlurAmount = (float)s2Blur.Value, Source = BackdropSource() }, "Blur.BlurAmount"),
                            Stage(() => new SaturationEffect { Name = "Sat", Saturation = (float)s2Sat.Value, Source = BackdropSource() }, "Sat.Saturation"),
                            Stage(() => tint.Create(), ColorTintEffect.AmountPropertyPath),
                        },
                    };
                    chain2.SetScalar("Blur.BlurAmount", (float)s2Blur.Value);
                    chain2.SetScalar("Sat.Saturation", (float)s2Sat.Value);
                    chain2.SetScalar(ColorTintEffect.AmountPropertyPath, (float)s2Tint.Value);
                    return chain2;
                },
                Stack(s2Blur, s2Sat, s2Tint));

            // 3. Chromatic Glass: GaussianBlur -> ChromaticAberration(custom,FlattenSource)
            //    -> ColorTint(custom). The ONLY chain consuming the single 0x0400 slot;
            //    ChromaticAberration materializes the blurred upstream into texture0.
            EffectChainBrush chain3 = null;
            var s3Blur = MakeSlider("模糊 BlurAmount（上游）", 0, 40, 4, 0.5);
            var s3Off = MakeSlider("色散偏移 Offset（custom 采样级）", 0, 0.05, 0.008, 0.001);
            s3Blur.ValueChanged += (s, a) => chain3?.SetScalar("Blur.BlurAmount", (float)a.NewValue);
            s3Off.ValueChanged += (s, a) => chain3?.SetScalar(ChromaticAberrationEffect.OffsetPropertyPath, (float)a.NewValue);
            AddCard(
                "3", "色散玻璃 Chromatic",
                "backdrop → GaussianBlur → ChromaticAberration(custom {0x0100,0x0400},FlattenSource) → ColorTint(custom)",
                "✓ 模糊纹理做 R/B 通道横向错位（色散）再暖染；调偏移看彩边",
                new[] { "ChromaticAberration.hlsl", "ColorTint.hlsl" },
                () =>
                {
                    var ca = new ChromaticAberrationEffect();
                    ca.RuntimeId = Guid.NewGuid();
                    var tint = new ColorTintEffect();
                    tint.RuntimeId = Guid.NewGuid();
                    chain3 = new EffectChainBrush
                    {
                        Stages = new[]
                        {
                            Stage(() => new GaussianBlurEffect { Name = "Blur", BlurAmount = (float)s3Blur.Value, Source = BackdropSource() }, "Blur.BlurAmount"),
                            Stage(() => ca.Create(), ChromaticAberrationEffect.OffsetPropertyPath),
                            Stage(() => tint.Create(), ColorTintEffect.AmountPropertyPath),
                        },
                    };
                    chain3.SetScalar("Blur.BlurAmount", (float)s3Blur.Value);
                    chain3.SetScalar(ChromaticAberrationEffect.OffsetPropertyPath, (float)s3Off.Value);
                    return chain3;
                },
                Stack(s3Blur, s3Off));

            // 4. Vintage Film: HueRotation -> BrightnessContrast -> Sepia -> ColorInvert.
            EffectChainBrush chain4 = null;
            var s4Hue = MakeSlider("色相 HueRotation（上游）", 0, 360, 30, 1);
            var s4Con = MakeSlider("对比度 Contrast（中游）", -1, 1, 0.2, 0.02);
            s4Hue.ValueChanged += (s, a) => chain4?.SetScalar("Hue.Angle", (float)a.NewValue);
            s4Con.ValueChanged += (s, a) => chain4?.SetScalar("Contrast.Contrast", (float)a.NewValue);
            AddCard(
                "4", "复古胶片 Vintage",
                "backdrop → HueRotation(Hue) → BrightnessContrast(Contrast) → Sepia → ColorInvert(custom)",
                "✓ 旋转色相 + 提对比 + 棕褐 + 反相；色相/对比两滑块",
                new[] { "ColorInvert.hlsl" },
                () =>
                {
                    var inv = new ColorInvertEffect();
                    inv.RuntimeId = Guid.NewGuid();
                    chain4 = new EffectChainBrush
                    {
                        Stages = new[]
                        {
                            Stage(() => new HueRotationEffect { Name = "Hue", Angle = (float)s4Hue.Value, Source = BackdropSource() }, "Hue.Angle"),
                            Stage(() => new ContrastEffect { Name = "Contrast", Contrast = (float)s4Con.Value, Source = BackdropSource() }, "Contrast.Contrast"),
                            Stage(() => new SepiaEffect { Source = BackdropSource() }, null),
                            Stage(() => inv.Create(), null),
                        },
                    };
                    chain4.SetScalar("Hue.Angle", (float)s4Hue.Value);
                    chain4.SetScalar("Contrast.Contrast", (float)s4Con.Value);
                    return chain4;
                },
                Stack(s4Hue, s4Con));

            // 5. Dual-Source: two branches (Blur + HueRotation) -> Blend(Multiply) -> ColorInvert.
            DualSourceChainBrush chain5 = null;
            var s5Blur = MakeSlider("分支A 模糊 BlurAmount", 0, 40, 6, 0.5);
            var s5Hue = MakeSlider("分支B 色相 HueRotation", 0, 360, 180, 1);
            s5Blur.ValueChanged += (s, a) => chain5?.SetScalar("Blur.BlurAmount", (float)a.NewValue);
            s5Hue.ValueChanged += (s, a) => chain5?.SetScalar("Hue.Angle", (float)a.NewValue);
            AddCard(
                "5", "双源叠影 DualSource",
                "backdrop →[A]GaussianBlur + [B]HueRotation → Blend(Multiply) → ColorInvert(custom)",
                "✓ 模糊与旋转色相两路相乘后反相；各自滑块控两支",
                new[] { "ColorInvert.hlsl" },
                () =>
                {
                    var inv = new ColorInvertEffect();
                    inv.RuntimeId = Guid.NewGuid();
                    chain5 = new DualSourceChainBrush
                    {
                        BranchAFactory = () => new GaussianBlurEffect { Name = "Blur", BlurAmount = (float)s5Blur.Value, Source = BackdropSource() },
                        BranchAPaths = new[] { "Blur.BlurAmount" },
                        BranchBFactory = () => new HueRotationEffect { Name = "Hue", Angle = (float)s5Hue.Value, Source = BackdropSource() },
                        BranchBPaths = new[] { "Hue.Angle" },
                        BlendMode = BlendEffectMode.Multiply,
                        TerminalFactory = () => inv.Create(),
                    };
                    chain5.SetScalar("Blur.BlurAmount", (float)s5Blur.Value);
                    chain5.SetScalar("Hue.Angle", (float)s5Hue.Value);
                    return chain5;
                },
                Stack(s5Blur, s5Hue));

            // 6. Channel Matrix: ColorMatrix(sepia) -> GaussianBlur -> ColorTint(custom).
            EffectChainBrush chain6 = null;
            var s6Blur = MakeSlider("模糊 BlurAmount（中游）", 0, 40, 3, 0.5);
            var s6Tint = MakeSlider("染色强度 Amount（终端）", 0, 1, 0.5, 0.01);
            s6Blur.ValueChanged += (s, a) => chain6?.SetScalar("Blur.BlurAmount", (float)a.NewValue);
            s6Tint.ValueChanged += (s, a) => chain6?.SetScalar(ColorTintEffect.AmountPropertyPath, (float)a.NewValue);
            AddCard(
                "6", "通道矩阵 Matrix",
                "backdrop → ColorMatrix(sepia) → GaussianBlur(Blur) → ColorTint(custom)",
                "✓ 棕褐矩阵重映射通道后模糊再暖染；模糊/染色两滑块",
                new[] { "ColorTint.hlsl" },
                () =>
                {
                    var tint = new ColorTintEffect();
                    tint.RuntimeId = Guid.NewGuid();
                    chain6 = new EffectChainBrush
                    {
                        Stages = new[]
                        {
                            Stage(() => new ColorMatrixEffect { Name = "Matrix", Source = BackdropSource(), ColorMatrix = SepiaMatrix() }, null),
                            Stage(() => new GaussianBlurEffect { Name = "Blur", BlurAmount = (float)s6Blur.Value, Source = BackdropSource() }, "Blur.BlurAmount"),
                            Stage(() => tint.Create(), ColorTintEffect.AmountPropertyPath),
                        },
                    };
                    chain6.SetScalar("Blur.BlurAmount", (float)s6Blur.Value);
                    chain6.SetScalar(ColorTintEffect.AmountPropertyPath, (float)s6Tint.Value);
                    return chain6;
                },
                Stack(s6Blur, s6Tint));

            SetupHotReload();
        }

        // ---- small build helpers -------------------------------------------------

        // A Win2D source bound to the chain's "Backdrop" source parameter.
        private static IGraphicsEffectSource BackdropSource() => new CompositionEffectSourceParameter("Backdrop");

        // One chain stage: a factory building a fresh effect (whose Source is BackdropSource)
        // plus the animatable path(s) it owns (null => CreateEffectFactory with no animatable set).
        private static ChainStage Stage(Func<IGraphicsEffect> factory, string animatablePath)
        {
            return new ChainStage
            {
                EffectFactory = factory,
                AnimatablePaths = string.IsNullOrEmpty(animatablePath) ? null : new[] { animatablePath },
            };
        }

        private static Slider MakeSlider(string header, double min, double max, double value, double step)
        {
            return new Slider
            {
                Header = header,
                Minimum = min,
                Maximum = max,
                Value = value,
                StepFrequency = step,
            };
        }

        // Stacks multiple slider rows into one FrameworkElement for AddCard's extra slot.
        private static StackPanel Stack(params FrameworkElement[] items)
        {
            var panel = new StackPanel { Spacing = 10 };
            foreach (var item in items)
            {
                panel.Children.Add(item);
            }
            return panel;
        }

        // Classic sepia channel-remap matrix for ColorMatrixEffect.
        private static Matrix5x4 SepiaMatrix()
        {
            return new Matrix5x4
            {
                M11 = 0.393f, M12 = 0.349f, M13 = 0.272f, M14 = 0f,
                M21 = 0.769f, M22 = 0.686f, M23 = 0.534f, M24 = 0f,
                M31 = 0.189f, M32 = 0.168f, M33 = 0.131f, M34 = 0f,
                M41 = 0f, M42 = 0f, M43 = 0f, M44 = 1f,
                M51 = 0f, M52 = 0f, M53 = 0f, M54 = 0f,
            };
        }

        // ---- card building -------------------------------------------------------

        // Builds one combo card and appends it to CardHost. makeBrush builds a fresh chain
        // (bumping each custom effect's RuntimeId to a new GUID) and is invoked for the
        // initial render AND on every hot-reload of any shader in `shaders`. extra holds the
        // card's slider(s). The fxc-status TextBlock is registered under every shader so
        // editing any of them refreshes it.
        private void AddCard(string idx, string title, string abi, string expected, string[] shaders, Func<Brush> makeBrush, FrameworkElement extra = null)
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
                Text = HslStatus(shaders[0], "PSBody"),
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

            try { overlay.Background = makeBrush(); } catch { }

            foreach (string shader in shaders)
            {
                if (!_reloadByShader.ContainsKey(shader))
                {
                    _reloadByShader[shader] = new List<Action>();
                    _statusByShader[shader] = new List<TextBlock>();
                }
                _reloadByShader[shader].Add(() => { try { overlay.Background = makeBrush(); } catch { } });
                _statusByShader[shader].Add(statusBox);
            }
        }

        // The colorful scene the backdrop-sampling effects refract/sample.
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

        // ---- shader hot-reload ---------------------------------------------------

        private void SetupHotReload()
        {
            _uiQueue = DispatcherQueue.GetForCurrentThread();
            _outputShaderDir = Path.Combine(AppContext.BaseDirectory, "Effects", "Shaders");

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

        private void OnShaderFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_uiQueue == null) return;
            string file = Path.GetFileName(e.FullPath);
            if (string.IsNullOrEmpty(file)) return;
            lock (_pendingShaders) { _pendingShaders.Add(file); }
            _uiQueue.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); });
        }

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

            if (_sourceShaderDir != _outputShaderDir)
            {
                string src = Path.Combine(_sourceShaderDir, file);
                string dst = Path.Combine(_outputShaderDir, file);
                try { if (File.Exists(src)) File.Copy(src, dst, overwrite: true); }
                catch { return; }
            }

            string status = HslStatus(file, "PSBody");
            if (_statusByShader.TryGetValue(file, out var boxes))
                foreach (var b in boxes) b.Text = status;

            int n = 0;
            foreach (var r in reloads) { try { r(); n++; } catch { } }

            if (_hotReloadStatus != null)
            {
                _hotReloadStatus.Text =
                    "🔥 [" + DateTime.Now.ToString("HH:mm:ss") + "] 已热更新 " + file +
                    " → " + n + " 张卡片重渲染\n  监听 " + _sourceShaderDir;
            }
        }

        // ---- fxc syntax check ----------------------------------------------------

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
