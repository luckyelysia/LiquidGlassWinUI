// Card 1 — N=1 regression. Single source, FlattenSource passthrough.
// ABI: flattenSourceBeforeCustomSampler=true, sourceCount=1,
//      args {0x0100, 0x0400}, linkingArgType=0x0200, hasCustomSamplers=true.

Texture2D texture0;
SamplerState sampler0;

export float4 FlattenSource(float4 sample0) { return sample0; }

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
