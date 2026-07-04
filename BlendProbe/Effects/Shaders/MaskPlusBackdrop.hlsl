// Card 9 — THE 2-SOURCE MIXED-WIRING PROBE (gating experiment for the mask-bake port).
// Two declared sources from DIFFERENT routes:
//   src0 "Mask"     -> color route        arg 0x0200 -> float4 sample0
//   src1 "Backdrop" -> custom-sampler route arg 0x0401 -> texture0/sampler0
// Card 7 proved both routes from ONE source (0x0400=src0); this proves them from TWO
// distinct sources — the empty cell in the linker-encoding table, and exactly the
// topology the mask-bake port needs (static baked mask = color src0, live flattened
// backdrop = custom-sampler src1).
//
// A slider drives Factor (cbuffer _Params.x); the shader lerps sample0 <-> texture0
// by it so each route can be probed IN ISOLATION:
//   Factor=0   -> pure sample0           proves the color route binds src0 (Mask)
//   Factor=1   -> pure texture0.Sample     proves the sampler route binds src1 (Backdrop)
//   Factor=0.5 -> 50/50 blend              proves BOTH bound simultaneously
// Outcomes (all informative):
//   slider wipes mask<->backdrop   -> 2-source mixed wiring SUPPORTED (green light)
//   stuck on flat mask color       -> only color route bound (sampler src1 dropped)
//   stuck on backdrop              -> only sampler route bound (color src0 dropped)
//   blank/black                    -> DWM dropped the whole shader (not supported)

cbuffer MaskPlusBackdropConstants : register(b0)
{
    float4 _Params; // _Params.x = Factor (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 sample0, float4 samplerDataExt)
{
    float4 tex = texture0.Sample(sampler0, uv);
    float f = saturate(_Params.x);
    return lerp(sample0, tex, f);
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
