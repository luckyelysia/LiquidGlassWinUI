using System;
using System.Collections.Generic;
using LiquidGlassWinUI.Interop;

namespace LiquidGlassWinUI.Effects
{
    // Vertical 1D separable Gaussian blur (second pass).
    // Chained as: backdrop -> BlurH -> BlurV -> glass.
    //
    // Identical to BlurHEffect except shader file and GUID.
    // See BlurHEffect.cs for bilinear-merging details and cbuffer layout.
    internal sealed class BlurVEffect : CustomEffectBase
    {
        public const string EffectNameValue = "BlurVEffect";
        public const string BlurAmountPropertyPath = EffectNameValue + ".BlurAmount";

        private const int MaxBlurRadius = 20;
        private const int MaxBlurPairs = 10;
        private const uint BlurAmountCbufferOffset = 0;
        private const float DefaultBlurAmount = 1.0f;
        private const int CbufferFloats = 28;

        protected override Guid Id => new Guid("f2a3b4c5-6d7e-8f9a-0b1c-2d3e4f5a6b7c");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "BlurV.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerData = false, WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty
            {
                PublicName = "BlurAmount",
                NativeName = "BlurAmount",
                CbufferOffset = BlurAmountCbufferOffset,
                DefaultValue = DefaultBlurAmount,
            },
        };

        protected override byte[] ConstantBuffer => BuildBlurConstants(DefaultBlurAmount);

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";

        private static readonly float[] s_merged = BuildMergedPairs();

        private static float[] BuildMergedPairs()
        {
            float sigma = MaxBlurRadius / 3.0f;

            float[] w = new float[MaxBlurRadius + 1];
            for (int i = 0; i <= MaxBlurRadius; i++)
                w[i] = (float)Math.Exp(-0.5 * i * i / (sigma * sigma));

            float[] merged = new float[MaxBlurPairs * 2];
            float totalWeight = w[0];
            for (int p = 0; p < MaxBlurPairs; p++)
            {
                int i0 = p * 2 + 1;
                int i1 = i0 + 1;
                float wSum = w[i0] + w[i1];
                float off = (i0 * w[i0] + i1 * w[i1]) / wSum;
                merged[p * 2] = off;
                merged[p * 2 + 1] = wSum;
                totalWeight += wSum * 2f;
            }

            float invTotal = 1f / totalWeight;
            float[] result = new float[1 + MaxBlurPairs * 2];
            result[0] = w[0] * invTotal;
            for (int i = 0; i < merged.Length; i++)
                result[1 + i] = merged[i] * invTotal;

            return result;
        }

        private static byte[] BuildBlurConstants(float blurAmount)
        {
            float[] values = new float[CbufferFloats];
            values[0] = blurAmount;
            values[4] = s_merged[0];
            for (int i = 0; i < MaxBlurPairs * 2; i++)
                values[8 + i] = s_merged[1 + i];
            byte[] bytes = new byte[CbufferFloats * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
