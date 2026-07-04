
cbuffer MaskBindingProbeConstants : register(b0)
{
    float4 _Params; // _Params.x = Mode (cbuffer offset 0)
};

Texture2D texture0;
SamplerState sampler0;

float4 Core(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1) // samplerDataExt1：(w,h,1/w,1/h) logic pixel
{
    float m = _Params.x;
    float2 physicSize = float2(2560, 1440);
    float2 logicSize = physicSize / 1.25;
    float2 canvasUV = uv / (float2(ddx(uv.x), ddy(uv.y)) * samplerDataExt1.xy);
    float4 tex0 = texture0.Sample(sampler0, uv);
    
    
    if (m < 0.5)
        return samplerData1;
    if (m < 1.5)
        return float4(sample1.rgb / 2, 1);
    if (m < 2.5)
        return float4(canvasUV, 0.0, 1.0);
    if (m < 3.5)
        return float4(samplerDataExt1.x / 2,0,0,1);
    if (m < 4.5)
        return (canvasUV.x > 0.5) ? float4(sample0.rgb / 2, 1.0) : float4(sample1.rgb / 2, 1.0);
    return float4(1.0, 0.0, 1.0, 1.0);
}

export float4 PSBody(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
{
    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
}
export float4 PSBodyCC(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
{
    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
}
//export float4 PSBodyCW(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyCM(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyWC(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyWW(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyWM(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyMC(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyMW(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyMM(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyC(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyW(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
//export float4 PSBodyM(float2 uv, float4 sample0, float4 sample1, float4 samplerData1, float4 samplerDataExt1)
//{
//    return Core(uv, sample0, sample1, samplerData1, samplerDataExt1);
//}
