using System;
using System.Collections.Generic;

namespace BlendProbe.Effects
{
    // Combo group terminal — color-input tint. ABI: PSBody(float4 sample0), arg 0x0200,
    // linkingArgType 0, no custom samplers (the default ColorInvert-style route). A single
    // animatable scalar Amount (cbuffer offset 0) lerps the backdrop toward a FIXED warm
    // tint baked into the constant buffer's yzw (1.0, 0.55, 0.15). Only Amount is exposed
    // to sliders; the tint itself never changes, so the slider controls intensity alone.
    public sealed class ColorTintEffect : CustomEffectBase
    {
        public const string EffectNameValue = "ColorTintEffect";
        public const string AmountPropertyPath = EffectNameValue + ".Amount";

        private const uint AmountCbufferOffset = 0;
        private const float DefaultAmount = 0.5f;

        // Fixed warm tint (sRGB-ish orange) packed into the cbuffer after Amount.
        private const float TintR = 1.0f;
        private const float TintG = 0.55f;
        private const float TintB = 0.15f;

        protected override Guid Id => new Guid("a1b2c3d4-0009-4b09-9c09-012345670009");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "ColorTint.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop" },
        };

        // Default color-input route: arg {0x0200}, linkingArgType 0, no custom samplers.

        // Slider-driven tint strength (cbuffer offset 0).
        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty
            {
                PublicName = "Amount",
                NativeName = "Amount",
                CbufferOffset = AmountCbufferOffset,
                DefaultValue = DefaultAmount,
            },
        };

        // 16-byte cbuffer: {Amount, R, G, B}. Amount updates at runtime; RGB stay fixed.
        protected override byte[] ConstantBuffer => BuildConstants(DefaultAmount);

        private static byte[] BuildConstants(float amount)
        {
            float[] values = { amount, TintR, TintG, TintB };
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
