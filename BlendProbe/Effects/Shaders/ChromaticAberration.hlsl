// Combo group 3 — chromatic aberration over the (blurred, flattened) upstream texture.
// Custom-sampler + FlattenSource ABI: PSBody(float2 uv, float4 samplerDataExt), args
// {0x0100, 0x0400}, linkingArgType 0x0200. FlattenSource materializes the upstream blur
// into texture0. cbuffer _Params.x = Offset: R is sampled +Offset right, B -Offset left,
// G/A centered (three samples). Offset is animatable (ChromaticAberrationEffect.Offset).

cbuffer OffsetConstants : register(b0)
{
    float4 _Params; // _Params.x = Offset
};

Texture2D texture0;
SamplerState sampler0;

// Required by the flatten subgraph: DWM runs this on the upstream composite to produce
// the materialized intermediate the custom sampler reads as texture0.
export float4 FlattenSource(float4 sample0) { return sample0; }

float4 Core(float2 uv, float4 samplerDataExt)
{
    float o = _Params.x;
    float4 center = texture0.Sample(sampler0, uv);
    float r = texture0.Sample(sampler0, uv + float2( o, 0.0)).r;
    float b = texture0.Sample(sampler0, uv + float2(-o, 0.0)).b;
    return float4(r, center.g, b, center.a);
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
