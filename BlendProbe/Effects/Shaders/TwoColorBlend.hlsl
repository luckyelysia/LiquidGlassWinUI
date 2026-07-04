// Card 8 — two-source color-input blend. PSBody takes sample0 (Tex0) and sample1
// (Tex1) and lerps by Factor. The runtime binds the Nth 0x0200 color arg to the Nth
// declared source by ordinal (WinUI3/CustomBlendEffect.cpp:32-36 uses {0x0200,0x0200}).
// The page binds Tex0=backdrop, Tex1=CompositionColorBrush so the wipe is distinct.

cbuffer BlendConstants : register(b0)
{
    float4 _Params; // _Params.x = Factor (cbuffer offset 0)
};

export float4 PSBody(float4 sample0, float4 sample1)
{
    float f = saturate(_Params.x);
    return lerp(sample0, sample1, f);
}
