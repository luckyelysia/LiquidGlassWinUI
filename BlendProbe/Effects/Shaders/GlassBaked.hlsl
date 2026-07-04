// GlassBaked — the 8-bit RECONSTRUCTION side of the Glass A/B scene. Reads the CPU-baked
// static fields from two RGBA8 mask textures (sample1=Tex0, sample2=Tex1) delivered via
// the color route, reconstructs the scaled normal / nLen / nAngle from the baked gradient,
// and refracts the live backdrop (texture0). Combine + refraction are IDENTICAL to
// GlassRef; only the ORIGIN of the static fields differs (8-bit texture vs inline float).
// Match GlassRef to the eye => the lossless bake is viable.
//
// Topology (verified by MaskBindingPage card D, extended to src2):
//   src0 = Backdrop  -> texture0 (lowest-index textured source) + sample0 (color route)
//   src1 = Mask0     -> sample1 (Tex0: merged, GX, GY)
//   src2 = Mask1     -> sample2 (Tex1: edgeFactor, fresnel, glareGeo)
// args {0x0100, 0x0200, 0x0201, 0x0202, 0x0400}, PSBody(uv, sample0, sample1, sample2,
// samplerDataExt). sample0 (backdrop color) is unused by the body but kept for the
// contiguous source-index convention (matches card D).
//
// Mode (cbuffer offset 120, the "Step" slot) drives diagnostics:
//   0 = baked glass (default; the A/B output)
//   1 = raw sample1 (Tex0: R=merged G=GX B=GY)
//   2 = raw sample2 (Tex1: R=edgeFactor G=fresnel B=glareGeo)
//   3 = split sample1 | sample2
// Raw modes sanity-check the bake independent of the combine (catches bake/encode bugs vs
// real 8-bit precision loss).

#define PI 3.14159265359

Texture2D texture0;
SamplerState sampler0;

cbuffer LiquidGlassParams : register(b0)
{
    float RefThickness;        // offset 0
    float RefFactor;           // offset 4
    float RefDispersion;       // offset 8
    float RefFresnelRange;     // offset 12
    float RefFresnelHardness;  // offset 16
    float RefFresnelFactor;    // offset 20
    float GlareRange;          // offset 24
    float GlareHardness;       // offset 28
    float GlareFactor;         // offset 32
    float GlareConvergence;    // offset 36
    float GlareOppositeFactor; // offset 40
    float GlareAngle;          // offset 44
    float BlurAmount;          // offset 48  (unused)
    float BlurEdge;            // offset 52  (unused)
    float TintR;               // offset 56
    float TintG;               // offset 60
    float TintB;               // offset 64
    float TintA;               // offset 68
    float ShadowExpand;        // offset 72  (unused)
    float ShadowFactor;        // offset 76  (unused)
    float ShadowPosX;          // offset 80  (unused)
    float ShadowPosY;          // offset 84  (unused)
    float ShapeWidth;          // offset 88  (unused)
    float ShapeHeight;         // offset 92  (unused)
    float ShapeRadius;         // offset 96  (unused here; baked geometry already applied)
    float ShapeRoundness;      // offset 100 (unused here)
    float MergeRate;           // offset 104 (unused)
    float ShowShape1;          // offset 108 (unused)
    float SpringSizeFactor;    // offset 112 (unused)
    float BgType;              // offset 116 (unused)
    float Step;                // offset 120 = Mode (diagnostic)
    float Dpr;                 // offset 124
};

// Decode constants — MUST match GlassFieldBaker's encode ranges exactly.
//   Tex0: R=merged[-0.03,0.03]  G=GX[-2,2]  B=GY[-2,2]   A=255
//   Tex1: R=edgeFactor[-0.25,2.5]  G=fresnel[0,1]  B=glareGeo[0,1]  A=255
#define MERGED_MIN (-0.03)
#define MERGED_SPAN 0.06
#define GRAD_MIN (-2.0)
#define GRAD_SPAN 4.0
#define EDGE_MIN (-0.25)
#define EDGE_SPAN 2.75

float4 GlassBakedBody(float2 uv, float4 sample1, float4 sample2)
{
    float mode = Step;
    if (mode > 0.5) // raw-texture diagnostics (alpha forced to 1)
    {
        if (mode < 1.5) return float4(sample1.rgb, 1.0);
        if (mode < 2.5) return float4(sample2.rgb, 1.0);
        if (mode < 3.5) return (uv.x < 0.5) ? float4(sample1.rgb, 1.0) : float4(sample2.rgb, 1.0);
    }

    float2 res = max(1.0 / max(abs(ddx(uv)) + abs(ddy(uv)), float2(1e-6, 1e-6)), float2(1.0, 1.0));
    float dpr = (Dpr > 0.0) ? Dpr : 1.0;

    // --- decode baked static fields ---
    float merged = sample1.r * MERGED_SPAN + MERGED_MIN;
    float2 g0 = float2(sample1.g * GRAD_SPAN + GRAD_MIN, sample1.b * GRAD_SPAN + GRAD_MIN);
    float edgeFactor = sample2.r * EDGE_SPAN + EDGE_MIN;
    float fresnelFactor = sample2.g;
    float glareGeoFactor = sample2.b;

    float3 tintRgb = float3(TintR, TintG, TintB) / 255.0;
    float tintA = TintA;

    float4 outColor;
    if (merged < 0.005)
    {
        float4 blurredPixel;
        float2 normal = float2(0.0, 0.0);
        float nLen = 0.0;
        if (edgeFactor <= 0.0)
        {
            blurredPixel = texture0.Sample(sampler0, uv);
        }
        else
        {
            // reconstruct the scaled normal from the baked UNSCALED gradient, then
            // nLen = length(normal) — same op sequence as GlassRef -> bit-identical
            // except for the 8-bit error carried in g0.
            normal = g0 * (1.0 / res.y) * 1.414213562 * 1000.0;
            nLen = length(normal);
            float2 safeN = nLen > 1e-6 ? normal : float2(0.0, 0.0);
            float2 off = -safeN * edgeFactor * 0.05 * float2(res.y / res.x, 1.0);
            float nr = 1.0 + 0.02 * RefDispersion;
            float nb = 1.0 - 0.02 * RefDispersion;
            blurredPixel = float4(
                texture0.Sample(sampler0, uv + off * nr).r,
                texture0.Sample(sampler0, uv + off).g,
                texture0.Sample(sampler0, uv + off * nb).b,
                1.0);
        }
        outColor = lerp(blurredPixel, float4(tintRgb, 1.0), tintA * 0.8);

        if (edgeFactor > 0.0)
        {
            // fresnel — base weight from Tex1, live multiplier from cbuffer
            float fresnelFactorN = RefFresnelFactor / 100.0;
            float3 fresnelBase = lerp(float3(1.0, 1.0, 1.0), tintRgb, tintA * 0.5);
            float lighten = clamp(0.2 * fresnelFactor * fresnelFactorN, 0.0, 1.0);
            float3 fresnelTint = lerp(fresnelBase, float3(1.0, 1.0, 1.0), lighten);
            outColor = lerp(outColor, float4(fresnelTint, 1.0), fresnelFactor * fresnelFactorN * 0.7);
        }

        if (edgeFactor > 0.0)
        {
            // glare — geo factor from Tex1, angle from the reconstructed normal, live mults
            float glareFactorN = GlareFactor / 100.0;
            float glareOppositeN = GlareOppositeFactor / 100.0;
            float glareConvergenceN = GlareConvergence / 100.0;
            float glareAngleNorm = GlareAngle * PI / 180.0;

            float2 nNorm = nLen > 1e-6 ? normal / nLen : float2(0.0, 0.0);
            float nAngle = atan2(nNorm.y, nNorm.x);
            if (nAngle < 0.0) nAngle += 2.0 * PI;
            float glareAngle = (nAngle - PI / 4.0 + glareAngleNorm) * 2.0;

            float glareFarside = ((glareAngle > 1.5 * PI && glareAngle < 3.5 * PI) || (glareAngle < -0.5 * PI)) ? 1.0 : 0.0;
            float glareAngleFactor = (0.5 + sin(glareAngle) * 0.5) * (glareFarside > 0.5 ? 1.2 * glareOppositeN : 1.2) * glareFactorN;
            glareAngleFactor = clamp(pow(max(glareAngleFactor, 0.0), 0.1 + glareConvergenceN * 2.0), 0.0, 1.0);

            float g = glareAngleFactor * glareGeoFactor;
            float3 glareBase = lerp(blurredPixel.rgb, tintRgb, tintA * 0.5);
            float3 glareColor = lerp(glareBase, float3(1.0, 1.0, 1.0), clamp(g, 0.0, 1.0));
            outColor = lerp(outColor, float4(glareColor, 1.0), g * nLen);
        }
    }
    else
    {
        outColor = float4(0.0, 0.0, 0.0, 0.0);
    }

    outColor = lerp(outColor, float4(0.0, 0.0, 0.0, 0.0), smoothstep(-0.001, 0.001, merged));
    return outColor;
}

float4 Core(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt)
{
    return GlassBakedBody(uv, sample1, sample2);
}

export float4 PSBody(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyCC(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyCW(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyCM(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyWC(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyWW(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyWM(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyMC(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyMW(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyMM(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyC(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyW(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
export float4 PSBodyM(float2 uv, float4 sample0, float4 sample1, float4 sample2, float4 samplerDataExt) { return Core(uv, sample0, sample1, sample2, samplerDataExt); }
