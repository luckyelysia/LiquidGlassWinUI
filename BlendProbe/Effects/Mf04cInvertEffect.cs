using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 4 stage2 — single-source color-route invert. Non-flatten consumer.
    public sealed class Mf04cInvertEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("2b04c007-0c04-4f04-9c04-000000000007");
        protected override string EffectName => "Mf04cInvertEffect";
        protected override string ShaderFileName => "Mf04cInvert.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop" },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new ushort[] { 0x0200 };
        protected override ushort LinkingArgType => 0;
        protected override bool HasCustomSamplers => false;
        protected override bool FlattenSource => false;
    }
}
