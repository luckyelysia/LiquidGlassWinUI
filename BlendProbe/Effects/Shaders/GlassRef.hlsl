// GlassRef — the ANALYTIC reference renderer for the Glass A/B scene. Computes every
// static field (merged SDF, analytic gradient -> normal/nLen/nAngle, edgeFactor,
// fresnel, glare) INLINE in float, and refracts the live backdrop (texture0). This is
// the "before baking" side of the comparison; it must match GlassBaked's 8-bit
// reconstruction to the eye (the constraint: visual identity, ignoring float error).
//
// Body is LiquidGlassWinUI/.../LiquidGlass.hlsl simplified for a CLEAN backdrop:
//   * localUv == uv (no GaussianBlur content-rect padding upstream -> samplerData is
//     the identity rect, so the content-rect recovery is dropped).
//   * nmerged is clamped >= 0 to kill the NaN annulus (merged in (0,0.005) would
//     otherwise asin(>1)). GlassBaked clamps identically, so the two stay in lock-step.
//   * No in-shader blur; texture0 IS the backdrop (CreateBackdropBrush).
//
// ABI mirrors MaskSamplerProbe (card A/B): 1 source Backdrop -> texture0,
// PSBody(float2 uv, float4 samplerDataExt), args {0x0100, 0x0400}, custom sampler.

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
    float ShapeRadius;         // offset 96
    float ShapeRoundness;      // offset 100
    float MergeRate;           // offset 104 (unused)
    float ShowShape1;          // offset 108 (unused)
    float SpringSizeFactor;    // offset 112 (unused)
    float BgType;              // offset 116 (unused)
    float Step;                // offset 120 (unused here; Mode in GlassBaked)
    float Dpr;                 // offset 124
};

float superellipseCornerSDF(float2 p, float r, float n)
{
    p = abs(p);
    return pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - r;
}

float RoundedRectSDF(float2 p, float2 center, float2 halfPx, float cr, float n)
{
    p -= center;
    cr = min(cr, min(halfPx.x, halfPx.y));
    float2 d = abs(p) - halfPx;
    if (d.x > -cr && d.y > -cr)
    {
        float2 cornerCenter = sign(p) * (halfPx - float2(cr, cr));
        return superellipseCornerSDF(p - cornerCenter, cr, n);
    }
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

// Unscaled analytic gradient (closed-form normal of the SDF, BEFORE the
// *(1/res.y)*sqrt2*1000 scale). GlassBaked reads this from Tex0 instead.
float2 AnalyticGradient(float2 p, float2 center, float2 halfPx, float cr, float n)
{
    p -= center;
    cr = min(cr, min(halfPx.x, halfPx.y));
    float2 d = abs(p) - halfPx;
    float2 grad;
    if (d.x > -cr && d.y > -cr)
    {
        float2 cornerCenter = sign(p) * (halfPx - float2(cr, cr));
        float2 q = p - cornerCenter;
        float2 aq = abs(q);
        float S = pow(aq.x, n) + pow(aq.y, n);
        float k = S > 1e-6 ? pow(S, 1.0 / n - 1.0) : 0.0;
        grad = sign(q) * pow(aq, float2(n - 1.0, n - 1.0)) * k;
    }
    else
    {
        grad = (d.x >= d.y) ? float2(sign(p.x), 0.0) : float2(0.0, sign(p.y));
    }
    return grad;
}

float4 GlassRefBody(float2 uv)
{
    // res from screen derivatives of uv (= localUv for a clean backdrop): for a rect
    // where uv spans [0,1], 1/(|ddx|+|ddy|) == (W,H) output px — matches the baker.
    float2 res = max(1.0 / max(abs(ddx(uv)) + abs(ddy(uv)), float2(1e-6, 1e-6)), float2(1.0, 1.0));
    float dpr = (Dpr > 0.0) ? Dpr : 1.0;
    float2 fragCoord = uv * res;

    float2 center = res * 0.5;
    float2 halfPx = res * 0.5;
    float cr = min(halfPx.x, halfPx.y) * saturate(ShapeRadius);
    float n = clamp(ShapeRoundness, 2.0, 8.0);

    float merged = RoundedRectSDF(fragCoord, center, halfPx, cr, n) / res.y;

    float3 tintRgb = float3(TintR, TintG, TintB) / 255.0;
    float tintA = TintA;

    float4 outColor;
    if (merged < 0.005)
    {
        float nmerged = max(-1.0 * (merged * res.y), 0.0); // clamp >= 0 (kills NaN annulus)
        float xR = 1.0 - nmerged / (RefThickness * dpr);
        float thetaI = asin(pow(max(xR, 0.0), 2.0));
        float thetaT = asin((1.0 / RefFactor) * sin(thetaI));
        float edgeFactor = -1.0 * tan(thetaT - thetaI);
        if (nmerged >= RefThickness * dpr) edgeFactor = 0.0;

        float4 blurredPixel;
        float2 normal = float2(0.0, 0.0);
        float nLen = 0.0;
        if (edgeFactor <= 0.0)
        {
            blurredPixel = texture0.Sample(sampler0, uv);
        }
        else
        {
            float2 g0 = AnalyticGradient(fragCoord, center, halfPx, cr, n);
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
            float fresnelHardness = RefFresnelHardness / 100.0;
            float fresnelFactorN = RefFresnelFactor / 100.0;
            float ffBase = 1.0 + nmerged / (1500.0 * dpr) * pow(500.0 / max(RefFresnelRange, 0.0001), 2.0) + fresnelHardness;
            float fresnelFactor = clamp(pow(max(ffBase, 0.0), 5.0), 0.0, 1.0);
            float3 fresnelBase = lerp(float3(1.0, 1.0, 1.0), tintRgb, tintA * 0.5);
            float lighten = clamp(0.2 * fresnelFactor * fresnelFactorN, 0.0, 1.0);
            float3 fresnelTint = lerp(fresnelBase, float3(1.0, 1.0, 1.0), lighten);
            outColor = lerp(outColor, float4(fresnelTint, 1.0), fresnelFactor * fresnelFactorN * 0.7);
        }

        if (edgeFactor > 0.0)
        {
            float glareHardness = GlareHardness / 100.0;
            float glareFactorN = GlareFactor / 100.0;
            float glareOppositeN = GlareOppositeFactor / 100.0;
            float glareConvergenceN = GlareConvergence / 100.0;
            float glareAngleNorm = GlareAngle * PI / 180.0;

            float glareGeoFactor = clamp(pow(1.0 + nmerged / (1500.0 * dpr) * pow(500.0 / max(GlareRange, 0.0001), 2.0) + glareHardness, 5.0), 0.0, 1.0);

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

export float4 PSBody(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyCC(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyCW(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyCM(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyWC(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyWW(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyWM(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyMC(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyMW(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyMM(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyC(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyW(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
export float4 PSBodyM(float2 uv, float4 samplerDataExt) { return GlassRefBody(uv); }
