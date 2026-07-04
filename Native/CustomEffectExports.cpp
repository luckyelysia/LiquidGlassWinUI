// CUSTOM_EFFECT_NATIVE_EXPORTS is provided by the project's PreprocessorDefinitions
// (so CustomEffectExports.h selects dllexport). pch.h resolves to ..\WinUI3\pch.h
// (via the include path), which already pulls windows.h, d3dcompiler.h, the
// C++/WinRT projection, and the WinAppSDK composition headers the runtime needs.
#include "pch.h"
#include "CustomEffectExports.h"
#include "CustomEffectRuntime.h"

#include <atomic>
#include <cstring>
#include <cwchar>
#include <utility>

using namespace winrt;

namespace
{
    // The runtime stores the CustomEffectDefinition pointer permanently and reads
    // it later from DWM worker threads (see CustomEffectRuntime.cpp). Every field
    // we hand it must therefore live for the process lifetime. These helpers
    // allocate permanent copies on the C++ heap; they are intentionally never
    // freed, matching the existing static-const definition lifetime model.

    char* DupAnsi(char const* source)
    {
        if (!source)
        {
            return nullptr;
        }

        auto const bytes = std::strlen(source) + 1;
        auto* copy = new char[bytes];
        std::memcpy(copy, source, bytes);
        return copy;
    }

    wchar_t* DupWide(wchar_t const* source)
    {
        if (!source)
        {
            return nullptr;
        }

        auto const count = std::wcslen(source) + 1;
        auto* copy = new wchar_t[count];
        std::memcpy(copy, source, count * sizeof(wchar_t));
        return copy;
    }

    uint8_t* DupBytes(void const* source, size_t size)
    {
        if (!source || !size)
        {
            return nullptr;
        }

        auto* copy = new uint8_t[size];
        std::memcpy(copy, source, size);
        return copy;
    }

    // PropertyValue::CreateSingle wrapper, mirroring CustomLiquidGlassEffect.cpp's
    // CreateScalarProperty so the returned ABI pointer matches what wuceffectsi's
    // traversal expects for the public IGraphicsEffectD2D1Interop::GetProperty path.
    HRESULT CreateScalarProperty(float scalar, ABI::Windows::Foundation::IPropertyValue** value) noexcept
    {
        if (!value)
        {
            return E_POINTER;
        }

        *value = nullptr;
        try
        {
            auto propertyValue = Windows::Foundation::PropertyValue::CreateSingle(scalar)
                .as<Windows::Foundation::IPropertyValue>();
            *value = reinterpret_cast<ABI::Windows::Foundation::IPropertyValue*>(
                detach_abi(propertyValue));
            return S_OK;
        }
        catch (...)
        {
            return to_hresult();
        }
    }

    // PropertyDescriptor::getDefaultValue has no context parameter, so each
    // property needs a distinct function address. We hand out one slot per
    // property from a fixed table; each slot stores that property's default and
    // is published atomically because the thunk can be invoked from a DWM thread.
    constexpr int kMaxPropertySlots = 64;

    struct PropertyDefaultSlot
    {
        std::atomic<float> value{ 0.0f };
    };

    PropertyDefaultSlot g_propertySlots[kMaxPropertySlots]{};
    std::atomic<int> g_nextPropertySlot{ 0 };

    template <int Slot>
    HRESULT PropertyDefaultThunk(ABI::Windows::Foundation::IPropertyValue** value) noexcept
    {
        float const scalar = g_propertySlots[Slot].value.load(std::memory_order_acquire);
        return CreateScalarProperty(scalar, value);
    }

    // Build a static table of kMaxPropertySlots distinct thunk addresses. Each
    // template instantiation is a unique function, so the runtime can distinguish
    // properties purely by the function pointer stored in PropertyDescriptor.
    template <int... Is>
    void* GetDefaultThunkImpl(int slot, std::integer_sequence<int, Is...>)
    {
        using Fn = HRESULT (__cdecl*)(ABI::Windows::Foundation::IPropertyValue**);
        static Fn const table[] = { &PropertyDefaultThunk<Is>... };
        constexpr int tableSize = sizeof...(Is);
        if (slot < 0 || slot >= tableSize)
        {
            return nullptr;
        }

        return reinterpret_cast<void*>(table[slot]);
    }

    void* GetDefaultThunk(int slot)
    {
        return GetDefaultThunkImpl(slot, std::make_integer_sequence<int, kMaxPropertySlots>{});
    }

    int AllocatePropertySlot(float defaultValue)
    {
        int const slot = g_nextPropertySlot.fetch_add(1, std::memory_order_acq_rel);
        if (slot < 0 || slot >= kMaxPropertySlots)
        {
            return -1;
        }

        g_propertySlots[slot].value.store(defaultValue, std::memory_order_release);
        return slot;
    }

    using GetDefaultValueFn = HRESULT(__cdecl*)(ABI::Windows::Foundation::IPropertyValue**);
}

extern "C" CUSTOM_EFFECT_API HRESULT CustomEffect_CompileShader(
    char const* source, uint64_t sourceSize,
    char const* entryName, char const* profile,
    char* outError, uint32_t outErrorCap, uint32_t* outErrorLen)
{
    if (outErrorLen)
    {
        *outErrorLen = 0;
    }
    if (outError && outErrorCap > 0)
    {
        outError[0] = '\0';
    }

    if (!source || sourceSize == 0 || !entryName || !profile)
    {
        return E_INVALIDARG;
    }

    // pch.h pulls d3dcompiler.h; the project links d3dcompiler.lib.
    ID3DBlob* code = nullptr;
    ID3DBlob* errors = nullptr;
    HRESULT hr = D3DCompile(
        source,
        static_cast<SIZE_T>(sourceSize),
        "shader",
        nullptr,
        D3D_COMPILE_STANDARD_FILE_INCLUDE,
        entryName,
        profile,
        0,
        0,
        &code,
        &errors);

    if (errors)
    {
        char const* msg = static_cast<char const*>(errors->GetBufferPointer());
        SIZE_T len = errors->GetBufferSize(); // includes trailing NUL
        if (len > 0 && msg[len - 1] == '\0')
        {
            len -= 1;
        }
        if (outError && outErrorCap > 1)
        {
            SIZE_T copy = (len < static_cast<SIZE_T>(outErrorCap) - 1)
                ? len
                : static_cast<SIZE_T>(outErrorCap) - 1;
            std::memcpy(outError, msg, copy);
            outError[copy] = '\0';
            if (outErrorLen)
            {
                *outErrorLen = static_cast<uint32_t>(copy);
            }
        }
    }

    if (code)
    {
        code->Release();
    }
    if (errors)
    {
        errors->Release();
    }
    return hr;
}

extern "C" CUSTOM_EFFECT_API HRESULT CustomEffect_Create(
    CustomEffectDefinitionFlat const* definition,
    void** outEffectAbi)
{
    if (!definition || !outEffectAbi)
    {
        return E_POINTER;
    }

    *outEffectAbi = nullptr;

    try
    {
        auto const& flat = *definition;

        if (!flat.effectName || !flat.shaderSource || flat.shaderSourceSize == 0 ||
            !flat.shaderFunctionName || !flat.sources)
        {
            return E_INVALIDARG;
        }

        if (flat.flattenSourceBeforeCustomSampler && !flat.flattenShaderFunctionName)
        {
            // The flatten subgraph reads this name from a DWM thread; a null pointer
            // would crash later, so reject it up front.
            return E_INVALIDARG;
        }

        if (flat.constantBufferSize && !flat.constantBufferInitial)
        {
            return E_INVALIDARG;
        }

        // Permanent definition. Never freed; the runtime keeps the pointer.
        auto* def = new CustomEffectRuntime::CustomEffectDefinition{};
        def->id = flat.id;
        def->effectName = DupWide(flat.effectName);
        def->fragmentName = DupAnsi(flat.fragmentName);
        def->shaderSource = reinterpret_cast<char const*>(DupBytes(flat.shaderSource, static_cast<size_t>(flat.shaderSourceSize)));
        def->shaderSourceSize = static_cast<size_t>(flat.shaderSourceSize);
        def->shaderFunctionName = DupAnsi(flat.shaderFunctionName);
        def->flattenShaderFunctionName = DupAnsi(flat.flattenShaderFunctionName);

        // Sources (only Backdrop is modeled by the runtime today).
        auto* sources = new CustomEffectRuntime::SourceDescriptor[flat.sourceCount];
        for (uint32_t index = 0; index < flat.sourceCount; ++index)
        {
            auto const& src = flat.sources[index];
            sources[index].name = DupWide(src.name);
            sources[index].kind = CustomEffectRuntime::SourceKind::Backdrop;
            sources[index].requiresSamplerData = src.wantsSamplerData != 0;
            sources[index].requiresSamplerDataExt = src.wantsSamplerDataExt != 0;
        }
        def->sources = sources;
        def->sourceCount = flat.sourceCount;

        // Scalar properties: synthesize the three aligned tables the runtime needs
        // (public descriptor, native metadata, constant-buffer mapping) from the
        // flat property list. propertiesStructSize must equal the constant-buffer
        // size or CreateCompiledResult rejects it with E_INVALIDARG.
        if (flat.propertyCount)
        {
            auto* properties = new CustomEffectRuntime::PropertyDescriptor[flat.propertyCount];
            auto* metadata = new CustomEffectRuntime::NativePropertyMetadata[flat.propertyCount];
            auto* mappings = new CustomEffectRuntime::ConstantBufferPropertyMapping[flat.propertyCount];

            for (uint32_t index = 0; index < flat.propertyCount; ++index)
            {
                auto const& prop = flat.properties[index];
                if (!prop.publicName || !prop.nativeName)
                {
                    return E_INVALIDARG;
                }

                int const slot = AllocatePropertySlot(prop.defaultValue);
                if (slot < 0)
                {
                    // Too many animated properties across the whole process.
                    return HRESULT_FROM_WIN32(ERROR_NOT_ENOUGH_MEMORY);
                }

                properties[index].publicName = DupWide(prop.publicName);
                properties[index].index = index;
                properties[index].mapping = ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;
                properties[index].getDefaultValue = reinterpret_cast<GetDefaultValueFn>(GetDefaultThunk(slot));

                metadata[index].shaderName = DupAnsi(prop.nativeName);
                metadata[index].propertyOffset = prop.cbufferOffset;
                metadata[index].expressionType = 18; // scalar
                metadata[index].propertyType = 8;     // Single
                metadata[index].valueCount = 1;
                metadata[index].validator = nullptr;

                mappings[index].propertyIndex = index;
                mappings[index].constantBufferOffset = prop.cbufferOffset;
            }

            def->properties = properties;
            def->propertyCount = flat.propertyCount;
            def->nativePropertyMetadata = metadata;
            def->nativePropertyMetadataCount = flat.propertyCount;
            def->propertiesStructSize = flat.constantBufferSize;
            def->constantBufferProperties = mappings;
            def->constantBufferPropertyCount = flat.propertyCount;
        }
        else
        {
            def->properties = nullptr;
            def->propertyCount = 0;
            def->nativePropertyMetadata = nullptr;
            def->nativePropertyMetadataCount = 0;
            def->propertiesStructSize = flat.constantBufferSize;
            def->constantBufferProperties = nullptr;
            def->constantBufferPropertyCount = 0;
        }

        // Shader-linking arguments (compact dwmcorei linker enum, copied verbatim).
        if (flat.shaderArgumentCount)
        {
            if (!flat.shaderArguments)
            {
                return E_INVALIDARG;
            }

            auto* args = new uint16_t[static_cast<size_t>(flat.shaderArgumentCount)];
            std::memcpy(args, flat.shaderArguments, sizeof(uint16_t) * static_cast<size_t>(flat.shaderArgumentCount));
            def->shaderArguments = args;
            def->shaderArgumentCount = flat.shaderArgumentCount;
        }
        else
        {
            def->shaderArguments = nullptr;
            def->shaderArgumentCount = 0;
        }

        def->linkingArgType = flat.linkingArgType;
        def->hasCustomSamplers = flat.hasCustomSamplers != 0;

        def->constantBufferSize = flat.constantBufferSize;
        def->constantBufferInitialValue = DupBytes(flat.constantBufferInitial, flat.constantBufferSize);

        def->flattenSourceBeforeCustomSampler = flat.flattenSourceBeforeCustomSampler != 0;
        def->keepAsFragmentOutput = flat.keepAsFragmentOutput != 0;

        // CreateEffect registers the definition and installs the process-wide hook
        // (idempotently), then returns a standard IGraphicsEffect. detach_abi hands
        // the single ref to the caller; the projected handle is left detached.
        auto effect = CustomEffectRuntime::CreateEffect(*def);
        *outEffectAbi = detach_abi(effect);
        return S_OK;
    }
    catch (...)
    {
        return to_hresult();
    }
}
