using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // MaskBindingPage card D — the SWAP probe. Same wiring as card C but source order
    // REVERSED: src0 = Backdrop (CreateBackdropBrush), src1 = Mask (surface). Card C had
    // Mask=src0 and the mask surface stole texture0 (backdrop was relegated to sample1, NOT
    // dropped). The open question this answers: with the backdrop FIRST, does texture0 become
    // the BACKDROP (=> mask-bake viable: texture0 = backdrop refraction + sample1 = mask
    // fields, both readable) or does the mask surface STILL claim texture0 from the src1 slot
    // (=> single-sampler hard wall; lossless bake blocked)?
    //
    //   Mode 0 -> sample0   (src0 color route; expect backdrop if routing is positional)
    //   Mode 1 -> sample1   (src1 color route; expect mask)
    //   Mode 2 -> texture0  (THE CRUX)
    //   Mode 3 -> split sample0 | sample1 | texture0   (one-glance verdict; default)
    //   Mode 4 -> uv        (atlas-uv sanity)
    //   Mode 5 -> magenta   (shader-runs check)
    public sealed class MaskBindingSwapProbeEffect : CustomEffectBase
    {
        public const string EffectNameValue = "MaskBindingSwapProbeEffect";
        public const string ModePropertyPath = EffectNameValue + ".Mode";

        private const uint ModeCbufferOffset = 0;
        private const float DefaultMode = 3f; // start on the 3-way split (one-glance verdict)

        protected override Guid Id => new Guid("a1b2c3d4-000e-4b0e-9c0e-01234567000e");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "MaskBindingSwapProbe.hlsl";

        // SWAPPED vs card C: src0 = Backdrop FIRST, src1 = Mask(surface).
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerData = true, WantsSamplerDataExt = true }, // src0
            new EffectSource { Name = "Mask" },                                                            // src1 (surface)
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,                                     // 0x0100  uv
            CustomEffectInterop.BackdropSampleArgument,                                 // 0x0200  sample0 <- src0 (Backdrop)
            (ushort)(CustomEffectInterop.BackdropSampleArgument | 0x0001),              // 0x0201  sample1 <- src1 (Mask)
            CustomEffectInterop.BackdropSamplerDataArgument,                            // 0x0300  samplerData <- src0
            CustomEffectInterop.BackdropSamplerDataExtArgument,                         // 0x0400  samplerDataExt <- src0
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        // Mode selector (cbuffer offset 0), driven live by the page's slider.
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

        // 16-byte cbuffer: Mode at offset 0, padded to float4.
        protected override byte[] ConstantBuffer => BuildConstants(DefaultMode);

        private static byte[] BuildConstants(float mode)
        {
            float[] values = { mode, 0f, 0f, 0f };
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
