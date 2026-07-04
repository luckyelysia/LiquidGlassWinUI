using LiquidGlassWinUI.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiquidGlassWinUI.Effects;

internal class DebugEffect : CustomEffectBase
{
    public const string EffectNameValue = "DebugEffect";

    protected override Guid Id => Guid.Parse("545FC019-663C-4590-93A7-95F83B204474");

    protected override string EffectName => EffectNameValue;

    protected override string ShaderFileName => "Debug.hlsl";

    protected override IReadOnlyList<EffectSource> Sources => [];

    protected override IReadOnlyList<ushort> ShaderArguments => [CustomEffectInterop.BackdropUvArgument];


}
