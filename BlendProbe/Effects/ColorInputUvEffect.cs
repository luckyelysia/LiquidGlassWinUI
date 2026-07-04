using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 4 — color-input ABI WITH a UV argument. ABI: PSBody(float2 uv, float4
    // sample0), args {0x0100, 0x0200}, linkingArgType 0. The same UV-as-color viz as
    // card 2, but in the color-input ABI where DWM's explicit UV is in a wrong
    // coordinate space. Expected: FAILS (UV wrong) — the contrast pair with card 2.
    public sealed class ColorInputUvEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-0004-4b04-9c04-012345670004");
        protected override string EffectName => "ColorInputUvEffect";
        protected override string ShaderFileName => "ColorInputUv.hlsl";

        protected override IReadOnlyList<EffectSource> Sources => new[]
        {
            new EffectSource { Name = "Backdrop" },
        };

        protected override IReadOnlyList<ushort> ShaderArguments => new[]
        {
            CustomEffectInterop.BackdropUvArgument,
            CustomEffectInterop.BackdropSampleArgument,
        };

        protected override ushort LinkingArgType => CustomEffectInterop.LinkingArgColor;
        protected override bool HasCustomSamplers => false;
    }
}
