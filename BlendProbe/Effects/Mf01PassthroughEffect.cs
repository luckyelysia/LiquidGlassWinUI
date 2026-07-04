using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 1 — N=1 regression. Single source FlattenSource passthrough.
    public sealed class Mf01PassthroughEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("2b010001-0001-4f01-9f01-000000000001");
        protected override string EffectName => "Mf01PassthroughEffect";
        protected override string ShaderFileName => "Mf01Passthrough.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop", WantsSamplerDataExt = true },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[]
        {
            0x0100, 0x0400,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgCustomSamplerResult;
        protected override bool HasCustomSamplers => true;
        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";
    }
}
