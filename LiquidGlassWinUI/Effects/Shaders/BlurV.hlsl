// BlurV — 1D separable vertical Gaussian blur.
// Second pass: backdrop -> BlurH -> BlurV -> glass.
//
// Optimised with bilinear-sample merging: adjacent integer-offset tap pairs
// (1,2) (3,4) … (19,20) are each replaced by a single bilinear sample at the
// weighted centroid.  Hardware linear interpolation is exact — zero visual
// change, 41 taps → 21 taps (49% fewer per pass).
//
// sigma = MAX_BLUR_RADIUS / 3, weights precomputed CPU-side.

#define MAX_BLUR_RADIUS 20   // original kernel radius (for weight computation)
#define MAX_BLUR_PAIRS  5    // 10 bilinear pairs packed into 5 float4s

Texture2D texture0;
SamplerState sampler0;

cbuffer BlurConstants : register(b0)
{
    float BlurAmount;              // offset 0: sample spread in texels (animatable)
    float3 _pad0;                  // offset 4-12
    float CenterWeight;            // offset 16: normalised weight at offset 0
    float3 _pad1;                  // offset 20-28
    float4 PairData[5];            // offset 32: 5 float4s = 10 (mergedOffset,mergedWeight) pairs
};

// Mirror edge addressing: periodically folds any real x into [0,1] by
// reflecting at each integer boundary (…→0→1→0→1→…).  Unlike clamp-to-edge,
// this never replicates a single border pixel, so dark backdrop borders
// (title bar, taskbar) don't accumulate into a visible band.
float clampEdge(float x)
{
    float ax = abs(x);
    // Fold into [0,2], then mirror the [1,2] half back into [0,1].
    float folded = ax - 2.0 * floor(ax * 0.5);
    return folded > 1.0 ? 2.0 - folded : folded;
}

// center + 10 bilinear symmetric pairs (matches the original 41-tap kernel exactly).
float4 BlurCore(float2 uv, float4 samplerDataExt)
{
    float invTexel = samplerDataExt.w; // 1/height
    float spread = max(BlurAmount, 0.0f);
    float step = invTexel * spread;

    float4 sum = texture0.Sample(sampler0, uv) * CenterWeight;

    [unroll]
    for (int pi = 0; pi < MAX_BLUR_PAIRS; ++pi)
    {
        float4 pd = PairData[pi];

        // Pair A: offset in .x, weight in .y
        float offA = pd.x * step;
        float wA   = pd.y;
        sum += texture0.Sample(sampler0, float2(uv.x, clampEdge(uv.y + offA))) * wA;
        sum += texture0.Sample(sampler0, float2(uv.x, clampEdge(uv.y - offA))) * wA;

        // Pair B: offset in .z, weight in .w
        float offB = pd.z * step;
        float wB   = pd.w;
        sum += texture0.Sample(sampler0, float2(uv.x, clampEdge(uv.y + offB))) * wB;
        sum += texture0.Sample(sampler0, float2(uv.x, clampEdge(uv.y - offB))) * wB;
    }
    return sum;
}

// FlattenSource — passthrough that materializes the upstream source into texture0.
export float4 FlattenSource(float4 sample0) { return sample0; }

export float4 PSBody(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyCC(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyCW(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyCM(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyWC(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyWW(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyWM(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyMC(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyMW(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyMM(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyC(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyW(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
export float4 PSBodyM(float2 uv, float4 samplerDataExt) { return BlurCore(uv, samplerDataExt); }
