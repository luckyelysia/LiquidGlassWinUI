using System;
using System.Collections.Generic;
using BlendProbe.Interop;
using BlendProbe.MaskBaking;

namespace BlendProbe.Effects
{
    // Glass A/B — the ANALYTIC reference side (left rect). One Backdrop source bound to
    // texture0 (the live backdrop, refracted inline). The shader re-computes every glass
    // field in float; this is the "before baking" renderer. Same cbuffer (Defaults) as
    // GlassBakedEffect so the two share identical material params for a fair A/B.
    //
    // ABI mirrors MaskSamplerEffect (cards A/B): args {0x0100, 0x0400}, custom sampler,
    // PSBody(float2 uv, float4 samplerDataExt).
    public sealed class GlassRefEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-00aa-4b0a-9aaa-0123456700aa");
        protected override string EffectName => "GlassRefEffect";
        protected override string ShaderFileName => "GlassRef.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,              // 0x0100  float2 uv
            CustomEffectInterop.BackdropSamplerDataExtArgument,  // 0x0400  texture0/sampler0
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;

        // 128-byte LiquidGlassParams at Defaults. Mode (Step slot) is unused on this side.
        protected override byte[] ConstantBuffer =>
            GlassFieldBaker.BuildParamConstants(GlassFieldBaker.Params.Defaults(), 0f);
    }
}
