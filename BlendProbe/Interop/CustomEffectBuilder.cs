using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Effects;

namespace BlendProbe.Interop
{
    // Fluent builder over CustomEffectInterop. The native layer deep-copies every
    // pointer/array to permanent process heap during CustomEffect_Create, so all
    // managed allocations here only need to survive that single call and are then
    // released/unpinned in a finally block.
    public sealed class CustomEffectBuilder
    {
        private readonly List<CustomEffectInterop.CustomEffectSourceFlat> _sources = new();
        private readonly List<string> _sourceNames = new();
        private readonly List<CustomEffectInterop.CustomEffectPropertyFlat> _properties = new();
        private readonly List<ushort> _shaderArguments = new();

        public Guid Id { get; set; }
        public string EffectName { get; set; } = string.Empty;
        public string FragmentName { get; set; } = string.Empty;
        public string ShaderSource { get; set; } = string.Empty;
        public string ShaderFunctionName { get; set; } = "PSBody";
        public string FlattenShaderFunctionName { get; set; } = null;
        public byte[] ConstantBuffer { get; set; } = null;
        public ushort LinkingArgType { get; set; } = CustomEffectInterop.LinkingArgColor;
        public bool HasCustomSamplers { get; set; } = true;
        public bool FlattenSource { get; set; } = false;
        public bool KeepAsFragmentOutput { get; set; } = true;

        // Backdrop source. wantsSamplerData/wantsSamplerDataExt select which
        // sampler inputs the linker hands to PSBody; for a color-only effect both
        // are false (see CustomInvertEffect).
        public CustomEffectBuilder BackdropSource(string name, bool wantsSamplerData, bool wantsSamplerDataExt)
        {
            _sources.Add(new CustomEffectInterop.CustomEffectSourceFlat
            {
                name = IntPtr.Zero,
                kind = 0,
                wantsSamplerData = wantsSamplerData ? 1 : 0,
                wantsSamplerDataExt = wantsSamplerDataExt ? 1 : 0,
            });
            _sourceNames.Add(name);
            return this;
        }

        // A scalar Single property bound directly to a cbuffer offset. Both
        // publicName and nativeName may be identical (as in CustomLiquidGlassEffect).
        public CustomEffectBuilder ScalarProperty(string publicName, string nativeName, uint cbufferOffset, float defaultValue)
        {
            _properties.Add(new CustomEffectInterop.CustomEffectPropertyFlat
            {
                publicName = IntPtr.Zero,
                nativeName = IntPtr.Zero,
                cbufferOffset = cbufferOffset,
                defaultValue = defaultValue,
            });
            _propPublicNames.Add(publicName);
            _propNativeNames.Add(nativeName);
            return this;
        }

        private readonly List<string> _propPublicNames = new();
        private readonly List<string> _propNativeNames = new();

        public CustomEffectBuilder ShaderArgument(ushort arg)
        {
            _shaderArguments.Add(arg);
            return this;
        }

        public IGraphicsEffect Build()
        {
            if (string.IsNullOrEmpty(EffectName))
                throw new InvalidOperationException("EffectName is required.");
            if (string.IsNullOrEmpty(ShaderSource))
                throw new InvalidOperationException("ShaderSource is required.");
            if (_sources.Count == 0)
                throw new InvalidOperationException("At least one source is required.");

            byte[] shaderBytes = Encoding.UTF8.GetBytes(ShaderSource);

            // Native-heap strings (freed after the call). Wide strings use
            // StringToHGlobalUni, ANSI strings use StringToHGlobalAnsi so the
            // native DupAnsi path (which uses strlen) sees a plain C string.
            List<IntPtr> nativeStrings = new();
            List<GCHandle> pinnedArrays = new();
            GCHandle srcHandle = default, propHandle = default, argHandle = default, shaderHandle = default, cbufferHandle = default;
            bool pinnedSrc = false, pinnedProp = false, pinnedArg = false, pinnedShader = false, pinnedCbuffer = false;

            try
            {
                var flat = new CustomEffectInterop.CustomEffectDefinitionFlat
                {
                    id = Id,
                    effectName = AllocWide(nativeStrings, EffectName),
                    fragmentName = AllocAnsi(nativeStrings, FragmentName),
                    shaderFunctionName = AllocAnsi(nativeStrings, ShaderFunctionName),
                    flattenShaderFunctionName = string.IsNullOrEmpty(FlattenShaderFunctionName)
                        ? IntPtr.Zero
                        : AllocAnsi(nativeStrings, FlattenShaderFunctionName),
                    linkingArgType = LinkingArgType,
                    hasCustomSamplers = HasCustomSamplers ? 1 : 0,
                    flattenSourceBeforeCustomSampler = FlattenSource ? 1 : 0,
                    keepAsFragmentOutput = KeepAsFragmentOutput ? 1 : 0,
                };

                // Sources: fill in the native name pointers, then pin the array.
                for (int i = 0; i < _sources.Count; i++)
                {
                    var src = _sources[i];
                    src.name = AllocWide(nativeStrings, _sourceNames[i]);
                    _sources[i] = src;
                }
                flat.sources = PinArray(_sources.ToArray(), out srcHandle, out pinnedSrc);
                flat.sourceCount = (uint)_sources.Count;

                // Properties: fill in the name pointers, then pin.
                for (int i = 0; i < _properties.Count; i++)
                {
                    var prop = _properties[i];
                    prop.publicName = AllocWide(nativeStrings, _propPublicNames[i]);
                    prop.nativeName = AllocAnsi(nativeStrings, _propNativeNames[i]);
                    _properties[i] = prop;
                }
                if (_properties.Count > 0)
                {
                    flat.properties = PinArray(_properties.ToArray(), out propHandle, out pinnedProp);
                    flat.propertyCount = (uint)_properties.Count;
                }
                else
                {
                    flat.properties = IntPtr.Zero;
                    flat.propertyCount = 0;
                }

                // Shader source bytes (UTF-8). Pin the managed byte[].
                flat.shaderSource = PinArray(shaderBytes, out shaderHandle, out pinnedShader);
                flat.shaderSourceSize = (ulong)shaderBytes.Length;

                // Constant buffer bytes.
                if (ConstantBuffer != null && ConstantBuffer.Length > 0)
                {
                    flat.constantBufferInitial = PinArray(ConstantBuffer, out cbufferHandle, out pinnedCbuffer);
                    flat.constantBufferSize = (uint)ConstantBuffer.Length;
                }
                else
                {
                    flat.constantBufferInitial = IntPtr.Zero;
                    flat.constantBufferSize = 0;
                }

                // Shader-linking arguments.
                if (_shaderArguments.Count > 0)
                {
                    flat.shaderArguments = PinArray(_shaderArguments.ToArray(), out argHandle, out pinnedArg);
                    flat.shaderArgumentCount = (ulong)_shaderArguments.Count;
                }
                else
                {
                    flat.shaderArguments = IntPtr.Zero;
                    flat.shaderArgumentCount = 0;
                }

                return CustomEffectInterop.Create(ref flat);
            }
            finally
            {
                if (pinnedSrc) srcHandle.Free();
                if (pinnedProp) propHandle.Free();
                if (pinnedArg) argHandle.Free();
                if (pinnedShader) shaderHandle.Free();
                if (pinnedCbuffer) cbufferHandle.Free();

                foreach (var p in nativeStrings)
                {
                    if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                }
            }
        }

        private static IntPtr AllocWide(List<IntPtr> tracker, string value)
        {
            if (value == null) return IntPtr.Zero;
            IntPtr p = Marshal.StringToHGlobalUni(value);
            tracker.Add(p);
            return p;
        }

        private static IntPtr AllocAnsi(List<IntPtr> tracker, string value)
        {
            if (value == null) return IntPtr.Zero;
            IntPtr p = Marshal.StringToHGlobalAnsi(value);
            tracker.Add(p);
            return p;
        }

        // Pins a managed array of blittable elements and returns a pointer to its
        // first element. The element layout of blittable structs in a managed
        // array matches the native array layout (same per-element size/alignment),
        // so the native pointer can be read as a plain C array.
        private static IntPtr PinArray<T>(T[] array, out GCHandle handle, out bool pinned)
        {
            handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            pinned = true;
            return handle.AddrOfPinnedObject();
        }
    }
}
