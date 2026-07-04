
cbuffer MaskBindingSwapProbeConstants : register(b0)
{
    float4 _Params; // _Params.x = Mode (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

// SWAP probe (card D): src0 = Backdrop, src1 = Mask(surface). Reverses card C's order.
// The single question — with the backdrop FIRST, what is texture0?
//   Mode 0 -> sample0      (src0 color route => expect backdrop, if routing is positional)
//   Mode 1 -> sample1      (src1 color route => expect mask)
//   Mode 2 -> texture0     (THE CRUX: backdrop => swap works / mask-bake viable;
//                                      mask     => surface still stole texture0 => blocked)
//   Mode 3 -> split sample0 | sample1 | texture0   (one-glance verdict; default)
//   Mode 4 -> uv           (atlas-uv sanity)
//   Mode 5 -> magenta      (shader-runs check)
// All display modes force alpha = 1 so RGB content is visible (backdrop on the color route
// was observed with a = 0; premultiplied alpha would otherwise hide it).
float4 Core(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    float4 tex = texture0.Sample(sampler0, uv);
    float m = _Params.x;
    if (m < 0.5) return float4(sample0.rgb / 2, 1.0);
    if (m < 1.5) return float4(sample1.rgb / 2, 1.0);
    if (m < 2.5) return float4(tex.rgb / 2, 1.0);
    if (m < 3.5)
    {
        if (uv.x < 0.3333) return float4(sample0.rgb, 1.0); // left band
        if (uv.x < 0.6667) return float4(sample1.rgb, 1.0); // middle band
        return float4(tex.rgb, 1.0);                         // right band = texture0 (the crux)
    }
    if (m < 4.5) return float4(uv, 0.0, 1.0);
    return float4(1.0, 0.0, 1.0, 1.0);
}

export float4 PSBody(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyCC(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyCW(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyCM(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyWC(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyWW(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyWM(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyMC(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyMW(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyMM(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyC(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyW(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
export float4 PSBodyM(float2 uv, float4 sample0, float4 sample1, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, sample0, sample1, samplerData, samplerDataExt);
}
