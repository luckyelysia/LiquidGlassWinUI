using System;
using System.Collections.Generic;
using BlendProbe.Interop;
using Windows.Graphics.Effects;

namespace BlendProbe.Effects
{
    // One backdrop source for an effect. The runtime models the Backdrop kind only.
    public sealed class EffectSource
    {
        public string Name { get; set; }
        public bool WantsSamplerData { get; set; }
        public bool WantsSamplerDataExt { get; set; }
    }

    // One scalar property bound directly to a constant-buffer offset.
    public sealed class EffectProperty
    {
        public string PublicName { get; set; }
        public string NativeName { get; set; }
        public uint CbufferOffset { get; set; }
        public float DefaultValue { get; set; }
    }

    // Base for self-contained custom effects. Each subclass owns its identity,
    // shader file, sources, properties, constant buffer and linker flags; Create()
    // loads the HLSL from disk and assembles the projected IGraphicsEffect through
    // the shared builder. This mirrors the per-effect .h/.cpp split in the C++
    // runtime (CustomInvertEffect / CustomBlurEffect / CustomLiquidGlassEffect).
    public abstract class CustomEffectBase
    {
        protected abstract Guid Id { get; }
        protected abstract string EffectName { get; }
        protected abstract string ShaderFileName { get; }     // file under Effects/Shaders/
        protected virtual string ShaderFunctionName => "PSBody";
        protected virtual string FragmentName => EffectName;

        protected abstract IReadOnlyList<EffectSource> Sources { get; }
        protected virtual IReadOnlyList<EffectProperty> Properties => Array.Empty<EffectProperty>();
        protected virtual byte[] ConstantBuffer => null;

        // Color-only route by default (matches CustomInvertEffect: sample0 + arg
        // 0x0200). Override for custom-sampler effects (CustomBlurEffect uses UV +
        // samplerDataExt -> args 0x0100/0x0400, LinkingArgCustomSamplerResult).
        protected virtual IReadOnlyList<ushort> ShaderArguments => new[] { CustomEffectInterop.BackdropSampleArgument };
        protected virtual ushort LinkingArgType => CustomEffectInterop.LinkingArgColor;
        protected virtual bool HasCustomSamplers => false;
        protected virtual bool FlattenSource => false;
        protected virtual string FlattenShaderFunctionName => null;
        protected virtual bool KeepAsFragmentOutput => true;

        // Hot-reload: when non-empty, replaces Id at Create() time. The runtime's
        // RegisterEffect is idempotent — it returns early if a GUID is already
        // registered (CustomEffectRuntime.cpp: FindEntryByGuidLocked early-return)
        // and does NOT replace the stored shader. So re-registering the SAME id
        // after editing a .hlsl changes nothing. Bumping RuntimeId to a fresh GUID
        // makes the runtime see a brand-new effect and forces DWM to recompile the
        // edited shader source. Left empty for normal (single-shot) construction.
        private Guid _runtimeIdOverride = Guid.Empty;
        public Guid RuntimeId { set => _runtimeIdOverride = value; }

        public IGraphicsEffect Create()
        {
            string shaderSource = ShaderSourceLoader.Load(ShaderFileName);
            Guid id = _runtimeIdOverride != Guid.Empty ? _runtimeIdOverride : Id;

            var builder = new CustomEffectBuilder
            {
                Id = id,
                EffectName = EffectName,
                FragmentName = FragmentName,
                ShaderSource = shaderSource,
                ShaderFunctionName = ShaderFunctionName,
                FlattenShaderFunctionName = FlattenShaderFunctionName,
                ConstantBuffer = ConstantBuffer,
                LinkingArgType = LinkingArgType,
                HasCustomSamplers = HasCustomSamplers,
                FlattenSource = FlattenSource,
                KeepAsFragmentOutput = KeepAsFragmentOutput,
            };

            foreach (var source in Sources)
            {
                builder.BackdropSource(source.Name, source.WantsSamplerData, source.WantsSamplerDataExt);
            }
            foreach (var property in Properties)
            {
                builder.ScalarProperty(property.PublicName, property.NativeName, property.CbufferOffset, property.DefaultValue);
            }
            foreach (var argument in ShaderArguments)
            {
                builder.ShaderArgument(argument);
            }

            return builder.Build();
        }
    }
}
