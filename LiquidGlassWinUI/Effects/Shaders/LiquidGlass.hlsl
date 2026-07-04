// LiquidGlass — control-oriented PRUNED liquid-glass material. Same visual
// algorithms as LiquidGlassStudio (refraction, chromatic dispersion, fresnel rim,
// directional glare, tint, edge AA — NO in-shader shadow; see below), but the GEOMETRY precompute is
// replaced: a control is ONE fixed rounded rect with ISOTROPIC superellipse corners
// (no circle, no smooth-min metaball merge), so the surface normal is the CLOSED-FORM
// gradient of that SDF instead of 4 finite-difference taps. The corner radius is a
// single isotropic pixel value, so corners stay round for non-square shapes (an
// anisotropic pure superellipse stretches its corners with the aspect ratio). This
// cuts the SDF+normal cost from ~100 to ~25
// ops (frees ~80 ops, banked here as headroom for a later LCH / in-shader-blur
// upgrade). Coordinates are control-local: the superellipse is centered in the
// brush and sized in real pixels (no p1/p2, no res.y-normalization indirection).
//
// Pipeline and material blocks are identical to Studio:
//   backdrop -> GaussianBlurEffect (upstream) -> THIS (FlattenSource on)
// so texture0 is the ALREADY-BLURRED backdrop; this shader does no in-shader blur.
//
// Per pixel inside the glass shape: superellipse SDF + analytical normal ->
// refraction of the blurred source (per-channel chromatic dispersion, RefDispersion),
// base tint, fresnel rim, directional glare. Outside: fully transparent — a
// control's drop shadow lives OUTSIDE the brush rect (drawn by the platform, e.g. a
// XAML Shadow on a larger host), so rendering it in-shader would only be clipped at
// the control edge. Fresnel/glare
// tint-lightening uses the same cheap RGB approximations as Studio (the SRGB->LCH
// forward chain still exceeds DWM's budget).
//
// cbuffer is byte-identical to LiquidGlassStudio (128 bytes / 32 floats) so the
// effect registration/layout is unchanged and this file is a drop-in preview for the
// Studio harness. Semantics that differ: ShapeRadius is a 0..1 corner-radius
// fraction of the shorter half-side; ShapeWidth/Height are UNUSED (the glass fills
// the brush rect = the control, sized by res at any DPI). ShowShape1/MergeRate/
// BlurEdge/SpringSizeFactor/BgType/Step are unused (BlurAmount drives the upstream
// GaussianBlur). dpr is read from cbuffer slot 124 (the brush sets it from the
// window DPI) and scales the band widths (RefThickness / fresnel / glare) so slider
// values read as logical px; the refraction magnitude is DPI-neutral on its own.

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
    float BlurAmount;          // offset 48  (unused in-shader; drives upstream blur)
    float Exposure;            // offset 52 (backdrop brightness gain, 0.6-1.6, default 1.0)
    float TintR;               // offset 56
    float TintG;               // offset 60
    float TintB;               // offset 64
    float TintA;               // offset 68
    float ShadowExpand;        // offset 72  (unused; shadow drawn outside the brush)
    float ShadowFactor;        // offset 76  (unused)
    float ShadowPosX;          // offset 80  (unused)
    float ShadowPosY;          // offset 84  (unused)
    float ShapeWidth;          // offset 88  (unused; glass fills the brush rect via res*0.5)
    float ShapeHeight;         // offset 92  (unused)
    float ShapeRadius;         // offset 96  (repurposed: 0..1 corner-radius fraction of the shorter half-side)
    float ShapeRoundness;      // offset 100 (superellipse exponent n; ~5 = Apple squircle)
    float MergeRate;           // offset 104 (unused; no merge)
    float ShowShape1;          // offset 108 (unused; no circle)
    float SpringSizeFactor;    // offset 112 (unused)
    float DispersionRange;     // offset 116 (0=no dispersion, 1=full; default 1)
    float Step;                // offset 120 (unused)
    float Dpr;                 // offset 124 (physical px per logical px; brush sets it from window DPI)
};

// Isotropic superellipse corner SDF: cr is a PIXEL radius, identical on every
// corner regardless of the shape's width/height, so corners stay round ("正") for
// non-square shapes. (An anisotropic pure superellipse |u.x|^n+|u.y|^n=1 with
// per-axis normalization stretches its corners with the aspect ratio -> elliptical,
// not round, corners — the bug this replaces.)
float superellipseCornerSDF(float2 p, float r, float n)
{
    p = abs(p);
    return pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - r;
}

// Rounded rect with isotropic superellipse corners. cr is a pixel corner radius;
// half = (W/2, H/2) pixels. Returns px distance. Same decomposition as
// LiquidGlassStudio (which never had the aspect-corner bug) minus the circle +
// smooth-min merge. caller divides by res.y to get the res.y-normalized `merged`.
float RoundedRectSDF(float2 p, float2 center, float2 half0, float cr, float n)
{
    p -= center;
    cr = min(cr, min(half0.x, half0.y));   // never larger than the shorter half-side
    float2 d = abs(p) - half0;
    if (d.x > -cr && d.y > -cr)
    {
        float2 cornerCenter = sign(p) * (half0 - float2(cr, cr));
        return superellipseCornerSDF(p - cornerCenter, cr, n);
    }
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

// Closed-form gradient of RoundedRectSDF = the limit of Studio's 4-tap finite-
// difference normal. Corner region -> isotropic superellipse gradient; flat faces
// -> axis-aligned outward gradient (a flat face's normal IS axis-aligned, and flat
// faces barely refract, so precision there is irrelevant). Scaled to Studio's
// magnitude convention (grad / res.y * sqrt2 * 1000). O(1), no SDF re-taps.
float2 AnalyticNormal(float2 fragCoord, float2 center, float2 halfPx, float cr, float n, float2 res)
{
    float2 p = fragCoord - center;
    cr = min(cr, min(halfPx.x, halfPx.y));
    float2 d = abs(p) - halfPx;
    float2 grad;
    if (d.x > -cr && d.y > -cr)
    {
        // corner: isotropic superellipse gradient about this corner's center
        float2 cornerCenter = sign(p) * (halfPx - float2(cr, cr));
        float2 q  = p - cornerCenter;
        float2 aq = abs(q);
        float S   = pow(aq.x, n) + pow(aq.y, n);
        float k   = S > 1e-6 ? pow(S, 1.0 / n - 1.0) : 0.0;   // guard exact corner center
        grad = sign(q) * pow(aq, float2(n - 1.0, n - 1.0)) * k;
    }
    else
    {
        // flat face: outward along the axis closest to its edge (the larger d)
        grad = (d.x >= d.y) ? float2(sign(p.x), 0.0) : float2(0.0, sign(p.y));
    }
    return grad * (1.0 / res.y) * 1.414213562 * 1000.0;
}

// Smooth outward edge-normal — soft inverse-distance blend of the four edge
// normals (left / right / top / bottom).  Unlike the analytic SDF gradient,
// this rotates smoothly through corners and across the medial axis with no
// branch-switch kinks, matching the reference shader's SmoothNormal.  tau
// controls the blending radius (larger = wider smooth zone); here it scales
// with the refraction band so the smoothing adapts to RefThickness.
float2 SmoothNormal(float2 fragCoord, float2 center, float2 halfPx, float tau)
{
    float px = fragCoord.x - center.x;
    float py = fragCoord.y - center.y;
    float t = max(tau, 1e-3);
    float wl = exp(-(halfPx.x + px) / t); // left edge
    float wr = exp(-(halfPx.x - px) / t); // right edge
    float wt = exp(-(halfPx.y - py) / t); // top edge
    float wb = exp(-(halfPx.y + py) / t); // bottom edge
    float w = wl + wr + wt + wb;
    // outward normals: left→(-1,0) right→(+1,0) top→(0,+1) bottom→(0,-1)
    return float2((wr - wl) / w, (wt - wb) / w);
}

// FlattenSource — color-input passthrough for the flatten subgraph. With the
// effect's flattenSourceBeforeCustomSampler flag on, DWM wraps the (composed)
// source brush — here backdrop -> GaussianBlurEffect — in this passthrough to
// materialize it into a real intermediate texture, which the glass sampler then
// reads as texture0. The blur is done UPSTREAM, so the glass shader samples the
// already-blurred texture directly (no in-shader blur).
export float4 FlattenSource(float4 sample0)
{
    return sample0;
}

float4 LiquidGlassBody(float2 uv, float4 samplerDataExt, float4 samplerData)
{
    // Recover the effective content rect from samplerData and remap raw uv into
    // [0,1] over the panel (same as Studio). GaussianBlur expands its intermediate
    // into padded surfaces as BlurAmount grows, so raw uv no longer spans [0,1] over
    // the panel; localUv is the content-space coordinate and its screen derivatives
    // give the true output size regardless of padding. Texture sampling uses raw uv.
    float2 contentMin = min(samplerData.xy, samplerData.zw);
    float2 contentMax = max(samplerData.xy, samplerData.zw);
    float2 contentUvSizeRaw = contentMax - contentMin;
    bool hasContentRect = all(contentUvSizeRaw > 1e-6.xx);
    float2 contentUvSize = hasContentRect ? contentUvSizeRaw : 1.0.xx;
    float2 localUv = hasContentRect ? ((uv - contentMin) / contentUvSize) : uv;
    //float2 localUv = uv / (float2(ddx(uv.x), ddy(uv.y)) * samplerDataExt.xy);
   

    float2 res = max(1.0 / max(abs(ddx(localUv)) + abs(ddy(localUv)), float2(1e-6, 1e-6)), 1.0);
    float dpr = (Dpr > 0.0) ? Dpr : 1.0;   // physical px per logical px (0/unset -> 1)
    float2 fragCoord = localUv * res;

    // Control-local geometry: the glass IS the brush rect — one rounded rect with
    // isotropic superellipse corners filling res, so it sizes to the control
    // automatically at any DPI (no Width/Height params). ShapeRadius is a 0..1
    // corner-radius fraction of the shorter half-side (keep < 1 so flat sides
    // exist; =1 collapses to a pure superellipse -> aspect-corner bug for non-square).
    float2 center = res * 0.5;
    float2 halfPx = res * 0.5;
    float cr = min(halfPx.x, halfPx.y) * saturate(ShapeRadius);
    float n = clamp(ShapeRoundness, 2.0, 8.0);

    float merged = RoundedRectSDF(fragCoord, center, halfPx, cr, n) / res.y;

    // AA width — computed early so the glass-branch threshold can cover the full
    // transition. Corner proximity widens AA at curved corners (1.5→3.0 px) where
    // the eye is more sensitive to stair-stepping.
    float2 dCorner = abs(fragCoord - center) - halfPx;
    float cornerProximity = saturate((dCorner.x + cr) / max(cr, 1.0)) * saturate((dCorner.y + cr) / max(cr, 1.0));
    float aaMult = lerp(1.5, 3.0, cornerProximity);
    float aaWidth = fwidth(merged) * aaMult;
    float glassThreshold = max(0.005, aaWidth * 2.0);

    float3 tintRgb = float3(TintR, TintG, TintB) / 255.0;
    float tintA = TintA;

    float4 outColor;
    if (merged < glassThreshold)
    {
        float nmerged = -1.0 * (merged * res.y);
        float xR = 1.0 - nmerged / (RefThickness * dpr);
        float thetaI = asin(pow(saturate(xR), 2.0));
        float thetaT = asin((1.0 / RefFactor) * sin(thetaI));
        float edgeFactor = -1.0 * tan(thetaT - thetaI);
        if (nmerged >= RefThickness * dpr) edgeFactor = 0.0;

        // Smooth outward normal (reference's SmoothNormal): soft inverse-distance
        // blend of the four edge normals.  Unlike the analytic SDF gradient, this
        // rotates smoothly through corners and across the medial axis — no
        // branch-switch kinks that would make the refraction direction jump
        // between adjacent pixels.  tau scales with RefThickness (matching the
        // reference: wide band → wide smoothing zone).
        float tau = RefThickness * dpr * 0.5;
        float2 normal = SmoothNormal(fragCoord, center, halfPx, tau);
        // Scale to match the original AnalyticNormal magnitude convention for
        // glare (nLen used in saturate(g * nLen)).  The direction (safeN) is
        // length-invariant.
        normal *= (1.0 / res.y) * 1.414213562 * 1000.0;
        float nLen = length(normal);
        // Refraction offset — always computed (no nmerged gate). The reference
        // shader computes offset = sign * Amount * pow(1-t, Power) unconditionally,
        // relying on alpha blending for the AA transition. Gating on nmerged>0
        // creates a discontinuity: pixels just outside the geometric edge get
        // identity samples (no refraction) while adjacent interior pixels get full
        // refraction — a visible "refraction cliff" at the outermost 1 px.
        float2 safeN = nLen > 1e-6 ? normal : float2(0.0, 0.0);
        // refraction magnitude is DPI-neutral (normal ~ 1/res.y cancels the
        // uv->physical scaling), so dpr is NOT applied here — only the band
        // widths (RefThickness / fresnel / glare) scale by dpr for logical-px.
        float2 off = -safeN * edgeFactor * 0.05 * float2(res.y / res.x, 1.0);

        // Analytical spread: finite-difference edgeFactor at nmerged vs nmerged+1.
        // This gives the exact per-pixel rate-of-change of the refraction offset
        // along the SDF gradient, independent of screen-space pixel-grid alignment.
        // The reference shader's spread = |d(offset)/ds| is the same idea, computed
        // from its power-curve derivative; here we finite-difference the Snell's-law
        // edgeFactor directly.  Direction is the edge normal (safeN), which IS the
        // gradient direction of the offset.
        float2 offSpread;
        {
            float xR1 = 1.0 - (nmerged + 1.0) / (RefThickness * dpr);
            float ef1 = 0.0;
            if (nmerged + 1.0 < RefThickness * dpr)
            {
                float thetaI1 = asin(pow(saturate(xR1), 2.0));
                float thetaT1 = asin(sin(thetaI1) / RefFactor);
                ef1 = -tan(thetaT1 - thetaI1);
            }
            float spreadMag = abs(edgeFactor - ef1) * 0.05;
            offSpread = safeN * spreadMag * float2(res.y / res.x, 1.0);
        }

        // Light minimum blur at the edge (~0.25 px spread), tapered by edge
        // proximity so the deep interior stays crisp.
        float edgeProx = 1.0 - saturate(abs(nmerged) / (RefThickness * dpr));
        float minSpread = (0.25 / res.y) * edgeProx;
        float osLen = length(offSpread);
        if (osLen < minSpread)
        {
            float2 spreadDir = osLen > 1e-10 ? offSpread / osLen : safeN * float2(res.y / res.x, 1.0);
            offSpread = spreadDir * minSpread;
        }
        // chromatic dispersion: per-channel IOR spread, attenuated by DispersionRange.
        float depthInZone = nmerged / (RefThickness * dpr);
        float tt = saturate(depthInZone / max(DispersionRange, 0.001));
        float attenuation = 1.0 - smoothstep(0.0, 1.0, tt);
        float disp = 0.02 * RefDispersion * attenuation;

        // 8-tap box-average along the normal (footprint integral, ported from
        // LiquidGlassShader.cs). When off=0 (no refraction) offSpread=0 → all
        // 8 taps coincide → identity sample. Near the edge where d(offset)/ds
        // is steepest, each rim pixel averages many interior pixels → no seam.
        float3 accRgb = 0.0;
        float  accA   = 0.0;
        for (int ti = 0; ti < 8; ti++)
        {
            float f = (float)ti / 7.0 - 0.5;
            float4 tap = texture0.Sample(sampler0, uv + off + offSpread * f);
            accRgb += tap.rgb;
            accA   += tap.a;
        }
        float4 blurredPixel = float4(accRgb / 8.0, accA / 8.0);

        // Chromatic dispersion: re-sample R/B with IOR-scaled offset, each with
        // a 3-tap box blur along the normal.  Single-point samples on a sharp
        // backdrop can hit a wildly different colour than the 8-tap footprint
        // average, creating per-pixel colour-fringing noise.  3 taps gives a
        // basic antialiasing pass (6 extra samples total, up from 2).
        if (abs(disp) >= 1e-6)
        {
            float nr = 1.0 + disp;
            float nb = 1.0 - disp;

            // R channel (dispersed outward): 3-tap box blur
            float rSum  = texture0.Sample(sampler0, uv + off * nr + offSpread * (-0.333)).r;
                 rSum += texture0.Sample(sampler0, uv + off * nr).r;
                 rSum += texture0.Sample(sampler0, uv + off * nr + offSpread *  0.333).r;
            blurredPixel.r = rSum / 3.0;

            // B channel (dispersed inward): 3-tap box blur
            float bSum  = texture0.Sample(sampler0, uv + off * nb + offSpread * (-0.333)).b;
                 bSum += texture0.Sample(sampler0, uv + off * nb).b;
                 bSum += texture0.Sample(sampler0, uv + off * nb + offSpread *  0.333).b;
            blurredPixel.b = bSum / 3.0;
        }
        // Un-premultiply: the blurred backdrop is premultiplied alpha. Near
        // content/void boundaries the blur creates semi-transparent pixels
        // whose RGB is already attenuated by alpha. Recover straight colour
        // before Exposure/tint so the glass edge doesn't get double-darkened
        // (once by blur alpha, once by shape AA).
        {
            float ba = blurredPixel.a;
            blurredPixel.rgb = ba > 0.001 ? blurredPixel.rgb / ba : float3(0, 0, 0);
        }
        // base tint
        blurredPixel.rgb *= Exposure;
        outColor = lerp(blurredPixel, float4(tintRgb, 1.0), tintA * 0.8);

        // fresnel rim — tint lightened toward white (RGB approximation of the LCH
        // L-bump; the SRGB_TO_LCH forward chain blows DWM's budget). RefFresnelRange
        // controls the falloff via fresnelK; the geoFactor naturally →0 deep inside.
        {
            float fresnelHardness = RefFresnelHardness / 100.0;
            float fresnelFactorN = RefFresnelFactor / 100.0;
            // Clamp steepness so the rim transition spans ≥ ~1.5 px.  When the raw
            // width 1500*dpr/K drops below 1.5 px the pow(x,5) nonlinearity compresses
            // it further → visible stair-stepping on the inner edge of the rim.
            float fresnelK = pow(500.0 / max(RefFresnelRange, 0.0001), 2.0);
            float fresnelMaxK = 3000.0 * dpr;
            fresnelK = min(fresnelK, fresnelMaxK);
            float ffBase = 1.0 + merged * res.y / (1500.0 * dpr) * fresnelK + fresnelHardness;
            float fresnelFactor = clamp(pow(max(ffBase, 0.0), 5.0), 0.0, 1.0);
            float3 fresnelBase = lerp(float3(1.0, 1.0, 1.0), tintRgb, tintA * 0.5);
            float lighten = clamp(0.2 * fresnelFactor * fresnelFactorN, 0.0, 1.0);
            float3 fresnelTint = lerp(fresnelBase, float3(1.0, 1.0, 1.0), lighten);
            float fresnelCoverage = smoothstep(0.0, 2.0, RefFresnelRange * dpr);
            outColor = lerp(outColor, float4(fresnelTint, 1.0), fresnelFactor * fresnelFactorN * 0.7 * fresnelCoverage);
        }

        // glare — directional specular highlight (RGB approximation of the LCH
        // L/C-bump). Copied verbatim from LiquidGlassStudio; uses the analytical
        // normal/nLen above. GlareRange controls the falloff via glareK; the geoFactor
        // naturally →0 deep inside — no longer gated by edgeFactor/RefThickness.
        {
            float glareHardness = GlareHardness / 100.0;
            float glareFactorN = GlareFactor / 100.0;
            float glareOppositeN = GlareOppositeFactor / 100.0;
            float glareConvergenceN = GlareConvergence / 100.0;
            float glareAngleNorm = GlareAngle * PI / 180.0;

            // Clamp steepness so the glare transition spans ≥ ~1.5 px (same reasoning
            // as fresnel above — sub-pixel transitions alias on the inner edge).
            float glareK = pow(500.0 / max(GlareRange, 0.0001), 2.0);
            float glareMaxK = 3000.0 * dpr;
            glareK = min(glareK, glareMaxK);
            float glareGeoFactor = clamp(pow(1.0 + merged * res.y / (1500.0 * dpr) * glareK + glareHardness, 5.0), 0.0, 1.0);

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
            float glareCoverage = smoothstep(0.0, 2.0, GlareRange * dpr);
            outColor = lerp(outColor, float4(glareColor, 1.0), saturate(g * nLen) * glareCoverage);
        }
    }
    else
    {
        // OUTSIDE the glass: fully transparent. A control's drop shadow lives
        // outside the brush rect (drawn by the platform, e.g. a XAML Shadow on a
        // larger host), so rendering it in-shader would only be clipped at the
        // control edge — nothing to do here but let the backdrop show through.
        outColor = float4(0.0, 0.0, 0.0, 0.0);
    }

    // One-sided AA (outside only), matching LiquidGlassShader.cs.  Interior stays
    // fully opaque — no more 50%-alpha edge pixels diluting the fresnel/glare rim.
    // aaWidth was computed before the glass branch to set glassThreshold.
    float alpha = 1.0 - saturate(merged / aaWidth);
    outColor = float4(outColor.rgb * alpha, alpha);

    return outColor;
}

export float4 PSBody(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyCC(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyCW(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyCM(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyWC(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyWW(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyWM(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyMC(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyMW(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyMM(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyC(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyW(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
export float4 PSBodyM(float2 uv, float4 samplerDataExt, float4 samplerData) { return LiquidGlassBody(uv, samplerDataExt, samplerData); }
