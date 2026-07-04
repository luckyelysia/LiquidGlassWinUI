using System;
using System.Collections.Generic;
using LiquidGlassWinUI.Interop;

namespace LiquidGlassWinUI.Effects
{
    // Horizontal 1D separable Gaussian blur (first pass).
    // Chained as: backdrop -> BlurH -> BlurV -> glass.
    //
    // Weights are precomputed CPU-side (sigma = MAX_RADIUS/3, matching
    // liquid-glass-studio computeGaussianKernelByRadius), then adjacent
    // integer-offset tap pairs are merged into bilinear samples — one hardware
    // bilinear read replaces two point samples with zero visual difference
    // (41 taps → 21 taps, 49% fewer per pass). BlurAmount scales sample
    // spacing at runtime.
    //
    // Cbuffer layout (112 bytes = 28 floats):
    //   offset 0:   BlurAmount (float, animatable)
    //   offset 4:   _pad0 (float3)
    //   offset 16:  CenterWeight (float)
    //   offset 20:  _pad1 (float3)
    //   offset 32:  PairData (float4[5]; 10 merged (off,weight) pairs)
    internal sealed class BlurHEffect : CustomEffectBase
    {
        public const string EffectNameValue = "BlurHEffect";
        public const string BlurAmountPropertyPath = EffectNameValue + ".BlurAmount";

        private const int MaxBlurRadius = 20;   // original kernel radius (for σ)
        private const int MaxBlurPairs = 10;    // (1,2)…(19,20) merged
        private const uint BlurAmountCbufferOffset = 0;
        private const float DefaultBlurAmount = 1.0f;

        // Total cbuffer floats: BlurAmount(1) + pad(3) + CenterWeight(1) + pad(3) + PairData(20) = 28
        private const int CbufferFloats = 28;

        protected override Guid Id => new Guid("e1f2a3b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "BlurH.hlsl";

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

        // Precomputed: Gaussian weights → bilinear-merged (offset, weight) pairs.
        private static readonly float[] s_merged = BuildMergedPairs();

        /// <summary>
        /// Compute Gaussian kernel (σ = radius/3), then merge adjacent integer-offset
        /// pairs into bilinear samples. Each pair (i, i+1) becomes one sample at the
        /// weighted centroid with the combined weight — mathematically identical to
        /// two separate point samples thanks to hardware bilinear interpolation.
        /// </summary>
        private static float[] BuildMergedPairs()
        {
            float sigma = MaxBlurRadius / 3.0f;

            // Raw Gaussian weights (unnormalised).
            float[] w = new float[MaxBlurRadius + 1];
            for (int i = 0; i <= MaxBlurRadius; i++)
                w[i] = (float)Math.Exp(-0.5 * i * i / (sigma * sigma));

            // Merge adjacent pairs: (1,2) (3,4) … (19,20).
            // merged[2p]   = weighted-centroid offset  (fractional)
            // merged[2p+1] = combined weight
            float[] merged = new float[MaxBlurPairs * 2];
            float totalWeight = w[0]; // center
            for (int p = 0; p < MaxBlurPairs; p++)
            {
                int i0 = p * 2 + 1;
                int i1 = i0 + 1;
                float wSum = w[i0] + w[i1];
                float off = (i0 * w[i0] + i1 * w[i1]) / wSum;
                merged[p * 2] = off;
                merged[p * 2 + 1] = wSum;
                totalWeight += wSum * 2f; // symmetric ± pairs
            }

            // Normalise so sum = 1.
            float invTotal = 1f / totalWeight;
            float[] result = new float[1 + MaxBlurPairs * 2]; // center + 10*(off,weight)
            result[0] = w[0] * invTotal;
            for (int i = 0; i < merged.Length; i++)
                result[1 + i] = merged[i] * invTotal;

            return result;
        }

        private static byte[] BuildBlurConstants(float blurAmount)
        {
            // Layout: 0=BlurAmount, 1-3=pad, 4=CenterWeight, 5-7=pad, 8-27=PairData
            float[] values = new float[CbufferFloats];
            values[0] = blurAmount;
            // values[1..3] = 0 (pad)
            values[4] = s_merged[0]; // CenterWeight
            // values[5..7] = 0 (pad)
            for (int i = 0; i < MaxBlurPairs * 2; i++)
                values[8 + i] = s_merged[1 + i];
            // values[28] doesn't exist (28 floats, indices 0..27)
            byte[] bytes = new byte[CbufferFloats * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
