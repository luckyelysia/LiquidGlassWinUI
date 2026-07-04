using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // MaskBindingPage control A & B — a SINGLE-source custom-sampler effect whose
    // shader just returns `texture0.Sample`. It exists to answer ONE question with the
    // source held in isolation (no second source competing for the texture0 slot):
    //
    //   "When a CompositionSurfaceBrush (the baked mask) is the SOLE source bound to a
    //    sampler-route effect, does DWM materialize it as texture0?"
    //
    // Card A binds the mask surface here; card B binds CreateBackdropBrush(). Same
    // effect, same shader, only the SourceBinder differs — a clean A/B control:
    //   A shows the mask      => a surface brush IS a sampler source claiming texture0
    //                            even with no other source present (the root-cause test
    //                            for card 11's "texture0 = mask, not backdrop").
    //   B shows the scene text => the backdrop alone reaches texture0 (sanity baseline).
    //
    // Same ABI as card 2 (CustomSamplerUvEffect): PSBody(float2 uv, float4 samplerDataExt),
    // args {0x0100, 0x0400}, linkingArgType 0x0200, hasCustomSamplers. No properties, no
    // cbuffer — the shader is stateless.
    public sealed class MaskSamplerEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-000c-4b0c-9c0c-01234567000c");
        protected override string EffectName => "MaskSamplerEffect";
        protected override string ShaderFileName => "MaskSamplerProbe.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            CustomEffectInterop.BackdropUvArgument,             // 0x0100  float2 uv
            CustomEffectInterop.BackdropSamplerDataExtArgument, // 0x0400  texture0/sampler0
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
    }
}
