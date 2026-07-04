using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlendProbe.Brushes;
using BlendProbe.Effects;
using BlendProbe.Interop;
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
    // Multi-Flatten verification page. Five probe cards — every shader/effect file
    // is DEDICATED to one card; no file is shared across cards.
    //
    // Card  shaders                           brush                        test
    // ----  --------------------------------  ---------------------------  ---------------------------
    //   1   Mf01Passthrough.hlsl              BlurChainBrush               N=1 regression
    //   2   Mf02SamplerColor.hlsl             BackdropEffectBrush           N=2 mixed wiring
    //   3   Mf03aCrossBlend.hlsl              TwoStageFlattenChainBrush     2N→2N cascade
    //       Mf03bTint.hlsl
    //   4   Mf04aCrossBlend.hlsl              FlattenRelayToColorChainBrush Flatten→Relay→NonFlatten
    //       Mf04bRelay.hlsl
    //       Mf04cInvert.hlsl
    //   5   Mf05aInvert.hlsl                  ColorToFlattenChainBrush      NonFlatten→Flatten
    //       Mf05bCrossBlend.hlsl
    //
    // SHADER HOT-RELOAD: FileSystemWatcher → debounce → mirror → rebuild with fresh GUIDs.
    public sealed partial class MultiFlattenPage : Page
    {
        private readonly Dictionary<string, List<Action>> _reloadByShader = new();
        private readonly Dictionary<string, List<TextBlock>> _statusByShader = new();
        private readonly HashSet<string> _pendingShaders = new();

        private DispatcherQueue _uiQueue;
        private DispatcherTimer _debounce;
        private FileSystemWatcher _watcher;
        private string _sourceShaderDir;
        private string _outputShaderDir;
        private TextBlock _hotReloadStatus;

        public MultiFlattenPage() { InitializeComponent(); }

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

            // ====== Card 1: N=1 Regression ==============================================

            BlurChainBrush blur1 = null;
            var s1 = MakeSlider("BlurAmount", 0, 40, 6, 0.5);
            s1.ValueChanged += (_, a) => { if (blur1 != null) blur1.BlurAmount = a.NewValue; };
            AddCard("1", "N=1 回归",
                "backdrop → GaussianBlur → Mf01Passthrough(FlattenSource, {0x0100,0x0400})",
                "✓ 模糊场景纹理。N=1 路径未被重构破坏。",
                new[] { "Mf01Passthrough.hlsl" },
                () =>
                {
                    var fx = new Mf01PassthroughEffect { RuntimeId = Guid.NewGuid() };
                    blur1 = new BlurChainBrush { DownstreamFactory = () => fx.Create(), BlurAmount = blur1?.BlurAmount ?? 6.0 };
                    return blur1;
                }, Stack(s1));

            // ====== Card 2: N=2 Mixed Wiring =============================================

            BackdropEffectBrush blend2 = null;
            var s2 = MakeSlider("Factor（0=纹理, 1=暖橙）", 0, 1, 0.5, 0.01);
            s2.ValueChanged += (_, a) =>
                blend2?.SetScalar(Mf02SamplerColorEffect.FactorPropertyPath, (float)a.NewValue);
            AddCard("2", "N=2 混合线路",
                "source0=Backdrop(sampler,0x0400), source1=ColorSrc(color,0x0201)",
                "✓ 滑块：0=texture0.Sample, 1=float4 sample1, 0.5=混合。N=2 flatten + 混合线路。",
                new[] { "Mf02SamplerColor.hlsl" },
                () =>
                {
                    var fx = new Mf02SamplerColorEffect { RuntimeId = Guid.NewGuid() };
                    blend2 = new BackdropEffectBrush
                    {
                        EffectFactory = () => fx.Create(),
                        AnimatablePaths = new[] { Mf02SamplerColorEffect.FactorPropertyPath },
                        SourceBinders = new Dictionary<string, Func<Compositor, CompositionBrush>>
                        {
                            { "Backdrop", c => c.CreateBackdropBrush() },
                            { "ColorSrc", c => c.CreateColorBrush(Orange) },
                        },
                    };
                    blend2.SetScalar(Mf02SamplerColorEffect.FactorPropertyPath, (float)s2.Value);
                    return blend2;
                }, Stack(s2));

            // ====== Card 3: 2N → 2N Cascade ==============================================

            TwoStageFlattenChainBrush chain3 = null;
            var s3a = MakeSlider("Stage0 · Mf03a · Factor（0=纹理, 1=暖橙）", 0, 1, 0.5, 0.01);
            var s3b = MakeSlider("Stage1 · Mf03b · Amount（0=无染色, 1=全乘青色）", 0, 1, 0.5, 0.01);
            s3a.ValueChanged += (_, a) =>
                chain3?.SetScalar0(Mf03aCrossBlendEffect.FactorPropertyPath, (float)a.NewValue);
            s3b.ValueChanged += (_, a) =>
                chain3?.SetScalar1(Mf03bTintEffect.AmountPropertyPath, (float)a.NewValue);
            AddCard("3", "2N → 2N 级联",
                "backdrop → Mf03a(FlattenSource,N=2) → Mf03b(FlattenSource,N=2)",
                "✓ 两个不同着色器的 N=2 FlattenSource 效果串联。Stage0=纹理+暖橙(lerp), Stage1=Stage0输出×青色(multiply)。",
                new[] { "Mf03aCrossBlend.hlsl", "Mf03bTint.hlsl" },
                () =>
                {
                    chain3 = new TwoStageFlattenChainBrush
                    {
                        Stage0Factory = () => new Mf03aCrossBlendEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage0AnimatablePaths = new[] { Mf03aCrossBlendEffect.FactorPropertyPath },
                        ColorSrcA = Orange,
                        Stage1Factory = () => new Mf03bTintEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage1AnimatablePaths = new[] { Mf03bTintEffect.AmountPropertyPath },
                        ColorSrcB = Cyan,
                    };
                    chain3.SetScalar0(Mf03aCrossBlendEffect.FactorPropertyPath, (float)s3a.Value);
                    chain3.SetScalar1(Mf03bTintEffect.AmountPropertyPath, (float)s3b.Value);
                    return chain3;
                }, Stack(s3a, s3b));

            // ====== Card 4: Flatten → Relay → Non-Flatten ================================

            FlattenRelayToColorChainBrush chain4 = null;
            var s4 = MakeSlider("Mf04a · Factor（0=纹理, 1=暖橙）", 0, 1, 0.5, 0.01);
            s4.ValueChanged += (_, a) =>
                chain4?.SetScalar(Mf04aCrossBlendEffect.FactorPropertyPath, (float)a.NewValue);
            AddCard("4", "Flatten → Relay → 非Flatten",
                "backdrop → Mf04a(FlattenSource,N=2,crossBlend) → Mf04b(relay,keepFragment=false) → Mf04c(非Flatten,invert)",
                "✓ Factor=0 出反相纹理(invert∘relay∘backdrop), Factor=1 出 #0073FF(invert∘relay∘#FF8C00)。Relay 消毒成功，下游 invert 正常消费。",
                new[] { "Mf04aCrossBlend.hlsl", "Mf04bRelay.hlsl", "Mf04cInvert.hlsl" },
                () =>
                {
                    chain4 = new FlattenRelayToColorChainBrush
                    {
                        Stage0Factory = () => new Mf04aCrossBlendEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage0AnimatablePaths = new[] { Mf04aCrossBlendEffect.FactorPropertyPath },
                        Stage0ColorSrc = Orange,
                        Stage1RelayFactory = () => new Mf04bRelayEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage2Factory = () => new Mf04cInvertEffect { RuntimeId = Guid.NewGuid() }.Create(),
                    };
                    chain4.SetScalar(Mf04aCrossBlendEffect.FactorPropertyPath, (float)s4.Value);
                    return chain4;
                }, Stack(s4));

            // ====== Card 5: Non-Flatten → Flatten ======================================

            ColorToFlattenChainBrush chain5 = null;
            var s5 = MakeSlider("Mf05b · Factor（0=反相纹理, 1=青色）", 0, 1, 0.5, 0.01);
            s5.ValueChanged += (_, a) =>
                chain5?.SetScalar(Mf05bCrossBlendEffect.FactorPropertyPath, (float)a.NewValue);
            AddCard("5", "非Flatten → Flatten",
                "backdrop → Mf05a(非Flatten,invert) → Mf05b(FlattenSource,N=2)",
                "✓ 普通 color-route 效果输出被下游 FlattenSource 的 flatten 子图物化。渲染出反相后交叉渐变。",
                new[] { "Mf05aInvert.hlsl", "Mf05bCrossBlend.hlsl" },
                () =>
                {
                    chain5 = new ColorToFlattenChainBrush
                    {
                        Stage0Factory = () => new Mf05aInvertEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage1Factory = () => new Mf05bCrossBlendEffect { RuntimeId = Guid.NewGuid() }.Create(),
                        Stage1AnimatablePaths = new[] { Mf05bCrossBlendEffect.FactorPropertyPath },
                        Stage1ColorSrc = Cyan,
                    };
                    chain5.SetScalar(Mf05bCrossBlendEffect.FactorPropertyPath, (float)s5.Value);
                    return chain5;
                }, Stack(s5));

            SetupHotReload();
        }

        // ---- helpers ----------------------------------------------------------------

        private static readonly Color Orange = Color.FromArgb(0xFF, 0xFF, 0x8C, 0x00);
        private static readonly Color Cyan   = Color.FromArgb(0xFF, 0x00, 0xCC, 0xFF);

        private static Border BuildScene()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop { Offset = 0.0, Color = Color.FromArgb(0xFF, 0x7E, 0x1F, 0xA8) });
            g.GradientStops.Add(new GradientStop { Offset = 0.5, Color = Color.FromArgb(0xFF, 0x0E, 0x8A, 0xA7) });
            g.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(0xFF, 0x13, 0x57, 0xB0) });
            var scene = new Border { Background = g, CornerRadius = new CornerRadius(8) };
            scene.Child = new TextBlock
            {
                Text = "BACKDROP\n拖拽内容对比", FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap,
            };
            return scene;
        }

        private static Slider MakeSlider(string h, double min, double max, double val, double step) =>
            new Slider { Header = h, Minimum = min, Maximum = max, Value = val, StepFrequency = step };

        private static StackPanel Stack(params FrameworkElement[] items)
        {
            var p = new StackPanel { Spacing = 10 };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        private void AddCard(string idx, string title, string abi, string expected,
            string[] shaders, Func<Brush> makeBrush, FrameworkElement extra = null)
        {
            var c = new StackPanel { Spacing = 8 };
            c.Children.Add(new TextBlock { Text = $"{idx}. {title}", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = White });
            c.Children.Add(new TextBlock { Text = "预期：" + expected, FontSize = 12, Foreground = Green, TextWrapping = TextWrapping.Wrap });
            c.Children.Add(new TextBlock { Text = abi, FontSize = 11, Foreground = Blue, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap });

            var preview = new Grid { Height = 130 };
            preview.Children.Add(BuildScene());
            var overlay = new Border { BorderBrush = SemiWhite, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            preview.Children.Add(overlay);
            c.Children.Add(preview);

            var status = new TextBlock { Text = HslStatus(shaders[0], "PSBody"), FontSize = 11, Foreground = Gray, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            c.Children.Add(status);
            if (extra != null) c.Children.Add(extra);

            var card = new Border { Background = DarkBg, BorderBrush = BorderGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = c };
            CardHost.Children.Add(card);

            try { overlay.Background = makeBrush(); } catch { }

            foreach (string shader in shaders)
            {
                if (!_reloadByShader.ContainsKey(shader)) { _reloadByShader[shader] = new List<Action>(); _statusByShader[shader] = new List<TextBlock>(); }
                _reloadByShader[shader].Add(() => { try { overlay.Background = makeBrush(); } catch { } });
                _statusByShader[shader].Add(status);
            }
        }

        // ---- hot-reload ------------------------------------------------------------

        private void SetupHotReload()
        {
            _uiQueue = DispatcherQueue.GetForCurrentThread();
            _outputShaderDir = Path.Combine(AppContext.BaseDirectory, "Effects", "Shaders");
            string devDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Effects", "Shaders"));
            _sourceShaderDir = Directory.Exists(devDir) ? devDir : _outputShaderDir;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += OnDebounceTick;
            try { _watcher = new FileSystemWatcher(_sourceShaderDir, "*.hlsl") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true }; _watcher.Changed += OnShaderFileChanged; _watcher.Created += OnShaderFileChanged; _watcher.Renamed += OnShaderFileChanged; } catch { }
            if (_hotReloadStatus != null) { bool dev = _sourceShaderDir != _outputShaderDir; _hotReloadStatus.Text = "🔥 热更新：" + _sourceShaderDir + (dev ? "\n  （编辑源 .hlsl → 镜像到输出 → 重渲染）" : ""); }
        }

        private void OnShaderFileChanged(object sender, FileSystemEventArgs e) { if (_uiQueue == null) return; string f = Path.GetFileName(e.FullPath); if (string.IsNullOrEmpty(f)) return; lock (_pendingShaders) _pendingShaders.Add(f); _uiQueue.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); }); }

        private void OnDebounceTick(object sender, object e)
        {
            _debounce.Stop();
            List<string> batch; lock (_pendingShaders) { batch = _pendingShaders.ToList(); _pendingShaders.Clear(); }
            foreach (string file in batch) ReloadShader(file);
        }

        private void ReloadShader(string file)
        {
            if (!_reloadByShader.TryGetValue(file, out var reloads) || reloads.Count == 0) return;
            if (_sourceShaderDir != _outputShaderDir) { string src = Path.Combine(_sourceShaderDir, file), dst = Path.Combine(_outputShaderDir, file); try { if (File.Exists(src)) File.Copy(src, dst, true); } catch { return; } }
            string s = HslStatus(file, "PSBody"); if (_statusByShader.TryGetValue(file, out var boxes)) foreach (var b in boxes) b.Text = s;
            int n = 0; foreach (var r in reloads) { try { r(); n++; } catch { } }
            if (_hotReloadStatus != null) _hotReloadStatus.Text = $"🔥 [{DateTime.Now:HH:mm:ss}] {file} → {n} cards";
        }

        private static string HslStatus(string file, string entry)
        {
            try
            {
                string src = ShaderSourceLoader.Load(file);
                string err = CustomEffectInterop.CompileShader(src, entry, "ps_5_0");
                if (err == null) return $"HLSL: ✓ {file}";
                bool onlySem = true; foreach (string line in err.Split('\n')) { string t = line.Trim(); if (t.Length == 0 || t.Contains("X3502") || t.Contains("missing semantics")) continue; onlySem = false; break; }
                return onlySem ? $"HLSL: ✓ {file} (X3502 expected)" : $"HLSL: ✗ {file}\n{err}";
            }
            catch (Exception ex) { return $"HLSL: ! {file} — {ex.Message}"; }
        }

        // ---- cached brushes --------------------------------------------------------
        private static readonly SolidColorBrush White     = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush Green     = new(Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC8));
        private static readonly SolidColorBrush Blue      = new(Color.FromArgb(0xFF, 0x9A, 0xCD, 0xFF));
        private static readonly SolidColorBrush SemiWhite = new(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush Gray      = new(Color.FromArgb(0xFF, 0x99, 0x99, 0x99));
        private static readonly SolidColorBrush DarkBg    = new(Color.FromArgb(0xFF, 0x26, 0x26, 0x26));
        private static readonly SolidColorBrush BorderGray= new(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
    }
}
