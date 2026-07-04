// Card 2 — custom-sampler ABI, single source. Returns the UV as color to PROVE the
// 0x0100 UV argument spans [0,1] in this ABI (right -> red, bottom -> green). The
// contrast pair with ColorInputUv.hlsl (card 4), where the same viz uses the
// color-input ABI and the UV is wrong.
//
// Custom-sampler route (linkingArgType == 0x0200): DWM's AppendCustomSamplerShaderBody
// appends edge-mode suffixes, so we export PSBody + 12 aliases all delegating to Core.

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 samplerDataExt)
{
    return float4(0.0, uv, 1.0);
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
