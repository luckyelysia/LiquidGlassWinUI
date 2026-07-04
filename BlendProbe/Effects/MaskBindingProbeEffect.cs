using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // MaskBindingPage card C — the decisive 2-source probe. SAME topology as card 9 /
    // card 11 (Mask = color src0, Backdrop = sampler src1, args {0x0100,0x0200,0x0401},
    // linkingArgType 0x0200, hasCustomSamplers, FlattenSource OFF), but instead of a
    // Factor lerp it exposes a Mode selector so each input can be read OFF in isolation:
    //
    //   Mode 0 -> sample0           (color route, src0 = mask surface)
    //   Mode 1 -> texture0.Sample   (sampler route, src1 = backdrop)
    //   Mode 2 -> uv                (UV sanity)
    //   Mode 3 -> samplerDataExt    (size/texel metadata)
    //   Mode 4 -> split sample0 | texture0   (side-by-side, the headline view)
    //   Mode 5 -> fixed magenta     (confirms the shader actually runs)
    //
    // This pins down card 11's open question — "texture0 returned the baked mask, not
    // the backdrop; where did the backdrop go?" — by showing EVERY input on the same
    // scene (which contains text, so backdrop vs mask is unambiguous). See
    // MaskBindingProbe.hlsl for the per-mode logic and how to read each result.
    public sealed class MaskBindingProbeEffect : CustomEffectBase
    {
        public const string EffectNameValue = "MaskBindingProbeEffect";
        public const string ModePropertyPath = EffectNameValue + ".Mode";

        private const uint ModeCbufferOffset = 0;
        private const float DefaultMode = 4f; // start on the split view (most informative)

        protected override Guid Id => new Guid("a1b2c3d4-000d-4b0d-9c0d-01234567000d");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "MaskBindingProbe.hlsl";

        // Order matters: src0=Mask (color route) before src1=Backdrop (sampler).
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Mask" },                                       // src0, color route  (baked mask surface)
            new EffectSource { Name = "Backdrop",WantsSamplerData=true, WantsSamplerDataExt = true  },       // src1, custom-sampler route (live backdrop)
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,           // 0x0100  float2 uv
            CustomEffectInterop.BackdropSampleArgument,       // 0x0200  float4 sample0  <- src0 (Mask)
            (ushort)(CustomEffectInterop.BackdropSampleArgument| 0x0001), // 0x0201 texture0/sampler0 <- src1 (Backdrop)
            (ushort)(CustomEffectInterop.BackdropSamplerDataArgument | 0x0001), // 0x0401 texture0/sampler0 <- src1 (Backdrop)
            (ushort)(CustomEffectInterop.BackdropSamplerDataExtArgument | 0x0001), // 0x0401 texture0/sampler0 <- src1 (Backdrop)
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
