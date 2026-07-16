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
// refraction of the blurred source (7-tap spectral chromatic dispersion, RefDispersion),
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
#define SHAPE_MARGIN 1.0       // px inset from brush edge; prevents clipping at AA boundary

Texture2D texture0;
SamplerState sampler0;

cbuffer LiquidGlassParams : register(b0)
{
    float RefThickness;        // offset 0
    float RefFactor;           // offset 4
    float RefDispersion;       // offset 8  (chromatic dispersion spread as a fraction of the refraction offset; 0 = none)
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
    float Exposure;            // offset 52 (unused — moved to PostProcessingEffect; slot kept for cbuffer layout parity)
    float TintR;               // offset 56
    float TintG;               // offset 60
    float TintB;               // offset 64
    float TintA;               // offset 68
    float Magnification;       // offset 72  (backdrop zoom: 1.0 = none, >1 = zoom in; min 1 — <1 would sample void outside content rect)
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
    float DispersionRange;     // offset 116 (depth over which dispersion fades, as a 0..1 fraction of RefThickness; default 1)
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

// Rounded-rect analytical normal, matching RoundedRectSDF above:
//   • flat faces  -> axis-aligned, EXACTLY perpendicular to the nearest edge
//     (cc.x>cc.y picks the x-axis face, else the y-axis face);
//   • corner band -> the superellipse gradient normalize(|q|^(n-1)), the true
//     outward normal of the isotropic superellipse corner (n=2 = circular arc,
//     larger n = squircle, following ShapeRoundness) — round and smooth;
//   • corner<->face boundary is naturally C0-continuous: at cc.x=0 the corner
//     gradient collapses to (1,0), the same axis the flat face picks there;
//   • the only hard switch left is the medial axis (cc.x==cc.y) deep inside the
//     flat faces — smoothed with smoothstep over ±blend px so the normal rotates
//     instead of jumping a full 90°.
// cc = abs(p)-(halfPx-cr) is the corner coordinate (max(cc)>0 -> corner band).
// blend = medial-axis transition half-width (px). One normal serves refraction
// (safeN) and glare (nNorm); nLen is the Studio-magnitude scale (unit ×
// 1/res.y × sqrt2 × 1000), constant except the degenerate corner-centre point.
float2 RoundedRectNormal(float2 fragCoord, float2 center, float2 halfPx,
                         float cr, float n, float blend, float2 res)
{
    float2 p   = fragCoord - center;
    cr = min(cr, min(halfPx.x, halfPx.y));
    float2 cc  = abs(p) - (halfPx - cr);   // corner coord; max(cc)>0 -> corner band
    float2 sgn = sign(p);
    float2 local;
    if (max(cc.x, cc.y) > 0.0)
    {
        // corner band: superellipse gradient (matches the SDF's squircle corner)
        float2 g = pow(max(cc, 0.0), float2(n - 1.0, n - 1.0));
        local = g / max(length(g), 1e-6);
    }
    else
    {
        // flat face: axis-aligned, with a smoothstep blend across the medial axis
        float t = smoothstep(-blend, blend, cc.x - cc.y);
        local = normalize(float2(t, 1.0 - t));
    }
    return sgn * local * (1.0 / res.y) * 1.414213562 * 1000.0;
}

// Clamp a sampling UV to the content rect of the intermediate texture.
// Extreme refraction + chromatic dispersion can push UVs beyond the valid
// area; without this, DWM's hardware sampler clamps to the texture border,
// producing visible smearing at glass edges (especially with BlurAmount=0).
float2 ClampSamplingUv(float2 uv, float2 lo, float2 hi)
{
    return clamp(uv, lo, hi);
}

// (Currently unused — the spectrum spread cap below keeps single-point taps
// smooth enough; a 3-tap was tried but, combined with the cap, spills registers
// past ps_2_x's 512-slot budget. Retained for a future AA upgrade.) 3-tap box
// blur along a spread direction for one sampling point; lo/hi clamp every tap
// to the content rect (see ClampSamplingUv).
float4 SampleAa3(float2 uv, float2 spread, float2 lo, float2 hi)
{
    float4 s  = texture0.Sample(sampler0, ClampSamplingUv(uv - spread * 0.5, lo, hi));
         s += texture0.Sample(sampler0, ClampSamplingUv(uv,                lo, hi));
         s += texture0.Sample(sampler0, ClampSamplingUv(uv + spread * 0.5, lo, hi));
    return s * (1.0 / 3.0);
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

    // Bounds used to clamp every texture sample inside the valid content rect.
    // When samplerData is degenerate fall back to [0,1] (the full intermediate).
    float2 clampMin = hasContentRect ? contentMin : float2(0.0, 0.0);
    float2 clampMax = hasContentRect ? contentMax : float2(1.0, 1.0);

    // Magnification: scale UVs about the content center to zoom the backdrop.
    // Magnification=1.0 is identity; >1 zooms in. Must stay ≥1 — <1 pushes UVs
    // beyond the content rect, sampling void/clamped edges.
    float2 contentCenter = (contentMin + contentMax) * 0.5;
    float2 zoomedUv = contentCenter + (uv - contentCenter) / Magnification;

    float2 res = max(1.0 / max(abs(ddx(localUv)) + abs(ddy(localUv)), float2(1e-6, 1e-6)), 1.0);
    float dpr = (Dpr > 0.0) ? Dpr : 1.0;   // physical px per logical px (0/unset -> 1)
    float2 fragCoord = localUv * res;

    // Control-local geometry: the glass IS the brush rect — one rounded rect with
    // isotropic superellipse corners filling res, so it sizes to the control
    // automatically at any DPI (no Width/Height params). ShapeRadius is a 0..1
    // corner-radius fraction of the shorter half-side (keep < 1 so flat sides
    // exist; =1 collapses to a pure superellipse -> aspect-corner bug for non-square).
    float2 center = res * 0.5;
    // ShapeMargin const (SHAPE_MARGIN px, scaled by dpr) shrinks the glass inward
    // so it does not clip against the brush edges. Clamped to never collapse.
    float margin = max(SHAPE_MARGIN * dpr, 0.0);
    float2 halfPx = max(res * 0.5 - margin, 1.0);
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

        // Outward normal — the rounded-rect analytical gradient (RoundedRectNormal
        // above): flat faces are exactly axis-aligned (perpendicular to the nearest
        // edge), the corner band uses the superellipse gradient matching the SDF's
        // squircle corner (n = ShapeRoundness), and the medial axis is smoothed.
        // One normal serves both refraction (safeN / off / offSpread) and glare
        // (nNorm).  nLen is the Studio-magnitude scale (unit × 1/res.y × sqrt2 × 1000).
        float2 normal = RoundedRectNormal(fragCoord, center, halfPx, cr, n, 3.0 * dpr, res);
        float  nLen   = length(normal);

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
        // Chromatic dispersion prep — AGSL-style 7-tap spectral sampling (see
        // the dispersed block below). Two modulations are combined:
        //   • depth attenuation (DispersionRange): fades dispersion with depth
        //     into the refraction zone so the deep interior stays clean;
        //   • positional intensity posFactor = (x·y)/(w·h): concentrates the
        //     dispersion on the control's diagonals and zeroes it on the medial
        //     axes, matching radial chromatic aberration (none at lens centre).
        // RefDispersion is now the dispersion spread as a FRACTION of the
        // refraction offset `off` (was: per-channel IOR delta — semantics changed).
        float depthInZone = nmerged / (RefThickness * dpr);
        float tt = saturate(depthInZone / max(DispersionRange, 0.001));
        float attenuation = 1.0 - smoothstep(0.0, 1.0, tt);
        float2 centeredCoord = fragCoord - center;
        float posFactor = (centeredCoord.x * centeredCoord.y) / max(halfPx.x * halfPx.y, 1e-6);
        float dispAmount = RefDispersion * attenuation * posFactor;

        // 8-tap box-average along the normal (footprint integral, ported from
        // LiquidGlassShader.cs). When off=0 (no refraction) offSpread=0 → all
        // 8 taps coincide → identity sample. Near the edge where d(offset)/ds
        // is steepest, each rim pixel averages many interior pixels → no seam.
        float3 accRgb = 0.0;
        float  accA   = 0.0;
        for (int ti = 0; ti < 8; ti++)
        {
            float f = (float)ti / 7.0 - 0.5;
            float2 sampleUv = ClampSamplingUv(zoomedUv + off + offSpread * f, clampMin, clampMax);
            float4 tap = texture0.Sample(sampler0, sampleUv);
            accRgb += tap.rgb;
            accA   += tap.a;
        }
        float4 blurredPixel = float4(accRgb / 8.0, accA / 8.0);

        // Chromatic dispersion: re-sample R/B with IOR-scaled offset, each with
        // a 3-tap box blur along the normal.  Single-point samples on a sharp
        // backdrop can hit a wildly different colour than the 8-tap footprint
        // average, creating per-pixel colour-fringing noise.  3 taps gives a
        // basic antialiasing pass (6 extra samples total, up from 2).
        // Chromatic dispersion — 7-tap spectral sampling, ported from the AGSL
        // RoundedRectRefractionWithDispersion shader. Seven samples along the
        // refraction offset `off` at fractions +1, +2/3, +1/3, 0, -1/3, -2/3,
        // -1 (red → orange → yellow → green → cyan → blue → purple); each
        // contributes only its wavelength's channel with hand-tuned spectral
        // weights. Because each channel's weight is spread across 3-4 ADJACENT
        // taps along `off`, the weighted sum is itself a box average along the
        // dispersion direction — the previous 3-tap-per-channel AA blur is
        // subsumed, so no extra AA taps are needed. Sample count: 8 (base) +
        // 7 (spectrum) = 15 (was 14).
        if (abs(dispAmount) >= 1e-6)
        {
            float2 dispersedCoord = off * dispAmount;
            // Cap the spectrum half-spread to ~half the backdrop's blur footprint
            // (clamped 2..8 px, scaled by dpr). Without this, RefDispersion > ~0.5
            // fans the 7 taps across many px at the edge (where off AND attenuation
            // peak), sampling wildly different backdrop and reading as amplified
            // rainbow noise that worsens toward the edge. Capping to the blur
            // footprint keeps the taps inside the already-smoothed region, so the
            // unfold can never outrun what the (blurred) backdrop can resolve.
            float maxHalf = clamp(BlurAmount * 0.5, 2.0, 8.0) * dpr / res.y;
            float dLen = length(dispersedCoord);
            dispersedCoord *= min(1.0, maxHalf / max(dLen, 1e-8));
            float2 base0 = zoomedUv + off;   // green / centre sample

            // All six spectrum taps are single-point. The spread cap above keeps
            // them inside the blur footprint and sub-pixel-spaced, so single-point
            // samples are already smooth — no per-tap 3-tap AA is needed. (A 3-tap
            // was tried: on its own it removes the diagonal bands, but combined
            // with the cap it spills registers and overflows ps_2_x's 512-slot
            // budget; the cap alone resolves BOTH the edge noise and the bands,
            // at 379 slots.) Green reuses the 8-tap blurredPixel.
            float4 sRed    = texture0.Sample(sampler0, ClampSamplingUv(base0 + dispersedCoord,              clampMin, clampMax));
            float4 sOrange = texture0.Sample(sampler0, ClampSamplingUv(base0 + dispersedCoord * (2.0 / 3.0), clampMin, clampMax));
            float4 sYellow = texture0.Sample(sampler0, ClampSamplingUv(base0 + dispersedCoord * (1.0 / 3.0), clampMin, clampMax));
            float4 sCyan   = texture0.Sample(sampler0, ClampSamplingUv(base0 - dispersedCoord * (1.0 / 3.0), clampMin, clampMax));
            float4 sBlue   = texture0.Sample(sampler0, ClampSamplingUv(base0 - dispersedCoord * (2.0 / 3.0), clampMin, clampMax));
            float4 sPurple = texture0.Sample(sampler0, ClampSamplingUv(base0 - dispersedCoord,              clampMin, clampMax));

            // Spectral weights. R/G sum to 1.0; B normalized to 1.0 (divisor
            // 4.0, not the AGSL source's 3.0 — see note above on the blue ring).
            // Alpha stays the AA'd blurredPixel.a (colour-only effect).
            float3 disp;
            disp.r = (sRed.r + sOrange.r + sYellow.r) / 3.5 + sPurple.r / 7.0;
            disp.g = sOrange.g / 7.0 + (sYellow.g + blurredPixel.g + sCyan.g) / 3.5;
            disp.b = (blurredPixel.b + sCyan.b + sBlue.b + sPurple.b) / 4.0;
            blurredPixel.rgb = disp;
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
        // base tint (Exposure moved to PostProcessingEffect)
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
        // L/C-bump).  Uses the shared radial normal (nLen) — purely geometric, so
        // the specular stays consistent regardless of RefThickness.  GlareRange
        // controls the falloff via glareK; the geoFactor naturally →0 deep inside.
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
