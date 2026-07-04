using System;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Effects;

namespace LiquidGlassWinUI.Interop
{
    // Flat-C P/Invoke mirror of Native\CustomEffectExports.h. All structs are
    // blittable so LayoutKind.Sequential produces the exact same memory layout
    // (including x64 alignment padding) as the native CustomEffect*Flat types.
    // Pointer fields are exposed as IntPtr and filled by CustomEffectBuilder at
    // build time; nothing here is auto-marshalled across the boundary.
    internal static class CustomEffectInterop
    {
        private const string DllName = "CustomEffectRuntimeNative.dll";

        // dwmcorei linker-argument enum (copied verbatim from CustomBlurEffect/
        // CustomInvertEffect): these select what DWM feeds PSBody's sampler inputs.
        public const ushort BackdropSampleArgument = 0x0200;        // color-only: float4 sample0
        public const ushort BackdropUvArgument = 0x0100;            // float2 uv
        public const ushort BackdropSamplerDataArgument = 0x0300;   // float4 samplerData (content rect)
        public const ushort BackdropSamplerDataExtArgument = 0x0400;// float4 samplerDataExt (size/texel)

        // The value placed in CustomEffectDefinitionFlat.LinkingArgType:
        //   color-only effect (sample0) => 0            (see CustomInvertEffect)
        //   custom-sampler effect (uv/texture0) => 0x0200 (see CustomBlurEffect)
        public const ushort LinkingArgColor = 0;
        public const ushort LinkingArgCustomSamplerResult = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        internal struct CustomEffectSourceFlat
        {
            public IntPtr name;        // LPWStr, source parameter name e.g. "Backdrop"
            public int kind;           // CustomEffectSourceKindFlat_Backdrop = 0
            public int wantsSamplerData;   // bool
            public int wantsSamplerDataExt;// bool
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CustomEffectPropertyFlat
        {
            public IntPtr publicName;  // LPWStr
            public IntPtr nativeName;  // ANSI (native metadata shader name)
            public uint cbufferOffset; // byte offset of this scalar in the cbuffer
            public float defaultValue; // Single default returned during traversal
        }

        // Field order must match CustomEffectExports.h::CustomEffectDefinitionFlat
        // exactly. Sequential layout reproduces the native alignment/padding.
        [StructLayout(LayoutKind.Sequential)]
        internal struct CustomEffectDefinitionFlat
        {
            public Guid id;
            public IntPtr effectName;              // LPWStr; also the animatable-path prefix
            public IntPtr fragmentName;            // ANSI
            public IntPtr shaderSource;            // byte[] HLSL source
            public ulong shaderSourceSize;
            public IntPtr shaderFunctionName;      // ANSI HLSL entry, e.g. "PSBody"
            public IntPtr flattenShaderFunctionName; // ANSI; null unless FlattenSource is set
            public IntPtr sources;                 // CustomEffectSourceFlat*
            public uint sourceCount;
            public IntPtr properties;              // CustomEffectPropertyFlat*
            public uint propertyCount;
            public IntPtr constantBufferInitial;   // byte[]
            public uint constantBufferSize;
            public IntPtr shaderArguments;         // ushort*
            public ulong shaderArgumentCount;
            public ushort linkingArgType;
            public int hasCustomSamplers;          // bool
            public int flattenSourceBeforeCustomSampler; // bool
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CustomEffect_Create(
            ref CustomEffectDefinitionFlat definition,
            out IntPtr effectAbi);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CustomEffect_CompileShader(
            IntPtr source,
            ulong sourceSize,
            [MarshalAs(UnmanagedType.LPStr)] string entryName,
            [MarshalAs(UnmanagedType.LPStr)] string profile,
            StringBuilder outError,
            uint outErrorCap,
            out uint outErrorLen);

        // Diagnostics-only: compiles HLSL with D3DCompile and returns the compiler
        // error text (null if it compiles clean). Used to surface the errors DWM
        // swallows when a shader fails to compile (effect then renders nothing).
        public static string CompileShader(string source, string entry = "PSBody", string profile = "ps_5_0")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            GCHandle h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var sb = new StringBuilder(16384);
                int hr = CustomEffect_CompileShader(
                    h.AddrOfPinnedObject(),
                    (ulong)bytes.Length,
                    entry, profile,
                    sb, (uint)sb.Capacity, out uint len);

                if (sb.Length > 0)
                {
                    return sb.ToString();
                }
                return hr >= 0 ? null : ("D3DCompile failed: HR=0x" + ((uint)hr).ToString("X8") + " (no message)");
            }
            finally
            {
                h.Free();
            }
        }

        // Creates the projected IGraphicsEffect.
        //
        // Ref-counting: CustomEffect_Create detaches exactly one reference into
        // effectAbi (native detach_abi). MarshalInspectable<T>.FromAbi AddRefs on
        // its own, so the RCW ends up holding a ref on top of the detached one.
        // We Marshal.Release(ptr) once to drop the transferred ref, leaving the
        // RCW with exactly one (balanced by GC/finalization).
        public static IGraphicsEffect Create(ref CustomEffectDefinitionFlat definition)
        {
            int hr = CustomEffect_Create(ref definition, out IntPtr ptr);
            Marshal.ThrowExceptionForHR(hr);

            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("CustomEffect_Create returned S_OK but no effect pointer.");
            }

            try
            {
                var effect = WinRT.MarshalInspectable<IGraphicsEffect>.FromAbi(ptr);
                Marshal.Release(ptr); // drop the reference detach_abi transferred
                return effect;
            }
            catch
            {
                Marshal.Release(ptr);
                throw;
            }
        }
    }
}
