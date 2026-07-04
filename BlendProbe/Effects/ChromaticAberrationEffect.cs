using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Combo group 3 middle stage — custom-sampler chromatic aberration. ABI is the SAME
    // FlattenSource route as FlattenCustomSamplerEffect (card 6): single source, args
    // {0x0100, 0x0400}, linkingArgType 0x0200, hasCustomSamplers, FlattenSource=true so
    // the upstream (blurred) intermediate materializes into texture0. The single 0x0400
    // slot in the whole DWM graph is consumed here, so no other stage in the chain may
    // also be a custom sampler.
    //
    // An animatable Offset (cbuffer _Params.x) drives how far the R and B channels are
    // shifted horizontally (R +offset, B -offset, G centered) — three texture samples.
    public sealed class ChromaticAberrationEffect : CustomEffectBase
    {
        public const string EffectNameValue = "ChromaticAberrationEffect";
        public const string OffsetPropertyPath = EffectNameValue + ".Offset";

        private const uint OffsetCbufferOffset = 0;
        private const float DefaultOffset = 0.008f;

        protected override Guid Id => new Guid("a1b2c3d4-000a-4b0a-9c0a-01234567000a");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "ChromaticAberration.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";

        // Slider-driven channel shift (cbuffer offset 0).
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
