// Card 4 — color-input ABI WITH the 0x0100 UV argument, SINGLE source. Same UV
// viz as CustomSamplerUv.hlsl, but linkingArgType == 0 (color route). VERIFIED at
// runtime: the UV is correct here — uv.x spans the full [0,1] (right edge → 1.0).
// So a SINGLE-source color-input ABI receives a correct explicit UV. The UV only
// goes wrong in MULTI-source color-input ABI (uv.x plateaus ~0.34); see the
// dwm-linker-argument-encoding notes.

export float4 PSBody(float2 uv, float4 sample0)
{
    return float4(0, 0,uv.x,  1.0);
}
