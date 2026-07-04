using System;
using System.Collections.Generic;

namespace BlendProbe.Effects
{
    // Card 1 — color sampling, single source. Color-input ABI: PSBody(float4
    // sample0), arg 0x0200, linkingArgType 0, no custom samplers. The simplest route
    // (matches CustomInvertEffect); inverts the backdrop. Expected: works.
    public sealed class ColorInvertEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-0001-4b01-9c01-012345670001");
        protected override string EffectName => "ColorInvertEffect";
        protected override string ShaderFileName => "ColorInvert.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop" },
        };
    }
}
