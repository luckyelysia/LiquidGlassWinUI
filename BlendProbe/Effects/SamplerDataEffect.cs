using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 3 — samplerData (content-rect) route, single source. ABI: PSBody(float2
    // uv, float4 samplerData), args {0x0100, 0x0300}, linkingArgType 0x0200. Verifies
    // the 0x03 SamplerData linker argument reaches the shader. Expected: works.
    public sealed class SamplerDataEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-0003-4b03-9c03-012345670003");
        protected override string EffectName => "SamplerDataEffect";
        protected override string ShaderFileName => "SamplerData.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerData = true, WantsSamplerDataExt =true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSamplerDataArgument,
            CustomEffectInterop.BackdropSamplerDataExtArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
    }
}
