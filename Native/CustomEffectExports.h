#pragma once

// Flat-C ABI that lets a managed (C#) caller register a custom HLSL effect
// through CustomEffectRuntime without depending on C++/WinRT or the runtime's
// internal structs. The native glue (CustomEffectExports.cpp) deep-copies the
// caller's data into permanent process-heap memory and builds the real
// CustomEffectRuntime::CustomEffectDefinition from it.
//
// Build-specific wuceffectsi/dwmcorei offsets still live in CustomEffectRuntime;
// this layer only adapts the public, declarative surface.

#include <windows.h>
#include <stdint.h>

#ifdef CUSTOM_EFFECT_NATIVE_EXPORTS
#define CUSTOM_EFFECT_API __declspec(dllexport)
#else
#define CUSTOM_EFFECT_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum CustomEffectSourceKindFlat
{
    CustomEffectSourceKindFlat_Backdrop = 0,
};

struct CustomEffectSourceFlat
{
    const wchar_t* name;          // Source parameter name, e.g. L"Backdrop"
    int kind;                     // CustomEffectSourceKindFlat
    int wantsSamplerData;         // bool: request samplerData (effective content rect)
    int wantsSamplerDataExt;      // bool: request samplerDataExt (size/texel)
};

struct CustomEffectPropertyFlat
{
    const wchar_t* publicName;    // Public WinRT property name, e.g. L"Intensity"
    const char* nativeName;       // ANSI name used in native property metadata
    uint32_t cbufferOffset;       // Byte offset of this scalar inside the cbuffer
    float defaultValue;           // Scalar Single default returned during traversal
};

// POD mirror of CustomEffectRuntime::CustomEffectDefinition. All pointer/array
// fields are read synchronously inside CustomEffect_Create and copied to
// permanent native memory, so the caller may free its buffers immediately after
// the call returns.
struct CustomEffectDefinitionFlat
{
    GUID id;                                 // Unique private effect GUID
    const wchar_t* effectName;               // LPWStr; also the property-path prefix
    const char* fragmentName;                // ANSI; diagnostic/hash name only
    const uint8_t* shaderSource;             // HLSL source bytes
    uint64_t shaderSourceSize;               // Byte length of shaderSource
    const char* shaderFunctionName;          // ANSI HLSL entry, e.g. "PSBody"
    const char* flattenShaderFunctionName;   // ANSI; may be null unless flatten is set

    const CustomEffectSourceFlat* sources;
    uint32_t sourceCount;

    const CustomEffectPropertyFlat* properties;
    uint32_t propertyCount;

    const uint8_t* constantBufferInitial;    // Initial constant-buffer bytes
    uint32_t constantBufferSize;             // Must equal the shader's cbuffer size

    const uint16_t* shaderArguments;         // dwmcorei linker arg enum (0x0100/0x0200/...)
    uint64_t shaderArgumentCount;
    uint16_t linkingArgType;                 // 0x0200 for custom-sampler result
    int hasCustomSamplers;                   // bool
    int flattenSourceBeforeCustomSampler;    // bool
    int keepAsFragmentOutput;               // bool; true=keep fragment (default), false=allow non-flat
};

// Creates a Composition-compatible IGraphicsEffect for the definition.
//
// On success, *outEffectAbi receives the raw IGraphicsEffect ABI pointer with
// exactly ONE reference transferred to the caller. The managed wrapper must
// consume that ref (e.g. MarshalInspectable<T>.FromAbi adds its own, then call
// Marshal.Release once to drop the transferred ref).
//
// Thread/initialization: call on a WinRT-initialized thread after the
// Compositor/Window is live, so wuceffectsi.dll is loaded before the runtime
// patches it.
CUSTOM_EFFECT_API HRESULT CustomEffect_Create(
    const CustomEffectDefinitionFlat* definition,
    void** outEffectAbi);

// Compiles HLSL source with D3DCompile purely to SURFACE compiler errors. DWM
// silently drops any shader that fails to compile (the effect renders nothing),
// so this lets the managed caller see the actual error text. Returns the
// D3DCompile HRESULT; on failure the message is copied into outError.
//
// NOTE: the profile/entry here are for diagnostics only and may not match DWM's
// internal linker exactly; the goal is to catch syntax/type errors, which DWM
// would also reject.
CUSTOM_EFFECT_API HRESULT CustomEffect_CompileShader(
    const char* source,          // HLSL source bytes
    uint64_t sourceSize,
    const char* entryName,       // e.g. "PSBody"
    const char* profile,         // e.g. "ps_5_0"
    char* outError,              // caller-provided buffer; may be null
    uint32_t outErrorCap,        // capacity of outError in bytes
    uint32_t* outErrorLen);      // receives bytes written; may be null

#ifdef __cplusplus
}
#endif
