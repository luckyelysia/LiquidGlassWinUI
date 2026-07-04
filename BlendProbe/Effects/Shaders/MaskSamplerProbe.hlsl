// MaskBindingPage cards A & B — single-source sampler probe. Just returns whatever DWM
// bound to texture0, so the source's identity is read straight off the pixels:
//   card A (source = baked mask surface) -> expect the R/G position gradient + B rounded
//                                            rect (proves a surface brush lands in texture0
//                                            even with no competing source).
//   card B (source = CreateBackdropBrush) -> expect the scene WITH text (proves the live
//                                            backdrop alone reaches texture0; the control).
// Stateless: no cbuffer. ABI matches card 2 (CustomSamplerUv.hlsl).

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 samplerDataExt)
{
    return texture0.Sample(sampler0, uv);
}

export float4 PSBody(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyCC(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyCW(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyCM(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyWC(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyWW(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyWM(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyMC(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyMW(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyMM(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyC(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyW(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
export float4 PSBodyM(float2 uv, float4 samplerDataExt) { return Core(uv, samplerDataExt); }
