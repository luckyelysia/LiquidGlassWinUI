// Card 3 — custom-sampler ABI with the 0x0300 SamplerData (content-rect) argument.
// Visualizes samplerData.z (width -> red) and .w (height -> green). If the arg is
// bound you get a teal/green tint; if it is NOT bound (zeros) you get pure blue
// (0,0,0.5,1) — so a glance tells whether 0x03 reaches the shader.

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 samplerData, float4 samplerDataExt)
{
    float r = saturate(samplerData.z * 0.005);
    float g = saturate(samplerData.w * 0.005);
    return float4(samplerData.xyz, uv.y);
}

export float4 PSBody(float2 uv, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, samplerData, samplerDataExt);
}
export float4 PSBodyCC(float2 uv, float4 samplerData, float4 samplerDataExt)
{
    return Core(uv, samplerData, samplerDataExt);
}
// export float4 PSBodyCW(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyCM(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyWC(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyWW(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyWM(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyMC(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyMW(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyMM(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyC(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyW(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
// export float4 PSBodyM(float2 uv, float4 samplerData, float4 samplerDataExt)
// {
//     return Core(uv, samplerData, samplerDataExt);
// }
