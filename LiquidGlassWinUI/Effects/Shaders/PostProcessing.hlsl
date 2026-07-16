// PostProcessing — unified post-processing stage between BlurV and LiquidGlass.
//
// Takes TWO backdrop sources:
//   src0 "Backdrop"    -> custom-sampler route -> texture0 (blurred via upstream BlurH->BlurV)
//   src1 "RawBackdrop" -> color route          -> sample1  (raw/unblurred backdrop)
//
// Processing order:
//   1. Bloom blend: lerp(blurred, raw, BloomAmount)
//   2. Exposure (multiplicative gain)
//   3. Temperature (R/G/B channel gains)
//   4. Brightness (additive)
//   5. Contrast (expand/compress around 0.5)
//   6. Saturation + Vibrance (intelligent desaturation boost)
//   7. saturate to [0,1], preserve original alpha
//
// ABI: flattenSourceBeforeCustomSampler=true, sourceCount=2,
//      args {0x0100, 0x0400, 0x0201}, linkingArgType=0x0200, hasCustomSamplers=true.

cbuffer PostProcessingConstants : register(b0)
{
    float BloomAmount;   // offset  0: bloom blend [0, 1]
    float Brightness;    // offset  4: additive brightness [-1, 1]
    float Contrast;      // offset  8: contrast multiplier [0, 2]
    float Saturation;    // offset 12: saturation multiplier [0, 2]
    float Temperature;   // offset 16: colour temperature [-1, 1]
    float Exposure;      // offset 20: exposure gain [0.5, 2]
    float Vibrance;      // offset 24: smart vibrance boost [0, 1]
    float _pad;          // offset 28: padding to float4×2
};

Texture2D texture0;
SamplerState sampler0;

// FlattenSource — passthrough that materializes the upstream BlurV output into texture0.
export float4 FlattenSource(float4 sample0) { return sample0; }

float4 Core(float2 uv, float4 samplerDataExt, float4 sample1)
{
    // 1. Bloom blend — cross-fade between blurred backdrop and raw backdrop.
    float4 blurred = texture0.Sample(sampler0, uv);
    float b = saturate(BloomAmount);
    float3 color = lerp(sample1.rgb, blurred.rgb, b);
    float alpha = blurred.a; // preserve blurred alpha for downstream premultiplied blending

    // 2. Exposure (multiplicative gain).
    color *= Exposure;

    // 3. Colour temperature — warm (positive T) boosts R, slightly G, reduces B.
    //    Cool (negative T) does the reverse.
    {
        float t = Temperature;
        color.r *= (1.0 + t * 0.25);
        color.g *= (1.0 + t * 0.10);
        color.b *= (1.0 - t * 0.25);
    }

    // 4. Brightness (additive).
    color += Brightness;

    // 5. Contrast — expand/compress around mid-grey 0.5.
    color = (color - 0.5) * Contrast + 0.5;

    // 6. Saturation + Vibrance.
    //    Vibrance boosts low-saturation regions more than already-saturated ones,
    //    avoiding skin-tone oversaturation.
    {
        float gray = dot(color, float3(0.299, 0.587, 0.114));
        float maxVal = max(color.r, max(color.g, color.b));
        float minVal = min(color.r, min(color.g, color.b));
        float sat = (maxVal - minVal) / (maxVal + 1e-6);
        float vibrancyBoost = 1.0 + Vibrance * (1.0 - sat);
        float finalSat = Saturation * vibrancyBoost;
        color = lerp(float3(gray, gray, gray), color, finalSat);
    }

    // 7. Clamp to valid range, preserve original alpha.
    color = saturate(color);
    return float4(color, alpha);
}

export float4 PSBody(float2 uv, float4 ext, float4 sample1)   { return Core(uv, ext, sample1); }
export float4 PSBodyCC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyCW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyCM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyC(float2 uv, float4 ext, float4 sample1)  { return Core(uv, ext, sample1); }
export float4 PSBodyW(float2 uv, float4 ext, float4 sample1)  { return Core(uv, ext, sample1); }
export float4 PSBodyM(float2 uv, float4 ext, float4 sample1)  { return Core(uv, ext, sample1); }
