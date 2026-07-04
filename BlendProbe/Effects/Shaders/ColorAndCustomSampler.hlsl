// Card 7 — HEADLINE: can a custom-sampler body ALSO receive the 0x0200 implicit
// color sample in the SAME shader? PSBody takes (uv, sample0, samplerDataExt) AND
// samples texture0 itself. A slider drives the texture sample's horizontal offset
// (cbuffer _Params.x); the result blends the backdrop color with the offset texture
// sample. Outcomes (all informative):
//   blend visible, shifts with slider -> BOTH supported (custom + color simultaneously)
//   looks like backdrop                -> color only (texture0 not bound)
//   offset-only image                  -> texture only (sample0 not bound)
//   blank/black                        -> DWM dropped the whole shader (not supported)

cbuffer OffsetConstants : register(b0)
{
    float4 _Params; // _Params.x = Offset (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 sample0, float4 samplerDataExt)
{
    float2 offsetUv = uv + float2(_Params.x, 0.0);
    float4 tex = texture0.Sample(sampler0, offsetUv);

    if(offsetUv.x < 0.0 || offsetUv.x > 1.0 || offsetUv.y < 0.0 || offsetUv.y > 1.0)
    {
        tex = float4(0.0, 0.0, 0, 0); // out-of-bounds -> pure blue
    }

    return lerp(sample0, tex, 0.5);
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
