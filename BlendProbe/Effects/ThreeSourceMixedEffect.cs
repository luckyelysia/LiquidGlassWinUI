using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 10 — THE 3-SOURCE MIXED-WIRING PROBE (closes the last topology cell before
    // the mask-bake port). THREE declared sources from TWO different routes in one shader:
    //   src0 "Tex0"     -> color route, arg 0x0200 -> float4 sample0
    //   src1 "Tex1"     -> color route, arg 0x0201 -> float4 sample1
    //   src2 "Backdrop" -> custom-sampler route, arg 0x0402 -> texture0/sampler0
    // args {0x0100, 0x0200, 0x0201, 0x0402}, linkingArgType 0x0200, hasCustomSamplers.
    //
    // This is the production packing for the FULL bake (Tex0 + Tex1 = 8 static channels):
    // two static baked masks as color sources plus the live backdrop as the single
    // sampler texture. Card 8 proved 2 color sources (0x0200/0x0201); card 9 proved
    // 1 color + 1 sampler (0x0200/0x0401). This card proves them COMBINED: 2 color +
    // 1 sampler. The "single texture0 slot" limitation (memory: only ONE custom-sampler
    // source binds) is NOT violated here — there is exactly one sampler source (src2),
    // so it occupies the lone texture0 slot with no contention. Strongly expected to
    // work; this confirms it.
    //
    // A slider drives Factor (cbuffer _Params.x). The shader blends the two mask colors
    // 50/50, then lerps that with texture0.Sample by Factor, so each route is probed:
    //   Factor=0 -> 50/50 of the two mask colors (proves BOTH color routes bind)
    //   Factor=1 -> pure texture0.Sample        (proves the sampler route binds)
    //   Factor=0.5 -> all three blended
    public sealed class ThreeSourceMixedEffect : CustomEffectBase
    {
        public const string EffectNameValue = "ThreeSourceMixedEffect";
        public const string FactorPropertyPath = EffectNameValue + ".Factor";

        private const uint FactorCbufferOffset = 0;
        private const float DefaultFactor = 0.5f;

        protected override Guid Id => new Guid("a1b2c3d4-000a-4b0a-9c0a-01234567000a");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "ThreeSourceMixed.hlsl";

        // Order matters: src0=Tex0, src1=Tex1 (both color), src2=Backdrop (sampler). The
        // low byte of each arg selects this index.
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Tex0" },                                       // src0, color route
            new EffectSource { Name = "Tex1" },                                       // src1, color route
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },       // src2, custom-sampler route
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,           // 0x0100  float2 uv
            CustomEffectInterop.BackdropSampleArgument,       // 0x0200  float4 sample0  <- src0 (Tex0)
            (ushort)(CustomEffectInterop.BackdropSampleArgument | 0x0001),            // 0x0201  float4 sample1  <- src1 (Tex1)
            (ushort)(CustomEffectInterop.BackdropSamplerDataExtArgument | 0x0002),    // 0x0402  texture0/sampler0 <- src2 (Backdrop)
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
