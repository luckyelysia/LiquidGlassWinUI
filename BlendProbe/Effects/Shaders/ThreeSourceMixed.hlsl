// Card 10 — THE 3-SOURCE MIXED-WIRING PROBE (closes the last topology cell before the
// mask-bake port). THREE sources from TWO routes in one shader:
//   src0 "Tex0"     -> color route          arg 0x0200 -> float4 sample0
//   src1 "Tex1"     -> color route          arg 0x0201 -> float4 sample1
//   src2 "Backdrop" -> custom-sampler route arg 0x0402 -> texture0/sampler0
// Card 8 proved 2 color sources; card 9 proved 1 color + 1 sampler. This proves them
// COMBINED — 2 color + 1 sampler — which is the production packing for the full bake
// (Tex0 + Tex1 as two static mask color sources + the live backdrop as the single
// sampler texture). Only one sampler source is declared, so the lone texture0 slot is
// not contested.
//
// A slider drives Factor (cbuffer _Params.x). The two mask colors are blended 50/50,
// then lerped with texture0.Sample by Factor:
//   Factor=0 -> 50/50 of the two mask colors   proves BOTH color routes bind
//                                                  (magenta+cyan => periwinkle; pure
//                                                   magenta or pure cyan => one dropped)
//   Factor=1 -> pure texture0.Sample             proves the sampler route binds (backdrop)
//   Factor=0.5 -> all three blended
// Outcomes (all informative):
//   slider wipes periwinkle<->backdrop  -> 3-source mixed wiring SUPPORTED (green light)
//   Factor=0 stuck on pure magenta/cyan -> only ONE color source binds
//   Factor=1 stuck on periwinkle        -> sampler route dropped (src2 not bound)
//   blank/black                         -> DWM dropped the whole shader (not supported)

cbuffer ThreeSourceMixedConstants : register(b0)
{
    float4 _Params; // _Params.x = Factor (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt)
{
    float4 tex = texture0.Sample(sampler0, uv);
    float4 maskBlend = lerp(sample0, sample1, 0.5);
    float f = saturate(_Params.x);
    return lerp(maskBlend, tex, f);
}

export float4 PSBody(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyCC(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyCW(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyCM(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyWC(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyWW(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyWM(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyMC(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyMW(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyMM(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyC(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyW(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
export float4 PSBodyM(float2 uv, float4 sample0, float4 sample1, float4 samplerDataExt) { return Core(uv, sample0, sample1, samplerDataExt); }
