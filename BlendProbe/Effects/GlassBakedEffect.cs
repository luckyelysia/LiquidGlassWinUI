using System;
using System.Collections.Generic;
using BlendProbe.Interop;
using BlendProbe.MaskBaking;

namespace BlendProbe.Effects
{
    // Glass A/B — the 8-bit BAKED side (right rect). Three sources: Backdrop (src0 ->
    // texture0 refraction + sample0 color), Mask0 (src1 -> sample1 = Tex0: merged/GX/GY),
    // Mask1 (src2 -> sample2 = Tex1: edgeFactor/fresnel/glareGeo). The shader reconstructs
    // the normal/nLen/nAngle from the baked gradient and combines exactly like GlassRef.
    //
    // Mode (cbuffer offset 120) is the diagnostic selector driven live by the page slider.
    public sealed class GlassBakedEffect : CustomEffectBase
    {
        public const string EffectNameValue = "GlassBakedEffect";
        public const string ModePropertyPath = EffectNameValue + ".Mode";

        private const uint ModeCbufferOffset = 120;   // the unused "Step" slot
        private const float DefaultMode = 0f;          // 0 = baked glass

        protected override Guid Id => new Guid("a1b2c3d4-00bb-4b0b-9bbb-0123456700bb");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "GlassBaked.hlsl";

        // src0 = Backdrop FIRST (lowest-index textured source -> texture0); then the two
        // mask surfaces ride the color route (sample1 / sample2).
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerData = true, WantsSamplerDataExt = true }, // src0
            new EffectSource { Name = "Mask0" },                                                          // src1
            new EffectSource { Name = "Mask1" },                                                          // src2
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,                                     // 0x0100  uv
            CustomEffectInterop.BackdropSampleArgument,                                 // 0x0200  sample0 <- src0 (Backdrop)
            (ushort)(CustomEffectInterop.BackdropSampleArgument | 0x0001),              // 0x0201  sample1 <- src1 (Mask0)
            (ushort)(CustomEffectInterop.BackdropSampleArgument | 0x0002),              // 0x0202  sample2 <- src2 (Mask1)
            CustomEffectInterop.BackdropSamplerDataExtArgument,                         // 0x0400  samplerDataExt <- src0
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty
            {
                PublicName = "Mode",
                NativeName = "Mode",
                CbufferOffset = ModeCbufferOffset,
                DefaultValue = DefaultMode,
            },
        };

        // 128-byte LiquidGlassParams at Defaults + Mode=0 (Step slot).
        protected override byte[] ConstantBuffer =>
            GlassFieldBaker.BuildParamConstants(GlassFieldBaker.Params.Defaults(), DefaultMode);
    }
}
