// Combo group terminal — warm tint. Color-input ABI: PSBody(float4 sample0), arg 0x0200.
// cbuffer _Params = {Amount, R, G, B}; lerps the backdrop RGB toward the fixed warm tint
// (1.0, 0.55, 0.15) by saturate(Amount). Alpha passes through. Amount is animatable
// (ColorTintEffect.Amount); the tint RGB never changes, so only intensity is controlled.

cbuffer TintConstants : register(b0)
{
    float4 _Params; // x = Amount, yzw = fixed tint RGB
};

export float4 PSBody(float4 sample0)
{
    float3 tinted = lerp(sample0.rgb, _Params.yzw, saturate(_Params.x));
    return float4(tinted, sample0.a);
}
