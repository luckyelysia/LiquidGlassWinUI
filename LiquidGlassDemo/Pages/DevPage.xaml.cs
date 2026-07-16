using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LiquidGlassDemo.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class DevPage : Page
    {
        // ── per-param defaults (mirrors LiquidParam / LiquidGlassBrush.RegisterParam) ──
        private static readonly Dictionary<string, double> Defaults = new()
        {
            ["RefThickness"]       = 20,
            ["RefFactor"]          = 1.4,
            ["RefDispersion"]      = 1,
            ["DispersionRange"]    = 1.0,
            ["RefFresnelRange"]    = 30,
            ["RefFresnelHardness"] = 20,
            ["RefFresnelFactor"]   = 20,
            ["Magnification"]     = 1.0,
            ["GlareRange"]         = 30,
            ["GlareHardness"]      = 20,
            ["GlareFactor"]        = 90,
            ["GlareConvergence"]   = 50,
            ["GlareOppositeFactor"] = 80,
            ["GlareAngle"]         = -45,
            ["BlurAmount"]         = 1.0,
            ["TintR"]              = 255,
            ["TintG"]              = 255,
            ["TintB"]              = 255,
            ["TintA"]              = 0,
            ["BloomAmount"]        = 0.0,
            ["Exposure"]           = 1.0,
            ["Brightness"]         = 0.0,
            ["Contrast"]           = 1.0,
            ["Saturation"]         = 1.0,
            ["Temperature"]        = 0.0,
            ["Vibrance"]           = 0.0,
            ["ShapeRadius"]        = 0.4,
            ["ShapeRoundness"]     = 5,
        };

        // ── presets ───────────────────────────────────────────────────────────
        //     Each preset is a named <lg:LiquidGlassBrush> resource in the XAML.
        //     This list maps their x:Name → display name for the ComboBox.
        //     To add a preset:
        //       1. Tweak sliders → Copy XAML → paste as new resource above
        //       2. Add a ("Display Name", Preset_YourName) entry below

        private (string Label, LiquidGlassWinUI.LiquidGlassBrush Brush)[] _presets = null!;

        // Ordered list for deterministic code-gen output.
        private static readonly (string name, string xamlAttr)[] ParamOrder =
        {
            ("RefThickness",        "RefThickness"),
            ("RefFactor",           "RefFactor"),
            ("RefDispersion",       "RefDispersion"),
            ("DispersionRange",     "DispersionRange"),
            ("RefFresnelRange",     "RefFresnelRange"),
            ("RefFresnelHardness",  "RefFresnelHardness"),
            ("RefFresnelFactor",    "RefFresnelFactor"),
            ("Magnification",      "Magnification"),
            ("GlareRange",          "GlareRange"),
            ("GlareHardness",       "GlareHardness"),
            ("GlareFactor",         "GlareFactor"),
            ("GlareConvergence",    "GlareConvergence"),
            ("GlareOppositeFactor", "GlareOppositeFactor"),
            ("GlareAngle",          "GlareAngle"),
            ("BlurAmount",          "BlurAmount"),
            ("TintR",               "TintR"),
            ("TintG",               "TintG"),
            ("TintB",               "TintB"),
            ("TintA",               "TintA"),
            ("BloomAmount",         "BloomAmount"),
            ("Exposure",           "Exposure"),
            ("Brightness",         "Brightness"),
            ("Contrast",           "Contrast"),
            ("Saturation",         "Saturation"),
            ("Temperature",        "Temperature"),
            ("Vibrance",           "Vibrance"),
            ("ShapeRadius",         "ShapeRadius"),
            ("ShapeRoundness",      "ShapeRoundness"),
        };

        // Reflection handles for the params above, so the Storyboard preset
        // transition can read/write every DP by name without a giant switch.
        private static readonly PropertyInfo[] s_paramProps =
            ParamOrder.Select(p => typeof(LiquidGlassWinUI.LiquidGlassBrush).GetProperty(p.name)!).ToArray();

        public DevPage()
        {
            InitializeComponent();

            // Wire up presets from the named XAML brush resources.
            _presets = new (string, LiquidGlassWinUI.LiquidGlassBrush)[]
            {
                ("Default",       Preset_Default),
                ("Thin + Tinted", Preset_ThinTinted),
                ("Magnified",     Preset_Magnified),
            };

            foreach (var (label, _) in _presets)
                PresetPicker.Items.Add(new ComboBoxItem { Content = label });

            // Sync initial TintPicker colour from brush defaults.
            TintPicker.Color = new Windows.UI.Color
            {
                R = (byte)GlassBrush.TintR,
                G = (byte)GlassBrush.TintG,
                B = (byte)GlassBrush.TintB,
                A = (byte)(GlassBrush.TintA * 255)
            };
        }

        private bool _updatingTint;

        private void GlassHost_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            GlassTransform.X += e.Delta.Translation.X;
            GlassTransform.Y += e.Delta.Translation.Y;
        }

        private void TintPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_updatingTint) return;
            _updatingTint = true;

            var c = args.NewColor;
            GlassBrush.TintR = c.R;
            GlassBrush.TintG = c.G;
            GlassBrush.TintB = c.B;
            GlassBrush.TintA = c.A / 255.0;

            _updatingTint = false;
        }

        // ── preset picker ─────────────────────────────────────────────────
        //     Cross-fades every material parameter to the preset via a UI-thread
        //     Storyboard (one DoubleAnimation per param, EnableDependentAnimation).
        //     Unlike LiquidGlassBrush.TransitionTo (compositor-thread; the DPs only
        //     update at the very end), this writes the DPs every frame — so every
        //     TwoWay-bound slider on the right rides along with the transition.

        private Storyboard? _transitionStoryboard;
        private LiquidGlassWinUI.LiquidGlassBrush? _transitionTarget;

        private void PresetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = PresetPicker.SelectedIndex;
            if (idx < 0 || idx >= _presets.Length) return;

            var target = _presets[idx].Brush;
            TransitionToViaStoryboard(target, durationMs: 400);

            // Sync tint picker.
            _updatingTint = true;
            TintPicker.Color = new Windows.UI.Color
            {
                R = (byte)target.TintR,
                G = (byte)target.TintG,
                B = (byte)target.TintB,
                A = (byte)(target.TintA * 255),
            };
            _updatingTint = false;
        }

        // Build & run a one-shot Storyboard animating every param from its current
        // value to the preset's. Default FillBehavior (HoldEnd) pins the endpoint;
        // Completed then commits those endpoints as the base DP values and Stops the
        // hold — the Storyboard analogue of TransitionTo's Batch_Completed sync — so
        // the sliders land exactly on the preset and stay draggable afterwards.
        private void TransitionToViaStoryboard(LiquidGlassWinUI.LiquidGlassBrush target, double durationMs)
        {
            // Snapshot current (possibly mid-transition) values BEFORE cancelling
            // the previous storyboard, so From is the on-screen value.
            double[] fromValues = new double[s_paramProps.Length];
            for (int i = 0; i < s_paramProps.Length; i++)
                fromValues[i] = (double)s_paramProps[i].GetValue(GlassBrush)!;

            if (_transitionStoryboard != null)
            {
                _transitionStoryboard.Completed -= Transition_Completed;
                _transitionStoryboard.Stop();
                _transitionStoryboard = null;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);

            var sb = new Storyboard();
            for (int i = 0; i < s_paramProps.Length; i++)
            {
                double to = (double)s_paramProps[i].GetValue(target)!;
                var anim = new DoubleAnimation
                {
                    From = fromValues[i],
                    To = to,
                    Duration = duration,
                    EnableDependentAnimation = true,
                    EasingFunction = ease,
                };
                Storyboard.SetTarget(anim, GlassBrush);
                Storyboard.SetTargetProperty(anim, s_paramProps[i].Name);
                sb.Children.Add(anim);
            }

            _transitionTarget = target;
            sb.Completed += Transition_Completed;
            _transitionStoryboard = sb;
            sb.Begin();
        }

        private void Transition_Completed(object sender, object e)
        {
            if (_transitionTarget != null)
            {
                // Commit endpoints as base values, then release the HoldEnd so the
                // base values own the properties (and the sliders) again.
                foreach (var prop in s_paramProps)
                    prop.SetValue(GlassBrush, prop.GetValue(_transitionTarget));
            }

            if (_transitionStoryboard != null)
            {
                _transitionStoryboard.Completed -= Transition_Completed;
                _transitionStoryboard.Stop();
                _transitionStoryboard = null;
            }
            _transitionTarget = null;
        }

        // ── utility buttons ────────────────────────────────────────────────

        private void CopyXamlBtn_Click(object sender, RoutedEventArgs e)
        {
            var attrs = new List<string>();
            foreach (var (prop, attr) in ParamOrder)
            {
                if (IsDefault(prop)) continue;
                var val = GetBrushValue(prop);
                attrs.Add($"{attr}=\"{val:F2}\"");
            }

            string xaml;
            if (attrs.Count == 0)
            {
                xaml = "<lg:LiquidGlassBrush />";
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("<lg:LiquidGlassBrush");
                for (int i = 0; i < attrs.Count; i++)
                    sb.AppendLine($"    {attrs[i]}");
                sb.Append("/>");
                xaml = sb.ToString();
            }

            CopyToClipboard(xaml);
            FlashButton(CopyXamlBtn);
        }

        private void CopyCSharpBtn_Click(object sender, RoutedEventArgs e)
        {
            var b = GlassBrush;
            var sb = new StringBuilder();
            sb.AppendLine("// LiquidGlassBrush instance (e.g. in code-behind or view-model)");
            sb.AppendLine("var brush = new LiquidGlassBrush();");

            foreach (var (prop, _) in ParamOrder)
            {
                if (IsDefault(prop)) continue;
                var val = GetBrushValue(prop);
                sb.AppendLine($"brush.{prop} = {val:F2}f;");
            }

            CopyToClipboard(sb.ToString());
            FlashButton(CopyCSharpBtn);
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (prop, _) in ParamOrder)
            {
                if (Defaults.TryGetValue(prop, out var def))
                    SetBrushValue(prop, def);
            }

            // Reset size.
            GlassHost.Width  = 320;
            GlassHost.Height = 200;

            // Reset tint picker.
            _updatingTint = true;
            TintPicker.Color = new Windows.UI.Color
            {
                R = (byte)Defaults["TintR"],
                G = (byte)Defaults["TintG"],
                B = (byte)Defaults["TintB"],
                A = (byte)(Defaults["TintA"] * 255)
            };
            _updatingTint = false;

            FlashButton(ResetBtn, "✓ Reset!");
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private double GetBrushValue(string prop) =>
            (double)typeof(LiquidGlassWinUI.LiquidGlassBrush)
                .GetProperty(prop)!
                .GetValue(GlassBrush)!;

        private void SetBrushValue(string prop, double value) =>
            typeof(LiquidGlassWinUI.LiquidGlassBrush)
                .GetProperty(prop)!
                .SetValue(GlassBrush, value);

        private bool IsDefault(string prop) =>
            Defaults.TryGetValue(prop, out var def) &&
            Math.Abs(GetBrushValue(prop) - def) < 0.005;

        private static void CopyToClipboard(string text)
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }

        private async void FlashButton(Button btn, string flash = "✓ Copied!")
        {
            var original = btn.Content as string ?? "";
            btn.Content = flash;
            btn.IsEnabled = false;
            await System.Threading.Tasks.Task.Delay(1200);
            btn.Content = original;
            btn.IsEnabled = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RotateAnim.Begin();
            PulseAnim.Begin();
            BounceAnim.Begin();
        }
    }
}
