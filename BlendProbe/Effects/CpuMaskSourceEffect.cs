using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 11 — THE MASK-PRODUCTION DE-RISK PROBE (CPU bake as a color source).
    //
    // Same card-9 wiring (2 sources: Mask = color src0, Backdrop = sampler src1, args
    // {0x0100,0x0200,0x0401}, linkingArgType 0x0200, hasCustomSamplers, FlattenSource
    // OFF) — but where card 9 bound a flat CompositionColorBrush to "Mask", this card
    // binds a CPU-BAKED mask uploaded to a CompositionDrawingSurface. That exercises the
    // three net-new port risks in one card:
    //   1. CPU bake (CpuMaskBaker, reusing the verified rounded-rect SDF) -> BGRA8 bytes.
    //   2. Win2D upload (byte[] -> CanvasBitmap -> CompositionDrawingSurface -> brush).
    //   3. A TEXTURED (surface) brush as a color source — never tested before; whether
    //      DWM samples it 1:1 with output pixels (so the baked pattern lands sharp and
    //      correctly positioned) is the open question.
    //
    // A slider drives Factor (cbuffer _Params.x); the shader lerps sample0 <->
    // texture0.Sample by it, so each route is probed in isolation:
    //   Factor=0 -> pure sample0  = the baked mask. Crisp R/G position gradient + B
    //                            rounded-rect => CPU bake + upload + 1:1 textured-color
    //                            sampling all work (PRIMARY green light).
    //   Factor=1 -> pure texture0.Sample = the blurred backdrop. Bonus probe: a COMPOSED
    //                            blur brush sampleable as texture0 in a multi-source
    //                            (FlattenSource-OFF) effect. Black here => the known
    //                            "GaussianBlur-as-source needs FlattenSource" constraint
    //                            blocks multi-source (informational for Stage 2).
    public sealed class CpuMaskSourceEffect : CustomEffectBase
    {
        public const string EffectNameValue = "CpuMaskSourceEffect";
        public const string FactorPropertyPath = EffectNameValue + ".Factor";

        private const uint FactorCbufferOffset = 0;
        private const float DefaultFactor = 0f;

        protected override Guid Id => new Guid("a1b2c3d4-000b-4b0b-9c0b-01234567000b");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "CpuMaskSource.hlsl";

        // Order matters: src0=Mask (color, no sampler data) before src1=Backdrop (sampler).
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Mask" },                                       // src0, color route  (CPU-baked surface brush)
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },       // src1, custom-sampler route (blurred backdrop)
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
