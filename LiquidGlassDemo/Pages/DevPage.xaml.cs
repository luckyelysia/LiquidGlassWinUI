using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            ["RefDispersion"]      = 7,
            ["DispersionRange"]    = 1.0,
            ["RefFresnelRange"]    = 30,
            ["RefFresnelHardness"] = 20,
            ["RefFresnelFactor"]   = 20,
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
            ["ShapeRadius"]        = 0.4,
            ["ShapeRoundness"]     = 5,
        };

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
            ("ShapeRadius",         "ShapeRadius"),
            ("ShapeRoundness",      "ShapeRoundness"),
        };

        public DevPage()
        {
            InitializeComponent();

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
    }
}
