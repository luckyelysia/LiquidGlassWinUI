// Card 4 stage2 — single-source color-route invert. Non-flatten consumer,
// expects a clean texture from Mf04bRelay upstream.
// ABI: flattenSourceBeforeCustomSampler=false, sourceCount=1,
//      args {0x0200}, linkingArgType=0, hasCustomSamplers=false.

export float4 PSBody(float4 sample0)
{
    return float4(1.0f.xxx - sample0.rgb, sample0.a);
}
