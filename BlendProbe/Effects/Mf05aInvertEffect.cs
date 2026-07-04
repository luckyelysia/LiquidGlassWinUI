using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 5 stage0 — single-source color-route invert. Non-flatten, feeds into
    // Mf05bCrossBlend which materializes the inverted output via FlattenSource.
    public sealed class Mf05aInvertEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("2b05a008-0a05-4f05-9a05-000000000008");
        protected override string EffectName => "Mf05aInvertEffect";
        protected override string ShaderFileName => "Mf05aInvert.hlsl";

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
