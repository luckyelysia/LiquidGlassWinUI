// Card 6 — effect chain WITH FlattenSource. flattenSourceBeforeCustomSampler=true
// materializes the upstream (blurred) intermediate into the texture0 this sampler
// reads. The passthrough FlattenSource(float4) is required by the runtime
// (CustomEffectRuntime.cpp:1113-1118); PSBody samples the materialized texture to
// prove the blur reached it.

Texture2D texture0;
SamplerState sampler0;

// Required by the flatten subgraph: DWM runs this on the source composite to
// produce the materialized intermediate the custom sampler consumes as texture0.
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
