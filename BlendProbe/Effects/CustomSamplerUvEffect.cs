using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 2 — custom-sampler route, single source. ABI: PSBody(float2 uv, float4
    // samplerDataExt), args {0x0100, 0x0400}, linkingArgType 0x0200, hasCustomSamplers.
    // The shader samples texture0 itself and returns float4(uv,0,1) to PROVE the UV
    // spans [0,1] in this ABI (matches CustomBlurEffect). Expected: works.
    public sealed class CustomSamplerUvEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-0002-4b02-9c02-012345670002");
        protected override string EffectName => "CustomSamplerUvEffect";
        protected override string ShaderFileName => "CustomSamplerUv.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
    }
}
