using System;
using System.Collections.Generic;

namespace BlendProbe.Effects
{
    // Card 8 — two-source color-input blend. ABI: PSBody(float4 sample0, float4
    // sample1), args {0x0200, 0x0200} (both low byte 0x00 — DWM binds the Nth color
    // arg to the Nth declared source by ordinal, per WinUI3/CustomBlendEffect.cpp:32-36;
    // NOT 0x0201). linkingArgType 0, no custom samplers. One animatable scalar Factor
    // (cbuffer offset 0). The page binds Tex0=backdrop, Tex1=CompositionColorBrush so
    // the wipe is visually distinct. NOTE: memory says {0x0200,0x0201}; this card uses
    // the in-repo reference encoding and lets the run disambiguate.
    public sealed class TwoColorBlendEffect : CustomEffectBase
    {
        public const string EffectNameValue = "TwoColorBlendEffect";
        public const string FactorPropertyPath = EffectNameValue + ".Factor";

        private const uint FactorCbufferOffset = 0;
        private const float DefaultFactor = 0.5f;

        protected override Guid Id => new Guid("a1b2c3d4-0008-4b08-9c08-012345670008");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "TwoColorBlend.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Tex0" },
            new EffectSource { Name = "Tex1" },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            0x0200,
            0x0201,
        };

        protected override ushort LinkingArgType => 0;
        protected override bool HasCustomSamplers => false;

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
