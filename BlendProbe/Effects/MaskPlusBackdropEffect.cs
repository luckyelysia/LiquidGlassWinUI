using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 9 — THE 2-SOURCE MIXED-WIRING PROBE (gating experiment for the mask-bake
    // production port). TWO declared sources from DIFFERENT routes in one shader:
    //   src0 "Mask"     -> color route,  arg 0x0200 -> float4 sample0
    //   src1 "Backdrop" -> custom-sampler route, arg 0x0401 -> texture0/sampler0
    // args {0x0100, 0x0200, 0x0401}, linkingArgType 0x0200, hasCustomSamplers.
    //
    // This is the empty cell in the linker-encoding table. Card 7 proved a single
    // source can populate BOTH sample0 AND texture0 simultaneously (0x0400 low byte
    // 0 = src0). Card 8 proved two COLOR sources (0x0200/0x0201). Nothing yet proves
    // a color source on src0 AND a custom-sampler source on src1 — which is exactly
    // what the mask-bake port needs: a static baked mask as a color source and the
    // live (flattened) backdrop as the single custom-sampler texture. The runtime
    // copies shader args verbatim with no mutual-exclusion guard (see card 7), so
    // whether src0 fills sample0 while src1 fills texture0 is purely DWM's linker
    // decision. Unknown until run.
    //
    // A slider drives Factor (cbuffer _Params.x). The shader lerps sample0<->
    // texture0.Sample by it, so each route can be probed IN ISOLATION:
    //   Factor=0   -> pure sample0        (proves the color route binds src0)
    //   Factor=1   -> pure texture0.Sample (proves the sampler route binds src1)
    //   Factor=0.5 -> 50/50 blend          (proves BOTH bound simultaneously)
    public sealed class MaskPlusBackdropEffect : CustomEffectBase
    {
        public const string EffectNameValue = "MaskPlusBackdropEffect";
        public const string FactorPropertyPath = EffectNameValue + ".Factor";

        private const uint FactorCbufferOffset = 0;
        private const float DefaultFactor = 0.5f;

        protected override Guid Id => new Guid("a1b2c3d4-0009-4b09-9c09-012345670009");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "MaskPlusBackdrop.hlsl";

        // Order matters: src0=Mask (color, no sampler data) MUST come before src1=
        // Backdrop (custom sampler). The low byte of each arg selects this index.
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Mask" },                                       // src0, color route
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },       // src1, custom-sampler route
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,           // 0x0100  float2 uv
            CustomEffectInterop.BackdropSampleArgument,       // 0x0200  float4 sample0  <- src0 (Mask)
            (ushort)(CustomEffectInterop.BackdropSamplerDataExtArgument | 0x0001), // 0x0401 texture0/sampler0 <- src1 (Backdrop)
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        // Slider-driven blend factor (cbuffer offset 0).
        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty
            {
                PublicName = "Factor",
                NativeName = "Factor",
                CbufferOffset = FactorCbufferOffset,
                DefaultValue = DefaultFactor,
            },
        };

        // 16-byte cbuffer: Factor at offset 0, padded to float4.
        protected override byte[] ConstantBuffer => BuildConstants(DefaultFactor);

        private static byte[] BuildConstants(float factor)
        {
            float[] values = { factor, 0f, 0f, 0f };
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
