using System;
using System.Collections.Generic;
using LiquidGlassWinUI.Interop;

namespace LiquidGlassWinUI.Effects
{
    // Unified post-processing stage inserted between BlurV and LiquidGlass.
    // Combines two functions in one shader pass:
    //
    //   1. Bloom blend — cross-fade between the blurred backdrop (custom-sampler
    //      route, texture0) and the raw backdrop (color route, sample1).
    //      lerp(blurred, raw, BloomAmount).
    //
    //   2. Colour adjustments — Brightness, Contrast, Saturation, Temperature,
    //      Exposure, Vibrance, applied in perceptual order.
    //
    // Two sources:
    //   src0 "Backdrop"    — blurred backdrop from upstream BlurH→BlurV
    //   src1 "RawBackdrop" — raw/unblurred backdrop
    //
    // Cbuffer layout (32 bytes = 8 floats, 16-byte aligned):
    //   offset  0: BloomAmount   [0, 1]
    //   offset  4: Brightness    [-1, 1]
    //   offset  8: Contrast      [0, 2]
    //   offset 12: Saturation    [0, 2]
    //   offset 16: Temperature   [-1, 1]
    //   offset 20: Exposure      [0.5, 2]
    //   offset 24: Vibrance      [0, 1]
    //   offset 28: _pad
    internal sealed class PostProcessingEffect : CustomEffectBase
    {
        public const string EffectNameValue = "PostProcessingEffect";

        // Property paths for compositor animations.
        public const string BloomAmountPropertyPath = EffectNameValue + ".BloomAmount";
        public const string BrightnessPropertyPath = EffectNameValue + ".Brightness";
        public const string ContrastPropertyPath = EffectNameValue + ".Contrast";
        public const string SaturationPropertyPath = EffectNameValue + ".Saturation";
        public const string TemperaturePropertyPath = EffectNameValue + ".Temperature";
        public const string ExposurePropertyPath = EffectNameValue + ".Exposure";
        public const string VibrancePropertyPath = EffectNameValue + ".Vibrance";

        private const uint CbufferSizeBytes = 32;
        private const float DefaultBloomAmount = 0.0f;
        private const float DefaultBrightness = 0.0f;
        private const float DefaultContrast = 1.0f;
        private const float DefaultSaturation = 1.0f;
        private const float DefaultTemperature = 0.0f;
        private const float DefaultExposure = 1.0f;
        private const float DefaultVibrance = 0.0f;

        protected override Guid Id => new Guid("c4d5e6f7-8a9b-4c1d-9e2f-4a5b6c7d8e9f");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "PostProcessing.hlsl";

        // Source order determines the low byte of shader arguments:
        //   index 0 = "Backdrop"    → samplerDataExt arg 0x0400 (sampler route)
        //   index 1 = "RawBackdrop" → sample arg 0x0201       (color route)
        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop",    WantsSamplerDataExt = true  },
            new EffectSource { Name = "RawBackdrop", WantsSamplerDataExt = false },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,                                  // 0x0100 → float2 uv
            CustomEffectInterop.BackdropSamplerDataExtArgument,                      // 0x0400 → float4 samplerDataExt (src0)
            (ushort)(CustomEffectInterop.BackdropSampleArgument | 0x0001),           // 0x0201 → float4 sample1       (src1)
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";

        protected override IReadOnlyList<EffectProperty> Properties => new[]
        {
            new EffectProperty { PublicName = "BloomAmount", NativeName = "BloomAmount", CbufferOffset = 0,  DefaultValue = DefaultBloomAmount },
            new EffectProperty { PublicName = "Brightness",  NativeName = "Brightness",  CbufferOffset = 4,  DefaultValue = DefaultBrightness },
            new EffectProperty { PublicName = "Contrast",    NativeName = "Contrast",    CbufferOffset = 8,  DefaultValue = DefaultContrast },
            new EffectProperty { PublicName = "Saturation",  NativeName = "Saturation",  CbufferOffset = 12, DefaultValue = DefaultSaturation },
            new EffectProperty { PublicName = "Temperature", NativeName = "Temperature", CbufferOffset = 16, DefaultValue = DefaultTemperature },
            new EffectProperty { PublicName = "Exposure",    NativeName = "Exposure",    CbufferOffset = 20, DefaultValue = DefaultExposure },
            new EffectProperty { PublicName = "Vibrance",    NativeName = "Vibrance",    CbufferOffset = 24, DefaultValue = DefaultVibrance },
        };

        protected override byte[] ConstantBuffer => BuildConstants(
            DefaultBloomAmount, DefaultBrightness, DefaultContrast, DefaultSaturation,
            DefaultTemperature, DefaultExposure, DefaultVibrance);

        private static byte[] BuildConstants(
            float bloomAmount, float brightness, float contrast, float saturation,
            float temperature, float exposure, float vibrance)
        {
            float[] values =
            {
                bloomAmount, brightness, contrast, saturation,
                temperature, exposure, vibrance, 0f, // pad
            };
            byte[] bytes = new byte[CbufferSizeBytes];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
