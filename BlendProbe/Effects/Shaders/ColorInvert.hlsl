// Card 1 — color-input ABI, single source. PSBody takes float4 sample0 (the 0x0200
// color argument) and inverts the backdrop. linkingArgType == 0, so no edge-mode
// aliases are needed.

export float4 PSBody(float4 sample0)
{
    return float4(1.0 - sample0.rgb, sample0.a);
}
