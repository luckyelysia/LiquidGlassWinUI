// Card 5 stage0 — single-source color-route invert. Non-flatten, feeds into
// Mf05bCrossBlend which materializes the inverted output as texture0.
// ABI: flattenSourceBeforeCustomSampler=false, sourceCount=1,
//      args {0x0200}, linkingArgType=0, hasCustomSamplers=false.

export float4 PSBody(float4 sample0)
{
    return float4(1.0f.xxx - sample0.rgb, sample0.a);
}
