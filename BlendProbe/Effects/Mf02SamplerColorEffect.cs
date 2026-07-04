using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 2 — N=2 mixed wiring. sampler(texture0) + color(float4 sample1).
    public sealed class Mf02SamplerColorEffect : CustomEffectBase
    {
        public const string EffectNameValue = "Mf02SamplerColorEffect";
        public const string FactorPropertyPath = EffectNameValue + ".Factor";

        private const uint FactorCbufferOffset = 0;
        private const float DefaultFactor = 0.5f;

        protected override Guid Id => new Guid("2b020002-0002-4f02-9f02-000000000002");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "Mf02SamplerColor.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true  },
            new EffectSource { Name = "ColorSrc", WantsSamplerDataExt = false },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            0x0100, 0x0400, 0x0201,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";

        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty { PublicName = "Factor", NativeName = "Factor", CbufferOffset = FactorCbufferOffset, DefaultValue = DefaultFactor },
        };

        protected override byte[] ConstantBuffer => BuildConstants(DefaultFactor);

        private static byte[] BuildConstants(float f) { var v = new[] { f, 0f, 0f, 0f }; var b = new byte[16]; Buffer.BlockCopy(v, 0, b, 0, 16); return b; }
    }
}
