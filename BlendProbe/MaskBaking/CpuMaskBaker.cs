using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace BlendProbe.MaskBaking
{
    // CPU mask baker. Computes a mask field on the CPU using the SAME rounded-rect SDF
    // as LiquidGlassWinUI/Effects/Shaders/LiquidGlass.hlsl (and MaskBakerProbe), then
    // uploads it to a CompositionSurfaceBrush via Win2D. Self-contained — no cross-
    // project reference — so it stays a standalone diagnostic.
    //
    // Per the user's design:
    //   * The mask does NOT need a 1:1 (full output) resolution. The rounded-rect SDF is
    //     a smooth field, so it bakes at a FRACTION of the output size and DWM upsamples
    //     it with its built-in bilinear filter (aspect-correct => no stretch, no visible
    //     quality loss on the smooth field). `scale` is that fraction (0.5 = half-res).
    //   * The bake MUST match the destination's aspect ratio. The element overload reads
    //     the UIElement's ActualSize×DPI and re-bakes on SizeChanged so the rounded rect
    //     always fills the element's real rect (like the shader's res = control px size).
    //
    // Packing (logical RGBA = what the shader reads via texture0.Sample):
    //   R = x/W   position encoding — verifies 1:1 sampling (gradient aligns with output).
    //   G = y/H   position encoding.
    //   B = merged rounded-rect SDF mapped to [0,1] — a filled silhouette with an ~8px
    //             gradient edge; sharp + correctly placed => mask data survives the path.
    //   A = 255.
    public static class CpuMaskBaker
    {
        // ---- raw byte[] bake (the SDF math; reused by both brush overloads) ----

        // Returns a BGRA8 byte[] of length w*h*4. `radius`/`roundness` mirror the
        // LiquidGlass ShapeRadius/ShapeRoundness params (defaults 0.4 / 5).
        public static byte[] Bake(int w, int h, float radius = 0.4f, float roundness = 5f)
        {
            // Match LiquidGlass.hlsl: the glass IS the brush rect — one rounded rect
            // filling `res`, isotropic superellipse corners, cr = fraction of the SHORTER
            // half-side (so corners stay round for non-square shapes). res=(w,h) here is
            // the bake resolution; aspect-correctness is the caller's job.
            var res = new Vector2(w, h);
            var center = res * 0.5f;
            var halfPx = res * 0.5f;

            float cr = MathF.Min(halfPx.X, halfPx.Y) * Saturate(radius);
            float n = Clamp(roundness, 2f, 8f);

            float resY = h;
            // `merged` is normalized by res.Y, so `merged * resY` is in pixels. 0.125
            // gives an ~4px half-band gradient straddling the edge (8px total). At a
            // sub-1:1 bake this band narrows in OUTPUT px after upscale, but the SDF edge
            // itself is resolution-independent; scale mostly affects the diagnostic's
            // gradient width, not the silhouette.
            float bScale = resY * 0.125f;

            byte[] data = new byte[w * h * 4];
            int p = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var fragCoord = new Vector2(x + 0.5f, y + 0.5f);
                    float merged = RoundedRectSDF(fragCoord, center, halfPx, cr, n) / resY;

                    float r = (float)x / w;                                   // position encoding
                    float g = (float)y / h;
                    float bb = Saturate(0.5f - merged * bScale);              // filled rounded-rect, AA edge

                    // logical RGBA -> BGRA byte order (B8G8R8A8UIntNormalized memory layout)
                    data[p + 0] = F2B(bb);   // B
                    data[p + 1] = F2B(g);    // G
                    data[p + 2] = F2B(r);    // R
                    data[p + 3] = 255;       // A
                    p += 4;
                }
            }
            return data;
        }

        // ---- surface / brush overloads ----
        //
        // IMPORTANT (the bug these shapes avoid): a CompositionEffectBrush built from a
        // factory with animatable properties does NOT tolerate post-connection
        // SetSourceParameter — it resets the constant-buffer / animatable scalar (the
        // slider goes dead). So a re-bake must NEVER re-bind the source. Instead the
        // caller creates ONE CompositionSurfaceBrush, binds it ONCE (via the effect's
        // source parameter), and a re-bake only swaps that brush's `.Surface` (a live
        // property — no effect relink, animatable scalar untouched). BakeToSurface feeds
        // that pattern; the element overload drives it on SizeChanged.

        // Bake at an EXPLICIT DIP size (× DPI × scale -> bake px) and return just the
        // uploaded surface. Caller owns the CompositionSurfaceBrush and sets `.Surface`.
        public static CompositionDrawingSurface BakeToSurface(
            Compositor compositor, double widthDip, double heightDip, float scale,
            float dpi = 96f, float radius = 0.4f, float roundness = 5f)
        {
            // DIP -> bake px: output physical px = DIP * dpi/96; bake at that × scale.
            float s = MathF.Max(scale, 0.05f); // clamp: never let it collapse to 0
            int w = Math.Max(1, (int)Math.Round(widthDip * (dpi / 96f) * s));
            int h = Math.Max(1, (int)Math.Round(heightDip * (dpi / 96f) * s));
            return UploadSurface(compositor, Bake(w, h, radius, roundness), w, h);
        }

        // One-shot explicit-size bake that returns its own CompositionSurfaceBrush. Use
        // for fixed-size destinations with no live resize.
        public static CompositionSurfaceBrush BakeToBrush(
            Compositor compositor, double widthDip, double heightDip, float scale,
            float dpi = 96f, float radius = 0.4f, float roundness = 5f)
        {
            var brush = compositor.CreateSurfaceBrush();
            brush.Stretch = CompositionStretch.Fill; // surface is aspect-correct => Fill = no distortion
            brush.Surface = BakeToSurface(compositor, widthDip, heightDip, scale, dpi, radius, roundness);
            return brush;
        }

        // Drive an EXISTING CompositionSurfaceBrush from a FrameworkElement: bake at the
        // element's real rect × scale now, and re-bake (swap `.Surface`) on SizeChanged.
        // The brush is created + bound ONCE by the caller (and reused across hot-reload
        // rebuilds); only its Surface changes here — so no source re-bind, no animatable-
        // scalar reset. `targetBrush` should already be bound as the effect's source.
        public static void BakeToBrush(
            Compositor compositor, FrameworkElement element, float scale,
            CompositionSurfaceBrush targetBrush,
            float radius = 0.4f, float roundness = 5f)
        {
            float dpi = DpiOf(element);

            void apply()
            {
                double w = element.ActualWidth > 0 ? element.ActualWidth : 256;
                double h = element.ActualHeight > 0 ? element.ActualHeight : 130;
                targetBrush.Surface = BakeToSurface(compositor, w, h, scale, dpi, radius, roundness);
            }

            apply();
            void onSizeChanged(object s, SizeChangedEventArgs _)
            {
                try { apply(); } catch { /* keep one resize failure quiet */ }
            }
            element.SizeChanged += onSizeChanged;
            // (No detach handle: the probe page owns element + brush for its lifetime.)
        }

        // ---- Win2D upload: byte[] -> CanvasBitmap -> CompositionDrawingSurface ----
        private static CompositionDrawingSurface UploadSurface(Compositor compositor, byte[] bytes, int w, int h)
        {
            // NOTE: CanvasComposition lives in Microsoft.Graphics.Canvas.UI.Composition.
            // CanvasBitmap uses the *Windows.Graphics.DirectX* pixel-format enum while
            // CreateDrawingSurface uses *Microsoft.Graphics.DirectX* (two distinct enum
            // types, same member names) — see memory win2d-effect-api-quirks.
            var device = CanvasDevice.GetSharedDevice();
            var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
            var bitmap = CanvasBitmap.CreateFromBytes(device, bytes, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);

            // CreateDrawingSession DPI defaults to 96, so a surface sized in physical px
            // and a DrawImage of the w×h bitmap land 1:1 (no DPI scaling).
            var surface = graphicsDevice.CreateDrawingSurface(
                new Size(w, h),
                Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            using (var ds = CanvasComposition.CreateDrawingSession(surface))
            {
                ds.DrawImage(bitmap);
            }
            return surface;
        }

        // Element DPI: honor the element's XamlRoot rasterization scale, else 96.
        private static float DpiOf(FrameworkElement element)
        {
            try
            {
                if (element.XamlRoot != null)
                {
                    return 96f * (float)element.XamlRoot.RasterizationScale;
                }
            }
            catch { /* fall through to 96 */ }
            return 96f;
        }

        // ---- rounded-rect SDF: verbatim float-port from LiquidGlass.hlsl ----

        private static float Sgn(float x) => x < 0f ? -1f : (x > 0f ? 1f : 0f);
        private static Vector2 Sgn(Vector2 v) => new(Sgn(v.X), Sgn(v.Y));
        private static float Clamp(float x, float a, float b) => x < a ? a : (x > b ? b : x);
        private static float Saturate(float x) => Clamp(x, 0f, 1f);

        private static float SuperellipseCornerSDF(Vector2 p, float r, float n)
        {
            p = Vector2.Abs(p);
            return MathF.Pow(MathF.Pow(p.X, n) + MathF.Pow(p.Y, n), 1.0f / n) - r;
        }

        private static float RoundedRectSDF(Vector2 p, Vector2 center, Vector2 halfPx, float cr, float n)
        {
            p -= center;
            cr = MathF.Min(cr, MathF.Min(halfPx.X, halfPx.Y));
            Vector2 d = Vector2.Abs(p) - halfPx;
            if (d.X > -cr && d.Y > -cr)
            {
                Vector2 cornerCenter = Sgn(p) * (halfPx - new Vector2(cr, cr));
                return SuperellipseCornerSDF(p - cornerCenter, cr, n);
            }
            return MathF.Min(MathF.Max(d.X, d.Y), 0.0f) + Vector2.Max(d, Vector2.Zero).Length();
        }

        private static byte F2B(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }
    }
}
