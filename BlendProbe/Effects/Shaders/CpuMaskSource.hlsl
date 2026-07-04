// Card 11 — THE MASK-PRODUCTION DE-RISK PROBE (CPU-baked mask as a color source).
// Same card-9 wiring (2 sources from 2 routes in one shader):
//   src0 "Mask"     -> color route,          arg 0x0200 -> float4 sample0   (CPU-baked surface)
//   src1 "Backdrop" -> custom-sampler route, arg 0x0401 -> texture0/sampler0 (blurred backdrop)
// args {0x0100, 0x0200, 0x0401}, linkingArgType 0x0200, hasCustomSamplers.
//
// The slider drives Factor (cbuffer _Params.x); the shader lerps sample0 <->
// texture0.Sample by it, isolating each route:
//   Factor=0 -> pure sample0       = the baked mask. A crisp R/G position gradient +
//                                    B rounded-rect means CPU bake + Win2D upload +
//                                    textured-color-source 1:1 sampling all work.
//   Factor=1 -> pure texture0.Sample = the blurred backdrop. Bonus probe of whether a
//                                    composed blur brush is sampleable as texture0 in a
//                                    multi-source (FlattenSource-OFF) effect.
//   Factor=0.5 -> 50/50 blend (both routes bound simultaneously).

cbuffer CpuMaskSourceConstants : register(b0)
{
    float4 _Params; // _Params.x = Factor (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

// float4 Core(float2 uv, float4 sample0, float4 samplerDataExt) 
// {
//     // float2 offsetUv = uv + float2(_Params.x, 0.0);
//     // float4 tex = texture0.Sample(sampler0, offsetUv);
//     float4 tex = texture0.Sample(sampler0, uv);
//     // float f = saturate(_Params.x);
//     // float4 result = lerp(sample0, tex, f);
//     return float4(tex.rgb, 1.0 - _Params.x); 
// }

float4 Core(float2 uv, float4 sample0, float4 samplerDataExt)
{
    float4 tex = texture0.Sample(sampler0, uv);
    // left half = sample0 (mask), right half = texture0 (backdrop)
    return (uv.x < 0.5) ? float4(sample0.rgb, 1.0) : float4(tex.rgb, 1.0);
}

export float4 PSBody(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyCC(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyCW(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyCM(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyWC(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyWW(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyWM(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyMC(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyMW(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyMM(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyC(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyW(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
export float4 PSBodyM(float2 uv, float4 sample0, float4 samplerDataExt) { return Core(uv, sample0, samplerDataExt); }
