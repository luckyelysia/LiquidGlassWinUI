// Card 2 — N=2 mixed wiring. source0=sampler(texture0, 0x0400), source1=color(float4 sample1, 0x0201).
// ABI: flattenSourceBeforeCustomSampler=true, sourceCount=2,
//      args {0x0100, 0x0400, 0x0201}, linkingArgType=0x0200, hasCustomSamplers=true.

cbuffer BlendConstants : register(b0)
{
    float4 _Params; // _Params.x = Factor (0=texture, 1=color overlay)
};

Texture2D texture0;
SamplerState sampler0;
// source1 arrives as pre-sampled float4 sample1 (color route)

export float4 FlattenSource(float4 sample0) { return sample0; }

float4 Core(float2 uv, float4 samplerDataExt, float4 sample1)
{
    float factor = saturate(_Params.x);
    float4 tex = texture0.Sample(sampler0, uv);
    return lerp(tex, sample1, factor);
}

export float4 PSBody(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyCC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyCW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyCM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyWM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyMM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyC(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyW(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
export float4 PSBodyM(float2 uv, float4 ext, float4 sample1) { return Core(uv, ext, sample1); }
