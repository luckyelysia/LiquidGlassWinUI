using System;
using System.Collections.Generic;
using BlendProbe.Interop;

namespace BlendProbe.Effects
{
    // Card 6 — effect chain WITH FlattenSource. Same custom-sampler ABI as card 2,
    // but flattenSourceBeforeCustomSampler=true so DWM materializes the upstream
    // (blurred) intermediate into a real texture the sampler reads as texture0.
    // Requires sourceCount==1 and a non-null flattenShaderFunctionName (runtime
    // CustomEffectRuntime.cpp:1113-1118). Expected: works (may stretch geometry).
    public sealed class FlattenCustomSamplerEffect : CustomEffectBase
    {
        protected override Guid Id => new Guid("a1b2c3d4-0006-4b06-9c06-012345670006");
        protected override string EffectName => "FlattenCustomSamplerEffect";
        protected override string ShaderFileName => "FlattenCustomSampler.hlsl";

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

        protected override bool FlattenSource => true;
        protected override string FlattenShaderFunctionName => "FlattenSource";
    }
}
