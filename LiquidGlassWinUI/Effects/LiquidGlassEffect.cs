using System;
using System.Collections.Generic;
using System.Linq;
using LiquidGlassWinUI.Interop;

namespace LiquidGlassWinUI.Effects
{
    // Control-oriented PRUNED liquid-glass effect (see Effects/Shaders/LiquidGlass.hlsl).
    // Same material algorithms and the SAME 128-byte cbuffer layout as the reference
    // Studio effect, but:
    //   - single rounded rect with isotropic superellipse corners (no circle, no
    //     smooth-min merge) -> ShowShape1 / MergeRate are gone
    //   - analytical normal computed in-shader -> no extra params for it
    //   - NO in-shader shadow (a control's shadow is drawn outside the brush by the
    //     platform) -> the four Shadow* params are gone
    //   - ShapeRadius is a 0..1 corner-radius fraction of the shorter half-side
    // The unused cbuffer slots (BlurEdge, Shadow*, MergeRate, ShowShape1,
    // SpringSizeFactor, Step, _Pad) stay 0; the 128-byte layout is unchanged,
    // so only the params below are exposed as animatable properties.
    // Custom-sampler route: UV arg 0x0100 + samplerDataExt 0x0400
    // + samplerData 0x0300, LinkingArgCustomSamplerResult, FlattenSource on.
    internal sealed class LiquidGlassEffect : CustomEffectBase
    {
        public const string EffectNameValue = "LiquidGlassEffect";

        // Physical px per logical px. Measured by the brush (window DPI) and baked
        // into the initial cbuffer at offset 124 (slot 31). Not a slider — the brush
        // sets it once when it connects; the shader scales band widths by it so
        // parameter values read as logical px across DPIs.
        public float Dpr { get; set; } = 1.0f;

        // 128-byte cbuffer = 32 floats. Matches register(b0) in LiquidGlass.hlsl.
        private const int CbufferFloats = 32;

        // The control-oriented subset of the reference parameters (order = cbuffer
        // order; offsets are the ABSOLUTE byte offsets shared with the reference so
        // the shader's cbuffer declaration matches byte-for-byte).
        internal static readonly LiquidParam[] Params =
        {
            // Refraction
            new() { Key = "RefThickness",       Offset = 0,   Default = 20,   Min = 1,    Max = 80,   Step = 0.01f, Group = "Refraction", Label = "Ref Thickness" },
            new() { Key = "RefFactor",           Offset = 4,   Default = 1.4f, Min = 1,    Max = 4,    Step = 0.01f, Group = "Refraction", Label = "Ref Factor (IOR)" },
            new() { Key = "RefDispersion",       Offset = 8,   Default = 7,    Min = 0,    Max = 50,   Step = 0.01f, Group = "Refraction", Label = "Dispersion" },
            new() { Key = "DispersionRange",    Offset = 116, Default = 1.0f, Min = 0,    Max = 1,    Step = 0.01f, Group = "Refraction", Label = "Dispersion Range" },
            new() { Key = "RefFresnelRange",     Offset = 12,  Default = 30,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Refraction", Label = "Fresnel Range" },
            new() { Key = "RefFresnelHardness",  Offset = 16,  Default = 20,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Refraction", Label = "Fresnel Hardness" },
            new() { Key = "RefFresnelFactor",    Offset = 20,  Default = 20,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Refraction", Label = "Fresnel Factor" },
            new() { Key = "Magnification",       Offset = 72,  Default = 1.0f, Min = 1.0f, Max = 3.0f, Step = 0.01f, Group = "Refraction", Label = "Magnification" },
            // Glare
            new() { Key = "GlareRange",          Offset = 24,  Default = 30,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Glare", Label = "Glare Range" },
            new() { Key = "GlareHardness",       Offset = 28,  Default = 20,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Glare", Label = "Glare Hardness" },
            new() { Key = "GlareFactor",         Offset = 32,  Default = 90,   Min = 0,    Max = 120,  Step = 0.01f, Group = "Glare", Label = "Glare Factor" },
            new() { Key = "GlareConvergence",    Offset = 36,  Default = 50,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Glare", Label = "Glare Convergence" },
            new() { Key = "GlareOppositeFactor", Offset = 40,  Default = 80,   Min = 0,    Max = 100,  Step = 0.01f, Group = "Glare", Label = "Glare Opposite Factor" },
            new() { Key = "GlareAngle",          Offset = 44,  Default = -45,  Min = -180, Max = 180,  Step = 0.01f, Group = "Glare", Label = "Glare Angle (deg)" },
            // Blur — BlurAmount at cbuffer offset 48. On the analytic path this slot is
            // unused in-shader (no upstream blur). On the baked path the shader reads it
            // for internal BackdropBlur. Kept here for default + layout parity.
            new() { Key = "BlurAmount",          Offset = 48,  Default = 1,    Min = 0,    Max = 5,   Step = 0.01f,    Group = "Blur", Label = "Blur Radius" },
            // Tint
            new() { Key = "TintR",               Offset = 56,  Default = 255,  Min = 0,    Max = 255,  Step = 1,    Group = "Tint", Label = "Tint R" },
            new() { Key = "TintG",               Offset = 60,  Default = 255,  Min = 0,    Max = 255,  Step = 1,    Group = "Tint", Label = "Tint G" },
            new() { Key = "TintB",               Offset = 64,  Default = 255,  Min = 0,    Max = 255,  Step = 1,    Group = "Tint", Label = "Tint B" },
            new() { Key = "TintA",               Offset = 68,  Default = 0,    Min = 0,    Max = 1,    Step = 0.01f, Group = "Tint", Label = "Tint Alpha" },
            // Exposure moved to PostProcessingEffect; cbuffer slot 52 kept at 1.0.
            // Shape (Width/Height dropped — the glass fills the brush rect = the control)
            new() { Key = "ShapeRadius",         Offset = 96,  Default = 0.4f, Min = 0,    Max = 1,    Step = 0.01f, Group = "Shape", Label = "Corner Radius (0..1)" },
            new() { Key = "ShapeRoundness",      Offset = 100, Default = 5,    Min = 2,    Max = 7,    Step = 0.01f, Group = "Shape", Label = "Roundness (n)" },
        };

        protected override Guid Id => new Guid("a2b8c4d6-7e9f-4a1b-8c3d-2e5f6a7b8c9d");
        protected override string EffectName => EffectNameValue;
        protected override string ShaderFileName => "LiquidGlass.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            // WantsSamplerData: samplerData = DWM's effective content rect of the
            // (expanded) intermediate. GaussianBlur grows padding with BlurAmount, so
            // raw uv no longer spans [0,1] over the panel; the shader remaps uv via
            // samplerData before deriving geometry, keeping the glass centered.
            new EffectSource { Name = "Backdrop", WantsSamplerData = true, WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<EffectProperty> Properties =>
            Params.Select(p => new EffectProperty
            {
                PublicName = p.Key,
                NativeName = p.Key,
                CbufferOffset = p.Offset,
                DefaultValue = p.Default,
            }).ToArray();

        protected override byte[] ConstantBuffer
        {
            get
            {
                float[] values = new float[CbufferFloats];
                foreach (var p in Params)
                {
                    values[p.Offset / 4] = p.Default;
                }
                values[13] = 1.0f; // Exposure slot (offset 52) — moved to PostProcessingEffect; keep at 1.0
                values[31] = Dpr; // cbuffer offset 124 (Dpr; not in Params — set by the brush)
                byte[] bytes = new byte[CbufferFloats * sizeof(float)];
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        // Order matches the PSBody signature: uv, samplerDataExt, samplerData.
        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
            CustomEffectInterop.BackdropSamplerDataArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;

        // FlattenSource makes DWM materialize the composed source (backdrop ->
        // BlurH -> BlurV) into a real intermediate texture the glass shader
        // samples as texture0 (see FlattenSource in the .hlsl).
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";
    }
}
