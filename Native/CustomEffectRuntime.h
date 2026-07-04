#pragma once

#include <winrt/Windows.Graphics.Effects.h>

#ifdef CUSTOM_EFFECT_RUNTIME_EXPORTS
#define CUSTOM_EFFECT_RUNTIME_API __declspec(dllexport)
#else
#define CUSTOM_EFFECT_RUNTIME_API __declspec(dllimport)
#endif

namespace CustomEffectRuntime
{
    enum class SourceKind
    {
        Backdrop,
    };

    struct SourceDescriptor
    {
        wchar_t const* name;
        SourceKind kind;
        bool requiresSamplerData;
        bool requiresSamplerDataExt;
    };

    struct PropertyDescriptor
    {
        wchar_t const* publicName;
        uint32_t index;
        ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING mapping;
        HRESULT (*getDefaultValue)(ABI::Windows::Foundation::IPropertyValue** value);
    };

    struct NativePropertyMetadata
    {
        char const* shaderName;
        uint32_t propertyOffset;
        uint32_t expressionType;
        uint32_t propertyType;
        uint32_t valueCount;
        void* validator;
    };

    struct ConstantBufferPropertyMapping
    {
        uint32_t propertyIndex;
        uint32_t constantBufferOffset;
    };

    struct CustomEffectDefinition
    {
        GUID id;
        wchar_t const* effectName;
        char const* fragmentName;

        char const* shaderSource;
        size_t shaderSourceSize;
        char const* shaderFunctionName;

        SourceDescriptor const* sources;
        uint32_t sourceCount;

        PropertyDescriptor const* properties;
        uint32_t propertyCount;
        void const* nativePropertyMetadata;
        uint32_t nativePropertyMetadataCount;
        uint32_t propertiesStructSize;

        ConstantBufferPropertyMapping const* constantBufferProperties;
        uint32_t constantBufferPropertyCount;

        uint16_t const* shaderArguments;
        uint64_t shaderArgumentCount;
        uint16_t linkingArgType;
        bool hasCustomSamplers;

        uint32_t constantBufferSize;
        void const* constantBufferInitialValue;

        bool flattenSourceBeforeCustomSampler;
        char const* flattenShaderFunctionName;
        bool keepAsFragmentOutput;    // true=keep main subgraph as fragment output (default);
                                      // false=allow downstream non-flatten consumers
    };

    CUSTOM_EFFECT_RUNTIME_API void RegisterEffect(CustomEffectDefinition const& definition);

    CUSTOM_EFFECT_RUNTIME_API winrt::Windows::Graphics::Effects::IGraphicsEffect CreateEffect(
        CustomEffectDefinition const& definition);
}
