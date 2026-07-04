using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 3 stage1 — N=2 multiply-tint. Stage1 of the 2N→2N cascade.
    public sealed class Mf03bTintEffect : CustomEffectBase
    {
        public const string EffectNameValue = "Mf03bTintEffect";
        public const string AmountPropertyPath = EffectNameValue + ".Amount";

        private const uint AmountCbufferOffset = 0;
        private const float DefaultAmount = 0.5f;

        protected override Guid Id => new Guid("2b03b004-0b03-4f03-9b03-000000000004");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "Mf03bTint.hlsl";

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
            new EffectProperty { PublicName = "Amount", NativeName = "Amount", CbufferOffset = AmountCbufferOffset, DefaultValue = DefaultAmount },
        };

        protected override byte[] ConstantBuffer => BuildConstants(DefaultAmount);

        private static byte[] BuildConstants(float a) { var v = new[] { a, 0f, 0f, 0f }; var b = new byte[16]; Buffer.BlockCopy(v, 0, b, 0, 16); return b; }
    }
}
