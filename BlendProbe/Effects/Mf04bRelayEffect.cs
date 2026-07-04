using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 4 stage1 — N=1 relay. keepAsFragmentOutput=false sanitizes upstream
    // fragment output so downstream non-flatten effects can consume it.
    public sealed class Mf04bRelayEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("2b04b006-0b04-4f04-9b04-000000000006");
        protected override string EffectName => "Mf04bRelayEffect";
        protected override string ShaderFileName => "Mf04bRelay.hlsl";

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
        protected override bool KeepAsFragmentOutput => false; // relay mode
    }
}
