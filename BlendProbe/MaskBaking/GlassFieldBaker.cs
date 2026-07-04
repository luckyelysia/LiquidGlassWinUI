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
    // CPU baker for the liquid-glass STATIC fields — the backdrop-independent geometry
    // (merged SDF, analytic gradient g0, edgeFactor, fresnel, glareGeo) that the glass
    // shader otherwise re-computes per pixel. This is the heart of the tiered pre-bake
    // scheme: bake these once on the CPU into two RGBA8 textures, hand them to the
    // shader via the COLOR route (sample1=Tex0, sample2=Tex1), and leave only the live
    // backdrop refraction (texture0) in the per-frame shader.
    //
    // Field math is a VERBATIM float-port of MaskBakerProbe/GlassMath.cs (itself a port
    // of LiquidGlassWinUI/.../LiquidGlass.hlsl), so the baked values are the exact same
    // floats the analytic shader path computes inline. `float` + MathF throughout so
    // every value is IEEE-754 single (== HLSL `float`).
    //
    // Packing (alpha = 255 always — see the premultiply note below):
    //   Tex0 (sample1)  R = merged  ∈[-0.03,0.03]   (only the AA edge band needs precision;
    //                                                  deep-in/out saturate but are unused)
    //                   G = GX      ∈[-2,2]         (unscaled gradient x)
    //                   B = GY      ∈[-2,2]         (unscaled gradient y)
    //                   A = 255
    //   Tex1 (sample2)  R = edgeFactor ∈[-0.25,2.5]
    //                   G = fresnelFactor ∈[0,1]
    //                   B = glareGeoFactor ∈[0,1]
    //                   A = 255
    // The shader RECONSTRUCTS the scaled normal (and thus nLen + nAngle) from GX/GY —
    // bit-identical to the analytic path except for the 8-bit error in GX/GY, which is
    // exactly what the A/B is designed to surface. nLen/nAngle are therefore NOT baked.
    //
    // Premultiply note: the surface uploads as Premultiplied. To avoid any straight→
    // premultiply rescaling of encoded RGB, NOTHING is stored in alpha (A=255 → identity
    // premultiply). This is the same reason CpuMaskBaker pins A=255.
    public static class GlassFieldBaker
    {
        public const float PI = 3.14159265359f;
        public const float SQRT2 = 1.4142135623730951f;

        // 128-byte cbuffer, byte-identical layout to LiquidGlass.hlsl's LiquidGlassParams.
        // Mode (diagnostic) is parked in the unused "Step" slot at offset 120.
        public struct Params
        {
            public float RefThickness, RefFactor, RefDispersion;
            public float RefFresnelRange, RefFresnelHardness, RefFresnelFactor;
            public float GlareRange, GlareHardness, GlareFactor, GlareConvergence, GlareOppositeFactor, GlareAngle;
            public float TintR, TintG, TintB, TintA;
            public float ShapeRadius, ShapeRoundness;
            public float Dpr;

            public static Params Defaults() => new()
            {
                RefThickness = 20, RefFactor = 1.4f, RefDispersion = 7,
                RefFresnelRange = 30, RefFresnelHardness = 20, RefFresnelFactor = 20,
                GlareRange = 30, GlareHardness = 20, GlareFactor = 90, GlareConvergence = 50,
                GlareOppositeFactor = 80, GlareAngle = -45,
                TintR = 255, TintG = 255, TintB = 255, TintA = 0,
                ShapeRadius = 0.4f, ShapeRoundness = 5,
                Dpr = 1.0f,
            };
        }

        // Pack Params + Mode into the 128-byte (32-float) cbuffer at the shipping offsets.
        public static byte[] BuildParamConstants(in Params p, float mode = 0f)
        {
            float[] v = new float[32];
            v[0] = p.RefThickness;            // 0
            v[1] = p.RefFactor;               // 4
            v[2] = p.RefDispersion;           // 8
            v[3] = p.RefFresnelRange;         // 12
            v[4] = p.RefFresnelHardness;      // 16
            v[5] = p.RefFresnelFactor;        // 20
            v[6] = p.GlareRange;              // 24
            v[7] = p.GlareHardness;           // 28
            v[8] = p.GlareFactor;             // 32
            v[9] = p.GlareConvergence;        // 36
            v[10] = p.GlareOppositeFactor;    // 40
            v[11] = p.GlareAngle;             // 44
            // 48 BlurAmount, 52 BlurEdge — unused, stay 0
            v[14] = p.TintR;                  // 56
            v[15] = p.TintG;                  // 60
            v[16] = p.TintB;                  // 64
            v[17] = p.TintA;                  // 68
            // 72..92 shadow/shape-size — unused, stay 0
            v[24] = p.ShapeRadius;            // 96
            v[25] = p.ShapeRoundness;         // 100
            // 104..116 unused, stay 0
            v[30] = mode;                     // 120 Step -> Mode
            v[31] = p.Dpr;                    // 124
            byte[] bytes = new byte[128];
            Buffer.BlockCopy(v, 0, bytes, 0, 128);
            return bytes;
        }

        // ---- encoding ranges (must match the HLSL decode constants exactly) ----
        private const float MergedMin = -0.03f, MergedMax = 0.03f;
        private const float GradMin = -2f, GradMax = 2f;
        private const float EdgeMin = -0.25f, EdgeMax = 2.5f;

        // ---- raw byte[] bake: Tex0 + Tex1 at OUTPUT res × scale ----
        // outW/outH are OUTPUT (physical) px; we bake (outW*scale, outH*scale) texels and
        // each texel's fragCoord maps back to output-px space, so the field values use
        // res=(outW,outH) regardless of bake scale (DWM upscales the texture to fill).
        public static (byte[] tex0, byte[] tex1) Bake(int outW, int outH, float scale, in Params p)
        {
            float s = MathF.Max(scale, 0.05f);
            int w = Math.Max(1, (int)Math.Round(outW * s));
            int h = Math.Max(1, (int)Math.Round(outH * s));
            var res = new Vector2(outW, outH);

            byte[] tex0 = new byte[w * h * 4];
            byte[] tex1 = new byte[w * h * 4];
            int i0 = 0, i1 = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var fragCoord = new Vector2((x + 0.5f) / s, (y + 0.5f) / s);

                    float merged = ComputeMerged(fragCoord, res, p);
                    Vector2 g0 = ComputeGradient(fragCoord, res, p);
                    // nmerged clamped >= 0 to kill the NaN annulus (merged in (0,0.005)
                    // would otherwise asin(>1)); factors are only used inside the shape.
                    float nmerged = MathF.Max(-merged * res.Y, 0f);
                    float edgeFactor = ComputeEdgeFactor(nmerged, p);
                    float fresnel = ComputeFresnelFactor(nmerged, p);
                    float glareGeo = ComputeGlareGeoFactor(nmerged, p);

                    // logical RGBA -> BGRA byte order (B8G8R8A8UIntNormalized memory layout).
                    // shader reads .r/.g/.b/.a; baker writes R at byte+2, G at +1, B at +0.
                    tex0[i0 + 0] = F2B(Enc(g0.Y, GradMin, GradMax));  // B <- GY
                    tex0[i0 + 1] = F2B(Enc(g0.X, GradMin, GradMax));  // G <- GX
                    tex0[i0 + 2] = F2B(Enc(merged, MergedMin, MergedMax)); // R <- merged
                    tex0[i0 + 3] = 255;                                // A
                    tex1[i1 + 0] = F2B(Enc(glareGeo, 0f, 1f));        // B <- glareGeo
                    tex1[i1 + 1] = F2B(Enc(fresnel, 0f, 1f));         // G <- fresnel
                    tex1[i1 + 2] = F2B(Enc(edgeFactor, EdgeMin, EdgeMax)); // R <- edgeFactor
                    tex1[i1 + 3] = 255;                                // A
                    i0 += 4; i1 += 4;
                }
            }
            return (tex0, tex1);
        }

        // ---- surface/brush plumbing (mirrors CpuMaskBaker; two surfaces, one element) ----

        // Bake at an explicit DIP size and return the two uploaded surfaces.
        public static (CompositionDrawingSurface t0, CompositionDrawingSurface t1) BakeToSurfaces(
            Compositor compositor, double widthDip, double heightDip, float scale,
            float dpi = 96f, Params? p = null)
        {
            Params pp = p ?? Params.Defaults();
            int outW = Math.Max(1, (int)Math.Round(widthDip * (dpi / 96f)));
            int outH = Math.Max(1, (int)Math.Round(heightDip * (dpi / 96f)));
            var (b0, b1) = Bake(outW, outH, scale, pp);
            float s = MathF.Max(scale, 0.05f);
            int w = Math.Max(1, (int)Math.Round(outW * s));
            int h = Math.Max(1, (int)Math.Round(outH * s));
            return (UploadSurface(compositor, b0, w, h), UploadSurface(compositor, b1, w, h));
        }

        // Drive TWO existing CompositionSurfaceBrushes from a FrameworkElement: bake at the
        // element's real rect × scale now, re-bake (swap .Surface) on SizeChanged. Caller
        // creates + binds both brushes ONCE (no source re-bind -> no animatable-scalar
        // reset; see memory setsourceparameter-resets-animatable-scalar).
        public static void BakeToBrushes(
            Compositor compositor, FrameworkElement element, float scale,
            CompositionSurfaceBrush brush0, CompositionSurfaceBrush brush1,
            Params? p = null)
        {
            Params pp = p ?? Params.Defaults();
            float dpi = DpiOf(element);

            void apply()
            {
                double w = element.ActualWidth > 0 ? element.ActualWidth : 256;
                double h = element.ActualHeight > 0 ? element.ActualHeight : 256;
                var (s0, s1) = BakeToSurfaces(compositor, w, h, scale, dpi, pp);
                brush0.Surface = s0;
                brush1.Surface = s1;
            }

            apply();
            void onSizeChanged(object s, SizeChangedEventArgs _)
            {
                try { apply(); } catch { /* keep one resize failure quiet */ }
            }
            element.SizeChanged += onSizeChanged;
        }

        // ---- Win2D upload: byte[] -> CanvasBitmap -> CompositionDrawingSurface ----
        private static CompositionDrawingSurface UploadSurface(Compositor compositor, byte[] bytes, int w, int h)
        {
            var device = CanvasDevice.GetSharedDevice();
            var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
            var bitmap = CanvasBitmap.CreateFromBytes(device, bytes, w, h, DirectXPixelFormat.B8G8R8A8UIntNormalized);

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

        // ---- encode/decode helpers ----
        private static float Enc(float v, float min, float max) => Saturate((v - min) / (max - min));
        private static byte F2B(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        // ---- field math: verbatim float-port of GlassMath.cs ----

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

        private static Vector2 AnalyticGradient(Vector2 p, Vector2 center, Vector2 halfPx, float cr, float n)
        {
            p -= center;
            cr = MathF.Min(cr, MathF.Min(halfPx.X, halfPx.Y));
            Vector2 d = Vector2.Abs(p) - halfPx;
            Vector2 grad;
            if (d.X > -cr && d.Y > -cr)
            {
                Vector2 cornerCenter = Sgn(p) * (halfPx - new Vector2(cr, cr));
                Vector2 q = p - cornerCenter;
                Vector2 aq = Vector2.Abs(q);
                float S = MathF.Pow(aq.X, n) + MathF.Pow(aq.Y, n);
                float k = S > 1e-6f ? MathF.Pow(S, 1.0f / n - 1.0f) : 0.0f;
                grad = Sgn(q) * new Vector2(MathF.Pow(aq.X, n - 1f), MathF.Pow(aq.Y, n - 1f)) * k;
            }
            else
            {
                grad = (d.X >= d.Y) ? new Vector2(Sgn(p.X), 0f) : new Vector2(0f, Sgn(p.Y));
            }
            return grad;
        }

        private static void ShapeParams(Vector2 res, in Params p, out Vector2 center, out Vector2 halfPx, out float cr, out float n)
        {
            center = res * 0.5f;
            halfPx = res * 0.5f;
            cr = MathF.Min(halfPx.X, halfPx.Y) * Saturate(p.ShapeRadius);
            n = Clamp(p.ShapeRoundness, 2f, 8f);
        }

        public static float ComputeMerged(Vector2 fragCoord, Vector2 res, in Params p)
        {
            ShapeParams(res, p, out var center, out var halfPx, out var cr, out var n);
            return RoundedRectSDF(fragCoord, center, halfPx, cr, n) / res.Y;
        }

        public static Vector2 ComputeGradient(Vector2 fragCoord, Vector2 res, in Params p)
        {
            ShapeParams(res, p, out var center, out var halfPx, out var cr, out var n);
            return AnalyticGradient(fragCoord, center, halfPx, cr, n);
        }

        public static float ComputeEdgeFactor(float nmerged, in Params p)
        {
            float dpr = p.Dpr > 0f ? p.Dpr : 1f;
            float xR = 1.0f - nmerged / (p.RefThickness * dpr);
            float thetaI = MathF.Asin(MathF.Pow(MathF.Max(xR, 0f), 2f));
            float thetaT = MathF.Asin((1.0f / p.RefFactor) * MathF.Sin(thetaI));
            float edgeFactor = -1.0f * MathF.Tan(thetaT - thetaI);
            if (nmerged >= p.RefThickness * dpr) edgeFactor = 0.0f;
            return edgeFactor;
        }

        public static float ComputeFresnelFactor(float nmerged, in Params p)
        {
            float dpr = p.Dpr > 0f ? p.Dpr : 1f;
            float fresnelHardness = p.RefFresnelHardness / 100.0f;
            float ffBase = 1.0f + nmerged / (1500.0f * dpr) * MathF.Pow(500.0f / MathF.Max(p.RefFresnelRange, 0.0001f), 2.0f) + fresnelHardness;
            return Clamp(MathF.Pow(MathF.Max(ffBase, 0f), 5.0f), 0f, 1f);
        }

        public static float ComputeGlareGeoFactor(float nmerged, in Params p)
        {
            float dpr = p.Dpr > 0f ? p.Dpr : 1f;
            float glareHardness = p.GlareHardness / 100.0f;
            return Clamp(MathF.Pow(1.0f + nmerged / (1500.0f * dpr) * MathF.Pow(500.0f / MathF.Max(p.GlareRange, 0.0001f), 2.0f) + glareHardness, 5.0f), 0f, 1f);
        }
    }
}
