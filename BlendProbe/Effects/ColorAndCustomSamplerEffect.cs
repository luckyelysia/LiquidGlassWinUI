using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 7 — THE HEADLINE PROBE: can a custom-sampler body ALSO receive the 0x0200
    // implicit color sample in the same shader? ABI: PSBody(float2 uv, float4 sample0,
    // float4 samplerDataExt) declaring Texture2D texture0, args {0x0100, 0x0200, 0x0400},
    // linkingArgType 0x0200, hasCustomSamplers. The runtime copies shader args verbatim
    // (no mutual-exclusion guard), so whether BOTH sample0 AND texture0 populate is
    // purely DWM's linker decision. Unknown until run: may render the blend (supported),
    // drop just the texture (color only), or drop the whole shader (blank).
    //
    // A slider drives the texture's horizontal Offset (cbuffer _Params.x). The shader
    // forces the region shifted past the texture's right edge to transparent so the
    // backdrop shows through there (instead of a clamped edge streak).
    public sealed class ColorAndCustomSamplerEffect : CustomEffectBase
    {
        public const string EffectNameValue = "ColorAndCustomSamplerEffect";
        public const string OffsetPropertyPath = EffectNameValue + ".Offset";

        private const uint OffsetCbufferOffset = 0;
        private const float DefaultOffset = 0.05f;

        protected override Guid Id => new Guid("a1b2c3d4-0007-4b07-9c07-012345670007");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "ColorAndCustomSampler.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSampleArgument,         // color sample0
            CustomEffectInterop.BackdropSamplerDataExtArgument, // texture0/sampler0 binding
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        // Slider-driven horizontal texture offset (cbuffer offset 0).
        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty
            {
                PublicName = "Offset",
                NativeName = "Offset",
                CbufferOffset = OffsetCbufferOffset,
                DefaultValue = DefaultOffset,
            },
        };

        // 16-byte cbuffer: Offset at offset 0, padded to float4.
        protected override byte[] ConstantBuffer => BuildConstants(DefaultOffset);

        private static byte[] BuildConstants(float offset)
        {
            float[] values = { offset, 0f, 0f, 0f };
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
