#include "pch.h"
#include "CustomEffectRuntime.h"

using namespace winrt;
using namespace Microsoft::UI::Composition;
using namespace Windows::Graphics::Effects;

namespace
{
    // This file is a deliberately narrow compatibility layer for one WinAppSDK /
    // Windows composition build. The public WinRT object returned by CreateEffect()
    // looks like a normal IGraphicsEffect, but wuceffectsi/dwmcorei only accept a
    // fixed internal EffectType/ICompiledEffect ABI. The runtime below supplies the
    // missing private EffectType for our GUIDs, intercepts CompileEffectDescription,
    // and returns an ICompiledEffect-shaped object whose vectors and vtables match
    // the native layout observed in reverse engineering.
    //
    // High-level flow:
    //   1. RuntimeGraphicsEffect exposes a private effect GUID to WinUI.
    //   2. EffectType::FromGuid is patched so wuceffectsi accepts that GUID.
    //   3. CompileEffectDescription is patched in dcompi/dwmcorei import tables.
    //   4. When the flattened graph contains our EffectType, the detour returns a
    //      synthetic CompiledResult instead of asking wuceffectsi to generate code.
    //   5. DWM consumes CompiledResult through the ICompiledEffect vtable below.
    //
    // The RVAs are not symbolic API contracts. They must be treated as build-specific
    // offsets and guarded by byte-pattern checks where code patching is involved.
    constexpr uintptr_t kEffectTypeFromGuidRva = 0x17c48;
    constexpr uintptr_t kEffectTypeTableRva = 0x62150;
    constexpr uintptr_t kEffectTypeGetBoundsRva = 0x1e040;
    constexpr uintptr_t kEffectTypeCalcInputBoundsRva = 0x1d700;
    constexpr uintptr_t kDirectPropertyUpdaterFunctionVtableRva = 0x451e0;
    constexpr size_t kEffectTypeCount = 0x1f;
    // Reverse engineering shows EffectType virtual calls stop at slot 21
    // (+0xa8, GetEffectOpacityRelation) in this WinAppSDK build. Slot 22+
    // is not part of the callable ABI we need to model for private GUIDs.
    constexpr size_t kEffectTypeVtableSlotCount = 22;
    constexpr size_t kFromGuidPatchSize = 15;
    constexpr uint32_t kCompiledEffectSubgraphOutputFlag = 0x8;

    struct RuntimeEffectEntry;

    // Private EffectType objects are not COM objects. wuceffectsi treats the first
    // pointer as a native C++ vtable and calls fixed slots directly. The entry back
    // pointer gives every slot access to the declarative CustomEffectDefinition.
    struct RuntimeEffectType
    {
        void** vtable;
        RuntimeEffectEntry* entry;
    };

    // One registered effect definition owns one native-shaped EffectType instance.
    // The shader blob is compiled lazily because the effect may be registered before
    // any CompositionEffectFactory asks DWM to materialize the graph.
    struct RuntimeEffectEntry
    {
        explicit RuntimeEffectEntry(CustomEffectRuntime::CustomEffectDefinition const& value) :
            definition(&value)
        {
            effectType.vtable = effectTypeVtable;
            effectType.entry = this;
        }

        CustomEffectRuntime::CustomEffectDefinition const* definition{};
        winrt::com_ptr<ID3DBlob> shaderBlob;
        std::once_flag shaderOnce;
        void* effectTypeVtable[kEffectTypeVtableSlotCount]{};
        RuntimeEffectType effectType{};
        RuntimeEffectEntry* next{};
    };

    // ABI returned by ICompiledEffect::GetSubgraphShaderLinkingBody. dwmcorei copies
    // this POD by value, then feeds bytecodeData/functionName/argData into its shader
    // linker. The static_assert pins the reverse-engineered struct size so accidental
    // field changes fail at compile time instead of corrupting DWM reads.
    struct ShaderLinkingBody
    {
        uint64_t argCount;
        void const* argData;
        uint64_t bytecodeSize;
        void const* bytecodeData;
        char const* functionName;
        uint32_t constantBufferSize;
        uint16_t linkingArgType;
        uint8_t hasCustomSamplers;
        uint8_t padding;
    };

    static_assert(sizeof(ShaderLinkingBody) == 48);

    // Mirrors CompiledEffectSubgraph::InputBindings: an input either maps to a named
    // brush input (isSubgraphOutput=false) or to a previously emitted subgraph output
    // (isSubgraphOutput=true). DWM uses this to build fragment inputs in
    // CBrushRenderingGraphBuilder::AddEffectBrush.
    struct InputBinding
    {
        uint32_t inputIndex;
        bool isSubgraphOutput;
        uint8_t padding[3];
    };

    static_assert(sizeof(InputBinding) == 8);

    // Four bytes copied from native EffectGenerator::SurfaceData. DWM currently
    // observes byte 2 for samplerData and byte 3 for samplerDataExt requirements.
    // The other bytes are kept so the vector stride matches native compiled graphs.
    struct SurfaceData
    {
        uint8_t data[4];
    };

    static_assert(sizeof(SurfaceData) == 4);
    static_assert(sizeof(CustomEffectRuntime::NativePropertyMetadata) == 32);
    static_assert(offsetof(CustomEffectRuntime::NativePropertyMetadata, propertyOffset) == 8);
    static_assert(offsetof(CustomEffectRuntime::NativePropertyMetadata, propertyType) == 16);
    static_assert(offsetof(CustomEffectRuntime::NativePropertyMetadata, valueCount) == 20);

    // Native property animation does not call our WinRT IGraphicsEffect again after
    // factory creation. wuceffectsi stores std::function-like updater callables in
    // the compiled subgraph; EffectInstance invokes them when CompositionPropertySet
    // values change. These structures model the inline-storage callable shape used
    // by the built-in DirectPropertyUpdater path.
    struct NativeFunctionStorage
    {
        uint8_t inlineStorage[56];
        void* callable;
    };

    static_assert(sizeof(NativeFunctionStorage) == 64);

    struct NativePropertyUpdaterCallable
    {
        void** vtable;
        CustomEffectRuntime::NativePropertyMetadata metadata;
    };

    static_assert(sizeof(NativePropertyUpdaterCallable) == 40);

    struct ConstantBufferUpdater
    {
        uint32_t nodeIndex;
        uint32_t constantBufferOffset;
        NativeFunctionStorage update;
    };

    static_assert(sizeof(ConstantBufferUpdater) == 72);
    static_assert(offsetof(ConstantBufferUpdater, update) == 8);
    static_assert(offsetof(ConstantBufferUpdater, update.callable) == 64);

    // Native CompiledEffectSubgraph layout. DWM indexes this array directly through
    // ICompiledEffect methods, but wuceffectsi later also reads vector ranges for
    // constant buffer creation. The offsets therefore matter as much as the getters.
    struct CompiledSubgraph
    {
        uint32_t flags;
        uint16_t linkingArgType;
        uint16_t padding0;
        void* shaderArgumentBegin;
        void* shaderArgumentEnd;
        void* shaderArgumentCapacity;
        void* shaderSource;
        void* constantBufferUpdaterBegin;
        void* constantBufferUpdaterEnd;
        void* constantBufferUpdaterCapacity;
        void* constantBufferInitialBegin;
        void* constantBufferInitialEnd;
        void* constantBufferInitialCapacity;
        void* surfaceDataBegin;
        void* surfaceDataEnd;
        void* surfaceDataCapacity;
        void* inputBindingBegin;
        void* inputBindingEnd;
        void* inputBindingCapacity;
    };

    static_assert(sizeof(CompiledSubgraph) == 136);
    static_assert(offsetof(CompiledSubgraph, constantBufferUpdaterBegin) == 40);
    static_assert(offsetof(CompiledSubgraph, constantBufferInitialBegin) == 64);
    static_assert(offsetof(CompiledSubgraph, inputBindingBegin) == 112);

    // Synthetic ICompiledEffect object returned by DetourCompileEffectDescription.
    // It is COM-like enough for AddRef/Release and has the native vector fields at
    // the offsets EffectInstance expects. Do not wrap this in another object: DWM
    // already stores this pointer inside its own compilation task result wrapper.
    struct CompiledResult
    {
        void** vtable;
        volatile long refCount;
        uint32_t padding;
        CompiledSubgraph* subgraphBegin;
        CompiledSubgraph* subgraphEnd;
        CompiledSubgraph* subgraphCapacity;

        RuntimeEffectEntry* entry;
    };

    static_assert(offsetof(CompiledResult, subgraphBegin) == 16);
    static_assert(offsetof(CompiledResult, entry) == 40);

    // Import patch records are name-aware because delay import thunks cannot always
    // be matched by function address before the delay loader resolves them.
    struct ImportPatch
    {
        char const* name;
        void* original;
        void* replacement;
    };

    RuntimeEffectEntry* g_effects{};
    std::mutex g_registryMutex;
    HMODULE g_wuceffectsiModule{};
    std::once_flag g_hookOnce;

    using CompileEffectDescriptionFn = HRESULT(__fastcall*)(void*, void**);
    CompileEffectDescriptionFn g_originalCompileEffectDescription{};

    bool SameGuid(GUID const& left, GUID const& right)
    {
        // Avoid relying on operator== or platform helpers here so the GUID comparison
        // stays valid for both WinRT GUID values and the raw GUID pointers returned
        // from native EffectType vtable slots.
        return left.Data1 == right.Data1 &&
            left.Data2 == right.Data2 &&
            left.Data3 == right.Data3 &&
            memcmp(left.Data4, right.Data4, sizeof(left.Data4)) == 0;
    }

    RuntimeEffectEntry* FindEntryByGuidLocked(GUID const& id)
    {
        // The registry is tiny and only mutated during effect construction, so a
        // linked list is enough. The caller holds g_registryMutex; keeping locking
        // outside avoids re-entering this helper from EffectType slots.
        for (auto* entry = g_effects; entry; entry = entry->next)
        {
            if (SameGuid(entry->definition->id, id))
            {
                return entry;
            }
        }

        return nullptr;
    }

    RuntimeEffectEntry* FindEntryByEffectTypeLocked(void* effectType)
    {
        // FlattenedEffectGraph stores EffectNode::m_effectType as a raw pointer to
        // the native EffectType. Comparing pointer identity is the most reliable way
        // to decide whether a graph node belongs to this runtime.
        for (auto* entry = g_effects; entry; entry = entry->next)
        {
            if (effectType == &entry->effectType)
            {
                return entry;
            }
        }

        return nullptr;
    }

    void CompileShaderLibrary(
        char const* source,
        size_t sourceSize,
        ID3DBlob** shaderBlob)
    {
        // wuceffectsi!EffectGenerator::BuildCompiledEffectSubgraph does not compile
        // generated effects as lib_5_0. This WinUI3 build passes
        // "lib_4_0_level_9_3_ps_only" and flags 0x8800
        // (STRICTNESS | OPTIMIZATION_LEVEL3) to D3DCompile, then dwmcorei links the
        // library into ps_4_0/ps_4_0_level_9_x. Matching that profile is intentional:
        // D3DLoadModule/CreateInstance accepts a lib_5_0 blob, but the final DWM
        // ID3D11Linker::Link path rejects the mixed-profile graph with E_FAIL.
        UINT flags = D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_OPTIMIZATION_LEVEL3;

        winrt::com_ptr<ID3DBlob> errors;
        check_hresult(D3DCompile(
            source,
            sourceSize,
            nullptr,
            nullptr,
            nullptr,
            // dwmcorei!LoadShaderBody consumes this blob with D3DLoadModule and then
            // CreateInstance(functionName). A standalone ps_4_0 blob compiles locally
            // but is not a loadable shader-linking module, so this runtime keeps the
            // code-only path aligned with wuceffectsi's generated-effect profile.
            nullptr,
            "lib_4_0_level_9_3_ps_only",
            flags,
            0,
            shaderBlob,
            errors.put()));
    }

    void EnsureShader(RuntimeEffectEntry* entry)
    {
        // Shader compilation is intentionally tied to the registered effect entry,
        // not to a brush instance. CompositionEffectFactory creation can occur on a
        // worker path and multiple brushes may share the same CustomEffectDefinition,
        // so call_once prevents duplicate D3DCompile work and keeps the bytecode
        // pointer stable for every compiled graph result.
        std::call_once(entry->shaderOnce, [entry]
        {
            auto const& definition = *entry->definition;
            CompileShaderLibrary(
                definition.shaderSource,
                definition.shaderSourceSize,
                entry->shaderBlob.put());
        });
    }

    char const* __fastcall EffectType_GetShaderFragmentName(RuntimeEffectType* self)
    {
        // Used by wuceffectsi for diagnostics/hash names and by generated shader
        // naming. It is not the HLSL entrypoint; GetSubgraphShaderLinkingBody
        // provides that later through ShaderLinkingBody::functionName.
        return self->entry->definition->fragmentName;
    }

    GUID const* __fastcall EffectType_GetGuid(RuntimeEffectType* self)
    {
        // EffectType::FromGuid callers expect this slot to return stable storage.
        // Returning the address inside CustomEffectDefinition avoids temporary GUID
        // lifetime problems while preserving per-effect private GUID identity.
        return &self->entry->definition->id;
    }

    bool __fastcall EffectType_IsValidInputCount(RuntimeEffectType* self, uint32_t sourceCount)
    {
        // Traverser rejects the graph before CompileEffectDescription if the source
        // count does not match the EffectType metadata. Keep this strict so the
        // synthetic compiled graph shape cannot disagree with the WinRT wrapper.
        return sourceCount == self->entry->definition->sourceCount;
    }

    bool __fastcall EffectType_IsValidInputType(RuntimeEffectType*, uint32_t inputType)
    {
        // Native EffectType uses small input-type enums. Zero is the "null input"
        // case; all non-null source forms accepted by IGraphicsEffectD2D1Interop are
        // allowed here and resolved later by VisitEffectInputs.
        return inputType != 0;
    }

    uint32_t __fastcall EffectType_GetPropertiesStructSize(RuntimeEffectType* self)
    {
        // Traverser allocates/copies the default property blob using this size before
        // native metadata is consulted. It must match the struct layout used by the
        // effect-specific default-value provider and constant-buffer mappings.
        return self->entry->definition->propertiesStructSize;
    }

    uint32_t __fastcall EffectType_GetEffectSamplingBehavior(RuntimeEffectType*)
    {
        // Neutral/default sampling behavior. Custom sampler details are not exposed
        // from this slot; DWM later asks ICompiledEffect for samplerData flags and
        // shader-linking arguments per subgraph input.
        return 0;
    }

    bool __fastcall EffectType_ReturnFalse(RuntimeEffectType*)
    {
        // Several EffectType slots are boolean feature probes. The custom runtime
        // opts out of those native special cases unless a slot is modeled explicitly,
        // because an accidental true changes graph simplification or bounds behavior.
        return false;
    }

    bool __fastcall EffectType_RequiresSourceFlattening(RuntimeEffectType* self)
    {
        // wuceffectsi!Traverser uses EffectType slot 5 as the native source-flattening
        // gate: when it is true, named inputs are first wrapped in
        // CSingleInputCompositeEffect and become their own EffectSubgraph. Returning true
        // here only for effects whose compiled result also exposes a flatten subgraph
        // keeps FlattenedEffectGraph and ICompiledEffect subgraph counts aligned.
        return self->entry->definition->flattenSourceBeforeCustomSampler;
    }

    bool __fastcall EffectType_ReturnTrue(RuntimeEffectType*)
    {
        // Slot 12 is observed as a positive capability bit for generated shader
        // effects in this build. Keeping it true matches the native generated-effect
        // path while the other feature-probe slots remain false.
        return true;
    }

    bool __fastcall EffectType_IsInputTransform(RuntimeEffectType*, uint32_t* mode)
    {
        // Input-transform effects such as AffineTransform2D change bounds and source
        // coordinate propagation. Custom glass/blur effects here are ordinary render
        // effects, so report false and leave transform mode at zero.
        if (mode)
        {
            *mode = 0;
        }

        return false;
    }

    bool __fastcall EffectType_IsIntersectionCombinator(RuntimeEffectType*, void const*)
    {
        // Intersection/combinator effects alter how source bounds are merged. Custom
        // sampler effects here consume one already-resolved source surface, so they
        // must not participate in that built-in combinator path.
        return false;
    }

    bool __fastcall EffectType_IsNoOp(RuntimeEffectType*, uint32_t, void const*)
    {
        // Never let wuceffectsi elide a private effect as a no-op. Even a passthrough
        // shader is useful as a probe because it proves the synthetic compile result
        // and shader-linking path are the code that actually ran.
        return false;
    }

    uint32_t __fastcall EffectType_GetEffectOpacityRelation(RuntimeEffectType*, void const*)
    {
        // Report the neutral opacity relation. DWM can still blend the final brush
        // normally, but this avoids claiming built-in opacity preservation rules that
        // may not hold for arbitrary custom shader code.
        return 0;
    }

    void __fastcall EffectType_GetPropertiesMetadata(
        RuntimeEffectType* self,
        uint32_t* count,
        void const** metadata)
    {
        // EffectGenerator and EffectInstance both depend on native property metadata:
        // property name, byte offset, scalar/vector type, and value count. The public
        // WinRT property mapping alone is not enough for animated CompositionBrush
        // properties because DWM needs constant-buffer updater descriptors.
        auto const& definition = *self->entry->definition;
        if (count)
        {
            *count = definition.nativePropertyMetadataCount;
        }

        if (metadata)
        {
            *metadata = definition.nativePropertyMetadata;
        }
    }

    void __fastcall EffectType_Validate(RuntimeEffectType*, void const*)
    {
        // Built-in effects validate property ranges here. This runtime validates
        // structural metadata while building ConstantBufferUpdater records; per-effect
        // range validation can be added through NativePropertyMetadata::validator
        // once the native validator ABI is modeled.
    }

    void __fastcall EffectType_GenerateCode(RuntimeEffectType*, void const*, void*, char const*)
    {
        // CompileEffectDescription detects runtime EffectType objects and returns a
        // CompiledEffect-shaped object directly. This no-op is a defensive slot so an
        // accidental fallback does not run a built-in GenerateCode path and hide whether
        // our private shader path ran.
    }

    void InitializeEffectType(RuntimeEffectEntry* entry, HMODULE wuceffectsi)
    {
        auto const base = reinterpret_cast<uint8_t*>(wuceffectsi);
        auto* vtable = entry->effectTypeVtable;

        std::fill(vtable, vtable + kEffectTypeVtableSlotCount, nullptr);

        // EffectType slots are not COM methods, and several folded tiny functions have
        // different meanings depending on their slot. These 22 entries cover every
        // observed EffectType virtual call from traversal, flattening, hashing, opacity
        // propagation, and generator code in this wuceffectsi build. The neutral native
        // bounds helpers are reused because their ABI includes struct-return details.
        vtable[0] = reinterpret_cast<void*>(EffectType_GetShaderFragmentName);
        vtable[1] = reinterpret_cast<void*>(EffectType_GetGuid);
        vtable[2] = reinterpret_cast<void*>(EffectType_GetEffectSamplingBehavior);
        vtable[3] = reinterpret_cast<void*>(EffectType_IsValidInputCount);
        vtable[4] = reinterpret_cast<void*>(EffectType_IsValidInputType);
        vtable[5] = reinterpret_cast<void*>(EffectType_RequiresSourceFlattening);
        vtable[6] = reinterpret_cast<void*>(EffectType_IsInputTransform);
        vtable[7] = reinterpret_cast<void*>(EffectType_ReturnFalse);
        vtable[8] = reinterpret_cast<void*>(EffectType_ReturnFalse);
        vtable[9] = reinterpret_cast<void*>(EffectType_ReturnFalse);
        vtable[10] = reinterpret_cast<void*>(EffectType_ReturnFalse);
        vtable[11] = reinterpret_cast<void*>(EffectType_ReturnFalse);
        vtable[12] = reinterpret_cast<void*>(EffectType_ReturnTrue);
        vtable[13] = reinterpret_cast<void*>(EffectType_IsIntersectionCombinator);
        vtable[14] = reinterpret_cast<void*>(EffectType_IsNoOp);
        vtable[15] = base + kEffectTypeGetBoundsRva;
        vtable[16] = base + kEffectTypeCalcInputBoundsRva;
        vtable[17] = reinterpret_cast<void*>(EffectType_GetPropertiesStructSize);
        vtable[18] = reinterpret_cast<void*>(EffectType_GetPropertiesMetadata);
        vtable[19] = reinterpret_cast<void*>(EffectType_Validate);
        vtable[20] = reinterpret_cast<void*>(EffectType_GenerateCode);
        vtable[21] = reinterpret_cast<void*>(EffectType_GetEffectOpacityRelation);
    }

    void InitializeAllEffectTypes(HMODULE wuceffectsi)
    {
        // RegisterEffect may run before wuceffectsi.dll is loaded. Once the hook is
        // installed, every previously registered entry must receive a valid native
        // vtable before EffectType::FromGuid can return it.
        std::lock_guard<std::mutex> guard(g_registryMutex);
        for (auto* entry = g_effects; entry; entry = entry->next)
        {
            InitializeEffectType(entry, wuceffectsi);
        }
    }

    void* __fastcall DetourEffectTypeFromGuid(GUID const* guid)
    {
        // First give private runtime GUIDs a chance to resolve to synthetic
        // EffectType objects. This is what lets CreateEffectFactory accept a real
        // custom GUID instead of pretending to be ColorMatrix/GaussianBlur/etc.
        if (guid)
        {
            std::lock_guard<std::mutex> guard(g_registryMutex);
            if (auto* entry = FindEntryByGuidLocked(*guid))
            {
                return &entry->effectType;
            }
        }

        // For all built-in GUIDs, reproduce the native lookup by scanning the
        // wuceffectsi EffectType table and calling slot 1 (GetGuid). This keeps the
        // patch transparent for every effect this runtime does not own.
        auto const module = GetModuleHandleW(L"wuceffectsi.dll");
        if (!module || !guid)
        {
            return nullptr;
        }

        auto const base = reinterpret_cast<uint8_t*>(module);
        auto* table = reinterpret_cast<void**>(base + kEffectTypeTableRva);
        for (size_t index = 0; index < kEffectTypeCount; ++index)
        {
            auto* effectType = table[index];
            if (!effectType)
            {
                continue;
            }

            auto* vtable = *reinterpret_cast<void***>(effectType);
            auto const getGuid = reinterpret_cast<GUID const*(__fastcall*)(void*)>(vtable[1]);
            auto const knownGuid = getGuid(effectType);
            if (knownGuid && SameGuid(*knownGuid, *guid))
            {
                return effectType;
            }
        }

        return nullptr;
    }

    void PatchEffectTypeFromGuid(HMODULE wuceffectsi)
    {
        // EffectType::FromGuid is called before CompileEffectDescription. If it
        // returns null, traversal fails with "Unsupported effect type" and our
        // compile detour never runs. Patching this function is therefore the first
        // gate that makes private GUIDs possible.
        auto* target = reinterpret_cast<uint8_t*>(wuceffectsi) + kEffectTypeFromGuidRva;
        uint8_t const expected[kFromGuidPatchSize] = {
            0x48, 0x89, 0x5c, 0x24, 0x08,
            0x48, 0x89, 0x74, 0x24, 0x10,
            0x48, 0x89, 0x7c, 0x24, 0x18,
        };

        if (memcmp(target, expected, sizeof(expected)) != 0)
        {
            // Fail closed on unknown builds. Jump-patching the wrong bytes would
            // corrupt wuceffectsi globally and produce misleading DWM crashes.
            check_hresult(HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH));
        }

        uint8_t patch[kFromGuidPatchSize] = {
            0x48, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0,
            0xff, 0xe0,
            0x90, 0x90, 0x90,
        };
        *reinterpret_cast<void**>(patch + 2) = reinterpret_cast<void*>(DetourEffectTypeFromGuid);

        DWORD oldProtect{};
        check_bool(VirtualProtect(target, sizeof(patch), PAGE_EXECUTE_READWRITE, &oldProtect));
        memcpy(target, patch, sizeof(patch));
        FlushInstructionCache(GetCurrentProcess(), target, sizeof(patch));
        DWORD unused{};
        VirtualProtect(target, sizeof(patch), oldProtect, &unused);
    }

    ULONG AddRef(volatile long* refCount)
    {
        // Keep ref-count helpers tiny and ABI-neutral. The object is consumed across
        // native DWM/WUCEffectsI code paths, so interlocked operations are required
        // even though creation happens on the app side.
        return static_cast<ULONG>(InterlockedIncrement(refCount));
    }

    ULONG ReleaseRef(volatile long* refCount)
    {
        // Release may run on the DWM composition/effect worker path, not necessarily
        // on the UI thread that created the brush.
        return static_cast<ULONG>(InterlockedDecrement(refCount));
    }

    uint32_t PointerRangeByteSize(void const* begin, void const* end);
    bool UsesFlattenSourceSubgraph(CustomEffectRuntime::CustomEffectDefinition const& definition);
    uint32_t GetMainSubgraphIndex(CustomEffectRuntime::CustomEffectDefinition const& definition);

    ULONG __fastcall Wrapper_AddRef(CompiledResult* self)
    {
        // DWM treats ICompiledEffect as ref-counted even though this object is not a
        // C++/WinRT implements type. Keep the lifetime independent from the public
        // RuntimeGraphicsEffect object; factories can outlive the original wrapper.
        return AddRef(&self->refCount);
    }

    void DestroyCompiledResult(CompiledResult* self)
    {
        // Every vector field in CompiledSubgraph points to process-heap allocations
        // created by this file. Free each vector explicitly before freeing the outer
        // CompiledResult so native EffectInstance cannot observe dangling ranges.
        if (self->subgraphBegin)
        {
            for (auto* subgraph = self->subgraphBegin; subgraph != self->subgraphEnd; ++subgraph)
            {
                if (subgraph->inputBindingBegin)
                {
                    HeapFree(GetProcessHeap(), 0, subgraph->inputBindingBegin);
                }

                if (subgraph->shaderArgumentBegin)
                {
                    HeapFree(GetProcessHeap(), 0, subgraph->shaderArgumentBegin);
                }

                if (subgraph->constantBufferInitialBegin)
                {
                    HeapFree(GetProcessHeap(), 0, subgraph->constantBufferInitialBegin);
                }

                if (subgraph->constantBufferUpdaterBegin)
                {
                    HeapFree(GetProcessHeap(), 0, subgraph->constantBufferUpdaterBegin);
                }

                if (subgraph->surfaceDataBegin)
                {
                    HeapFree(GetProcessHeap(), 0, subgraph->surfaceDataBegin);
                }
            }

            HeapFree(GetProcessHeap(), 0, self->subgraphBegin);
        }

        HeapFree(GetProcessHeap(), 0, self);
    }

    ULONG __fastcall Wrapper_Release(CompiledResult* self)
    {
        // This is paired with Wrapper_AddRef rather than C++/WinRT lifetime support.
        // Native callers only know the first vtable pointer and the reference count
        // field; they never see a winrt::implements control block.
        auto const ref = ReleaseRef(&self->refCount);
        if (ref == 0)
        {
            DestroyCompiledResult(self);
        }

        return ref;
    }

    uint32_t __fastcall Wrapper_GetSubgraphCount(CompiledResult* self)
    {
        // DWM uses this count to size its SubgraphOutput array. wuceffectsi later
        // iterates the same subgraph vector for constant-buffer creation, so this
        // must match the actual [subgraphBegin, subgraphEnd) range.
        auto* subgraphBegin = self->subgraphBegin;
        auto* subgraphEnd = self->subgraphEnd;
        if (!subgraphBegin || !subgraphEnd || subgraphEnd < subgraphBegin)
        {
            return 0;
        }

        return static_cast<uint32_t>(subgraphEnd - subgraphBegin);
    }

    ShaderLinkingBody* __fastcall Wrapper_GetSubgraphShaderLinkingBody(
        CompiledResult* self,
        ShaderLinkingBody* body,
        uint32_t subgraphIndex)
    {
        // This is the most important ICompiledEffect method. It supplies DWM with a
        // loadable shader library, the exported HLSL function name, and the packed
        // shader-linking argument list (color input, uv, samplerData, samplerDataExt,
        // custom sampler result, etc.).
        EnsureShader(self->entry);

        auto const& definition = *self->entry->definition;
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            check_hresult(E_INVALIDARG);
        }

        auto* subgraph = self->subgraphBegin + subgraphIndex;
        auto const isMainSubgraph = subgraphIndex == GetMainSubgraphIndex(definition);
        auto const argCount = subgraph && subgraph->shaderArgumentBegin && subgraph->shaderArgumentEnd
            ? static_cast<uint64_t>(
                (static_cast<uint8_t*>(subgraph->shaderArgumentEnd) -
                    static_cast<uint8_t*>(subgraph->shaderArgumentBegin)) /
                sizeof(uint16_t))
            : definition.shaderArgumentCount;

        body->argCount = argCount;
        body->argData = subgraph && subgraph->shaderArgumentBegin
            ? subgraph->shaderArgumentBegin
            : definition.shaderArguments;
        body->bytecodeSize = self->entry->shaderBlob->GetBufferSize();
        body->bytecodeData = self->entry->shaderBlob->GetBufferPointer();
        body->functionName = isMainSubgraph ? definition.shaderFunctionName : definition.flattenShaderFunctionName;
        body->constantBufferSize = PointerRangeByteSize(
            subgraph->constantBufferInitialBegin,
            subgraph->constantBufferInitialEnd);
        body->linkingArgType = subgraph->linkingArgType;
        // wuceffectsi!CompiledEffect::GetSubgraphShaderLinkingBody writes this byte as
        // 1 for generated shader-linking subgraphs, including ordinary CompositeEffect
        // passthrough code. The flatten stage still uses color-input arguments; this
        // byte is kept native-shaped so DWM treats it as generated linker code.
        body->hasCustomSamplers = isMainSubgraph
            ? (definition.hasCustomSamplers ? 1 : 0)
            : 1;
        body->padding = 0;
        return body;
    }

    uint32_t __fastcall Wrapper_GetSubgraphInputCount(CompiledResult* self, uint32_t subgraphIndex)
    {
        // Input count is per subgraph, not per effect. A flatten/custom-sampler graph
        // has different input meanings at each subgraph: source brush, previous
        // intermediate, or final wrapper input.
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            return 0;
        }

        auto const& subgraph = self->subgraphBegin[subgraphIndex];
        auto* inputBegin = static_cast<InputBinding*>(subgraph.inputBindingBegin);
        auto* inputEnd = static_cast<InputBinding*>(subgraph.inputBindingEnd);
        if (!inputBegin || !inputEnd || inputEnd < inputBegin)
        {
            return 0;
        }

        return static_cast<uint32_t>(inputEnd - inputBegin);
    }

    uint32_t __fastcall Wrapper_GetSubgraphFlags(CompiledResult* self, uint32_t subgraphIndex)
    {
        // Flag 0x8 is observed by CBrushRenderingGraphBuilder as "keep this subgraph
        // as a fragment output". For the LiquidGlass shape this prevents the custom
        // material from being rendered into the upstream blur's prescaled target.
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            return 0;
        }

        return self->subgraphBegin[subgraphIndex].flags;
    }

    uint32_t __fastcall Wrapper_GetInputMapping(
        CompiledResult* self,
        uint32_t subgraphIndex,
        uint32_t inputIndex,
        bool* isSubgraphOutput)
    {
        // DWM asks this for each subgraph input. If isSubgraphOutput is false, the
        // returned index selects a named brush source. If true, it selects a previous
        // SubgraphOutput entry. This is how the synthetic graph expresses edges
        // between flattened/intermediate/custom/final subgraphs.
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            check_hresult(E_INVALIDARG);
        }

        auto const& subgraph = self->subgraphBegin[subgraphIndex];
        auto* inputBegin = static_cast<InputBinding*>(subgraph.inputBindingBegin);
        auto* inputEnd = static_cast<InputBinding*>(subgraph.inputBindingEnd);
        if (!inputBegin || !inputEnd ||
            inputIndex >= static_cast<uint32_t>(inputEnd - inputBegin))
        {
            check_hresult(E_INVALIDARG);
        }

        auto const& binding = inputBegin[inputIndex];
        if (isSubgraphOutput)
        {
            *isSubgraphOutput = binding.isSubgraphOutput;
        }

        return binding.inputIndex;
    }

    bool __fastcall Wrapper_IsUVClampingRequired(
        CompiledResult* self,
        uint32_t subgraphIndex,
        uint32_t inputIndex,
        uint32_t* horizontalMode,
        uint32_t* verticalMode)
    {
        // The method name is misleading for this use case: DWM's shader argument
        // population also checks it to decide whether samplerDataN is available.
        // SurfaceData byte 2 therefore controls more than plain edge clamping.
        bool required = false;
        auto* subgraphBegin = self->subgraphBegin;
        auto* subgraphEnd = self->subgraphEnd;
        if (subgraphBegin && subgraphEnd &&
            subgraphIndex < static_cast<uint32_t>(subgraphEnd - subgraphBegin))
        {
            auto const& subgraph = subgraphBegin[subgraphIndex];
            auto* surfaceDataBegin = static_cast<SurfaceData*>(subgraph.surfaceDataBegin);
            auto* surfaceDataEnd = static_cast<SurfaceData*>(subgraph.surfaceDataEnd);
            required = surfaceDataBegin && surfaceDataEnd &&
                inputIndex < static_cast<uint32_t>(surfaceDataEnd - surfaceDataBegin) &&
                surfaceDataBegin[inputIndex].data[2] != 0;
        }

        if (horizontalMode)
        {
            *horizontalMode = required ? 1 : 0;
        }

        if (verticalMode)
        {
            *verticalMode = required ? 1 : 0;
        }

        return required;
    }

    bool __fastcall Wrapper_IsSamplerDataExtRequired(
        CompiledResult* self,
        uint32_t subgraphIndex,
        uint32_t inputIndex)
    {
        // samplerDataExt carries source/intermediate dimensions and texel-size data
        // for custom sampler bodies. The LiquidGlass shader uses it for refraction
        // offsets, while samplerData is used for the effective content rect.
        auto* subgraphBegin = self->subgraphBegin;
        auto* subgraphEnd = self->subgraphEnd;
        if (!subgraphBegin || !subgraphEnd || subgraphIndex >= static_cast<uint32_t>(subgraphEnd - subgraphBegin))
        {
            return false;
        }

        auto const& subgraph = subgraphBegin[subgraphIndex];
        auto* surfaceDataBegin = static_cast<SurfaceData*>(subgraph.surfaceDataBegin);
        auto* surfaceDataEnd = static_cast<SurfaceData*>(subgraph.surfaceDataEnd);
        if (!surfaceDataBegin || !surfaceDataEnd ||
            inputIndex >= static_cast<uint32_t>(surfaceDataEnd - surfaceDataBegin))
        {
            return false;
        }

        return surfaceDataBegin[inputIndex].data[3] != 0;
    }

    uint32_t __fastcall Wrapper_GetConstantBufferSize(CompiledResult* self, uint32_t subgraphIndex)
    {
        // EffectInstance allocates one constant buffer per flattened subgraph. Most
        // helper/flatten subgraphs intentionally return zero here; the main subgraph
        // exposes the effect definition's constant buffer range.
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            return 0;
        }

        auto const& subgraph = self->subgraphBegin[subgraphIndex];
        return PointerRangeByteSize(
            subgraph.constantBufferInitialBegin,
            subgraph.constantBufferInitialEnd);
    }

    void const* __fastcall Wrapper_GetConstantBufferInitialValue(CompiledResult* self, uint32_t subgraphIndex)
    {
        // Native code copies this initial blob before applying direct property
        // updates. It must remain valid for the lifetime of the CompiledResult.
        if (subgraphIndex >= Wrapper_GetSubgraphCount(self))
        {
            return nullptr;
        }

        auto const& subgraph = self->subgraphBegin[subgraphIndex];
        return subgraph.constantBufferInitialBegin;
    }

    void* __fastcall Wrapper_ScalarDeletingDestructor(CompiledResult* self, uint32_t flags)
    {
        // MSVC scalar-deleting destructor slot. Some native cleanup paths call this
        // instead of Release when they believe they own the compiled effect directly;
        // honor the delete flag but otherwise leave ownership unchanged.
        if ((flags & 1) != 0)
        {
            DestroyCompiledResult(self);
        }

        return self;
    }

    void __fastcall Wrapper_FinalRelease(CompiledResult*)
    {
        // Native ICompiledEffect has a final-release-style slot after the deleting
        // destructor. The synthetic object has no secondary resources outside the
        // explicit vector ranges freed in DestroyCompiledResult, so this is a no-op.
    }

    // CompileEffectDescription must return the CompiledEffect-shaped object directly.
    // dwmcorei!Compile_WorkerThread already wraps that returned pointer in its own task
    // result object, and GetCompiledEffectNoRef returns the pointer stored in that DWM
    // wrapper at +0x20. Returning another app-defined outer wrapper here makes
    // wuceffectsi!EffectInstance read that wrapper's +0x10/+0x18 as an empty subgraph
    // vector, so this intentionally diverges from the earlier v3 note's extra-wrapper
    // wording for this WinUI3 build.
    void* g_wrapperVtable[] = {
        reinterpret_cast<void*>(Wrapper_AddRef),
        reinterpret_cast<void*>(Wrapper_Release),
        reinterpret_cast<void*>(Wrapper_GetSubgraphCount),
        reinterpret_cast<void*>(Wrapper_GetSubgraphShaderLinkingBody),
        reinterpret_cast<void*>(Wrapper_GetSubgraphInputCount),
        reinterpret_cast<void*>(Wrapper_GetSubgraphFlags),
        reinterpret_cast<void*>(Wrapper_GetInputMapping),
        reinterpret_cast<void*>(Wrapper_IsUVClampingRequired),
        reinterpret_cast<void*>(Wrapper_IsSamplerDataExtRequired),
        reinterpret_cast<void*>(Wrapper_GetConstantBufferSize),
        reinterpret_cast<void*>(Wrapper_GetConstantBufferInitialValue),
        reinterpret_cast<void*>(Wrapper_ScalarDeletingDestructor),
        reinterpret_cast<void*>(Wrapper_FinalRelease),
    };

    void* AllocateBytes(size_t size)
    {
        // Match the native heap used by the rest of this synthetic object so cleanup
        // can be uniform in DestroyCompiledResult.
        if (!size)
        {
            return nullptr;
        }

        auto* memory = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, size);
        check_pointer(memory);
        return memory;
    }

    uint32_t PointerRangeByteSize(void const* begin, void const* end)
    {
        // Native vectors are represented by begin/end pointers. Defensive validation
        // avoids unsigned wrap if a malformed definition leaves a range inverted.
        auto const beginAddress = reinterpret_cast<uintptr_t>(begin);
        auto const endAddress = reinterpret_cast<uintptr_t>(end);
        if (!beginAddress || !endAddress || endAddress < beginAddress)
        {
            return 0;
        }

        return static_cast<uint32_t>(endAddress - beginAddress);
    }

    bool UsesFlattenSourceSubgraph(CustomEffectRuntime::CustomEffectDefinition const& definition)
    {
        // Keep this helper separate because the same boolean must drive both sides of
        // the ABI: EffectType slot 5 controls Traverser flattening, while
        // CreateCompiledResult controls the ICompiledEffect subgraph layout.
        return definition.flattenSourceBeforeCustomSampler;
    }

    uint32_t GetMainSubgraphIndex(CustomEffectRuntime::CustomEffectDefinition const& definition)
    {
        // Without flatten: subgraph 0 is the effect.
        // With N-source flatten: subgraphs 0..N-1 materialize each source,
        // subgraph N is the custom sampler, subgraph N+1 is the final wrapper.
        return UsesFlattenSourceSubgraph(definition) ? definition.sourceCount : 0u;
    }

    uint32_t GetCompiledSubgraphCount(CustomEffectRuntime::CustomEffectDefinition const& definition)
    {
        // Flatten creates one materialization subgraph per source, a custom effect
        // subgraph that consumes the materialized intermediates, and a final
        // composite wrapper. ICompiledEffect must expose the same count because
        // EffectInstance iterates the flattened subgraph vector when allocating
        // constant buffers.
        //
        // Without flatten: 1 subgraph (the effect itself).
        // With N-source flatten: N + 2 subgraphs.
        return UsesFlattenSourceSubgraph(definition)
            ? (definition.sourceCount + 2u)
            : 1u;
    }

    void InitializeDirectPropertyUpdater(
        ConstantBufferUpdater& updater,
        void** directUpdaterFunctionVtable,
        CustomEffectRuntime::NativePropertyMetadata const& metadata,
        uint32_t constantBufferOffset,
        uint32_t nodeIndex)
    {
        updater.nodeIndex = nodeIndex;
        updater.constantBufferOffset = constantBufferOffset;
        memset(&updater.update, 0, sizeof(updater.update));

        // wuceffectsi!DeclareShaderVariableForProperty uses this exact
        // std::function target for direct animatable properties. Reusing its vtable
        // keeps SetAnimatableProperty on the native EffectInstance path instead of
        // rebuilding brushes from XAML slider changes.
        auto* callable = reinterpret_cast<NativePropertyUpdaterCallable*>(updater.update.inlineStorage);
        callable->vtable = directUpdaterFunctionVtable;
        callable->metadata = metadata;
        updater.update.callable = callable;
    }

    void InitializeSubgraphInputs(
        CompiledSubgraph& subgraph,
        CustomEffectRuntime::SourceDescriptor const* sources,
        uint32_t sourceCount,
        bool mapFromSubgraphOutput,
        uint32_t mappedInputBase,
        bool copySamplerDataExtRequirements)
    {
        if (!sourceCount)
        {
            return;
        }

        // This fills two native vectors in parallel. inputBindings describes where
        // each logical source comes from; surfaceData describes which sampler helper
        // arguments DWM must make available for that input. The copySamplerData flag
        // lets final color-only wrapper subgraphs avoid requesting sampler metadata
        // they do not consume.
        auto* inputBindings = static_cast<InputBinding*>(
            AllocateBytes(sizeof(InputBinding) * sourceCount));
        auto* surfaceData = static_cast<SurfaceData*>(
            AllocateBytes(sizeof(SurfaceData) * sourceCount));
        for (uint32_t index = 0; index < sourceCount; ++index)
        {
            inputBindings[index].inputIndex = mappedInputBase + index;
            inputBindings[index].isSubgraphOutput = mapFromSubgraphOutput;

            // wuceffectsi copies EffectGenerator::SurfaceData bytes 4..7 into
            // CompiledEffectSubgraph::SurfaceData. This code-only path bypasses
            // that generator, so we synthesize the two bytes DWM later observes:
            // byte 2 drives IsUVClampingRequired and makes PopulateSamplerArguments
            // emit GetSamplerDataN, while byte 3 drives IsSamplerDataExtRequired
            // and emits GetSamplerDataExtN. This intentionally replaces the
            // earlier samplerDataExt-only shortcut because CCustomKernelEffect's
            // single-source model uses samplerData to recover the effective content
            // rect when a source has been materialized into a padded intermediate.
            surfaceData[index].data[2] =
                copySamplerDataExtRequirements && sources[index].requiresSamplerData ? 1 : 0;
            surfaceData[index].data[3] =
                copySamplerDataExtRequirements && sources[index].requiresSamplerDataExt ? 1 : 0;
        }

        subgraph.inputBindingBegin = inputBindings;
        subgraph.inputBindingEnd = inputBindings + sourceCount;
        subgraph.inputBindingCapacity = inputBindings + sourceCount;

        subgraph.surfaceDataBegin = surfaceData;
        subgraph.surfaceDataEnd = surfaceData + sourceCount;
        subgraph.surfaceDataCapacity = surfaceData + sourceCount;
    }

    void InitializeSubgraphShaderArguments(
        CompiledSubgraph& subgraph,
        uint16_t const* arguments,
        uint64_t argumentCount)
    {
        // Shader arguments are the compact numbers consumed by dwmcorei's shader
        // linker. They are not HLSL reflection data. Each value selects one linker
        // input kind such as color sample, uv, samplerData, samplerDataExt, or the
        // custom sampler return type.
        if (!argumentCount)
        {
            return;
        }

        auto* shaderArguments = static_cast<uint16_t*>(
            AllocateBytes(sizeof(uint16_t) * static_cast<size_t>(argumentCount)));
        memcpy(
            shaderArguments,
            arguments,
            sizeof(uint16_t) * static_cast<size_t>(argumentCount));

        subgraph.shaderArgumentBegin = shaderArguments;
        subgraph.shaderArgumentEnd = shaderArguments + argumentCount;
        subgraph.shaderArgumentCapacity = shaderArguments + argumentCount;
    }

    void* CreateCompiledResult(RuntimeEffectEntry* entry)
    {
        // Build the native-shaped compiled graph DWM expects after
        // CompileEffectDescription. Nothing in this function is cosmetic: every
        // vector range is later read either through the wrapper vtable or directly by
        // wuceffectsi's EffectInstance.
        EnsureShader(entry);

        CompiledResult* result{};
        try
        {
            auto const& definition = *entry->definition;
            result = static_cast<CompiledResult*>(
                HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(CompiledResult)));
            check_pointer(result);

            result->vtable = g_wrapperVtable;
            result->refCount = 1;
            result->entry = entry;

            if (UsesFlattenSourceSubgraph(definition))
            {
                // The flatten stage models the Traverser path used by source-materializing
                // effects such as GaussianBlur: each source brush is rendered into a real
                // intermediate texture so the custom sampler receives true Texture2D
                // bindings with samplerData/samplerDataExt instead of 0x0500 fragment-
                // dependency arguments.
                if (!definition.flattenShaderFunctionName)
                {
                    check_hresult(E_INVALIDARG);
                }
            }

            auto const subgraphCount = GetCompiledSubgraphCount(definition);
            auto* subgraph = static_cast<CompiledSubgraph*>(
                HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(CompiledSubgraph) * subgraphCount));
            check_pointer(subgraph);

            result->subgraphBegin = subgraph;
            result->subgraphEnd = subgraph + subgraphCount;
            result->subgraphCapacity = subgraph + subgraphCount;

            auto const mainSubgraphIndex = GetMainSubgraphIndex(definition);
            auto& mainSubgraph = subgraph[mainSubgraphIndex];

            if (UsesFlattenSourceSubgraph(definition))
            {
                constexpr uint16_t flattenColorArgument = 0x0200;

                // One materialization subgraph per source. Each renders its source
                // brush into a real intermediate texture so the custom sampler
                // receives true Texture2D bindings with samplerData/samplerDataExt
                // instead of 0x0500 fragment-dependency arguments.
                for (uint32_t i = 0; i < definition.sourceCount; ++i)
                {
                    auto& flattenSubgraph = subgraph[i];
                    InitializeSubgraphInputs(
                        flattenSubgraph,
                        &definition.sources[i],
                        1,
                        false,
                        i,
                        true);
                    InitializeSubgraphShaderArguments(flattenSubgraph, &flattenColorArgument, 1);
                    flattenSubgraph.flags = 0;
                    flattenSubgraph.linkingArgType = 0;
                }

                // Custom subgraph inputs are mapped from the flatten subgraph
                // outputs: input 0 ← subgraph output 0, input 1 ← subgraph output 1,
                // …, input N-1 ← subgraph output N-1.
                InitializeSubgraphInputs(
                    mainSubgraph,
                    definition.sources,
                    definition.sourceCount,
                    true,
                    0,
                    true);

                // Final color-passthrough wrapper around the custom subgraph.
                // Exactly one input (the custom result), no sampler metadata needed.
                constexpr uint16_t finalColorArgument = 0x0200;
                auto& finalSubgraph = subgraph[definition.sourceCount + 1];
                CustomEffectRuntime::SourceDescriptor const finalSourceDesc = {
                    L"",
                    CustomEffectRuntime::SourceKind::Backdrop,
                    false,
                    false,
                };
                InitializeSubgraphInputs(
                    finalSubgraph,
                    &finalSourceDesc,
                    1,
                    true,
                    definition.sourceCount,
                    false);
                InitializeSubgraphShaderArguments(finalSubgraph, &finalColorArgument, 1);
                finalSubgraph.flags = 0;
                finalSubgraph.linkingArgType = 0;
            }
            else
            {
                InitializeSubgraphInputs(
                    mainSubgraph,
                    definition.sources,
                    definition.sourceCount,
                    false,
                    0,
                    true);
            }

            InitializeSubgraphShaderArguments(
                mainSubgraph,
                definition.shaderArguments,
                definition.shaderArgumentCount);

            if (definition.constantBufferSize)
            {
                // Store the initial constant buffer only on the main shader subgraph.
                // Helper flatten/final wrapper subgraphs are color passthrough nodes
                // and must report no constant buffer to EffectInstance.
                auto* constantBuffer = static_cast<uint8_t*>(
                    AllocateBytes(definition.constantBufferSize));
                if (definition.constantBufferInitialValue)
                {
                    memcpy(
                        constantBuffer,
                        definition.constantBufferInitialValue,
                        definition.constantBufferSize);
                }

                mainSubgraph.constantBufferInitialBegin = constantBuffer;
                mainSubgraph.constantBufferInitialEnd = constantBuffer + definition.constantBufferSize;
                mainSubgraph.constantBufferInitialCapacity = constantBuffer + definition.constantBufferSize;
            }

            if (definition.constantBufferPropertyCount)
            {
                // Animated CompositionBrush properties update the native constant
                // buffer through DirectPropertyUpdater callables. This validates that
                // every public property maps to a real scalar range inside the native
                // property struct and the HLSL constant buffer.
                check_pointer(definition.nativePropertyMetadata);
                check_pointer(definition.constantBufferProperties);

                auto* metadata = static_cast<CustomEffectRuntime::NativePropertyMetadata const*>(
                    definition.nativePropertyMetadata);
                auto* module = g_wuceffectsiModule ? g_wuceffectsiModule : GetModuleHandleW(L"wuceffectsi.dll");
                check_pointer(module);
                auto* directUpdaterFunctionVtable =
                    reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(module) + kDirectPropertyUpdaterFunctionVtableRva);

                auto* updaters = static_cast<ConstantBufferUpdater*>(
                    AllocateBytes(sizeof(ConstantBufferUpdater) * definition.constantBufferPropertyCount));
                for (uint32_t index = 0; index < definition.constantBufferPropertyCount; ++index)
                {
                    auto const& mapping = definition.constantBufferProperties[index];
                    if (mapping.propertyIndex >= definition.nativePropertyMetadataCount)
                    {
                        check_hresult(E_INVALIDARG);
                    }

                    auto const& property = metadata[mapping.propertyIndex];
                    auto const propertyBytes = property.valueCount * sizeof(float);
                    if (property.propertyType != 8 ||
                        property.propertyOffset + propertyBytes > definition.propertiesStructSize ||
                        mapping.constantBufferOffset + propertyBytes > definition.constantBufferSize)
                    {
                        check_hresult(E_INVALIDARG);
                    }

                    InitializeDirectPropertyUpdater(
                        updaters[index],
                        directUpdaterFunctionVtable,
                        property,
                        mapping.constantBufferOffset,
                        mainSubgraphIndex);
                }

                mainSubgraph.constantBufferUpdaterBegin = updaters;
                mainSubgraph.constantBufferUpdaterEnd = updaters + definition.constantBufferPropertyCount;
                mainSubgraph.constantBufferUpdaterCapacity = updaters + definition.constantBufferPropertyCount;
            }

            // DWM materializes non-final subgraphs with flags==0 via
            // CBrushRenderingGraphBuilder::CreateTechniqueForFragment. When
            // keepAsFragmentOutput is true (default), the custom material subgraph
            // stays linked as a fragment output so DWM doesn't render it into an
            // upstream prescale target. Set to false when this effect's output
            // must be consumable by a non-flatten downstream effect (relay mode).
            mainSubgraph.flags = (UsesFlattenSourceSubgraph(definition) && definition.keepAsFragmentOutput)
                ? kCompiledEffectSubgraphOutputFlag
                : 0;
            mainSubgraph.linkingArgType = definition.linkingArgType;
            return result;
        }
        catch (...)
        {
            if (result)
            {
                DestroyCompiledResult(result);
            }

            throw;
        }
    }

    RuntimeEffectEntry* FindEntryInGraph(void* description)
    {
        // CompileEffectDescription is shared by all composition effects. Only return
        // a custom compiled result when the flattened graph actually contains one of
        // our synthetic EffectType pointers; otherwise forward to the original export.
        if (!description)
        {
            return nullptr;
        }

        // CompileEffectDescription receives the IEffectDescriptionWithNames interface
        // pointer at FlattenedEffectGraph + 0x10, not the object base. Reversing the
        // export showed it subtracts 0x10 before invoking EffectGenerator::Compile, so
        // the detour must do the same when it inspects the node vector.
        auto* graph = static_cast<uint8_t*>(description) - 0x10;
        auto* nodeBegin = *reinterpret_cast<void***>(graph + 0x30);
        auto* nodeEnd = *reinterpret_cast<void***>(graph + 0x38);
        auto const beginAddress = reinterpret_cast<uintptr_t>(nodeBegin);
        auto const endAddress = reinterpret_cast<uintptr_t>(nodeEnd);
        if (!nodeBegin || !nodeEnd || endAddress < beginAddress)
        {
            return nullptr;
        }

        auto const nodeBytes = endAddress - beginAddress;
        if ((nodeBytes % sizeof(void*)) != 0)
        {
            return nullptr;
        }

        auto const nodeCount = nodeBytes / sizeof(void*);
        if (nodeCount > 0x19)
        {
            return nullptr;
        }

        std::lock_guard<std::mutex> guard(g_registryMutex);
        for (auto** current = nodeBegin; current != nodeEnd; ++current)
        {
            auto* node = *current;
            if (!node)
            {
                continue;
            }

            if (auto* entry = FindEntryByEffectTypeLocked(*reinterpret_cast<void**>(node)))
            {
                return entry;
            }
        }

        return nullptr;
    }

    HRESULT __fastcall DetourCompileEffectDescription(void* description, void** result)
    {
        // This detour is reached on DWM's effect compilation worker path after
        // wuceffectsi has already traversed and flattened the public IGraphicsEffect
        // graph. Replacing only the compile result keeps traversal, named inputs, and
        // animatable property enumeration on the normal WinUI path.
        if (!result)
        {
            return E_POINTER;
        }

        if (auto* entry = FindEntryInGraph(description))
        {
            try
            {
                *result = CreateCompiledResult(entry);
                return S_OK;
            }
            catch (...)
            {
                *result = nullptr;
                return to_hresult();
            }
        }

        return g_originalCompileEffectDescription(description, result);
    }

    bool IsTargetImport(char const* dllName)
    {
        // Restrict import patching to wuceffectsi.dll. The symbol name alone is not a
        // safe discriminator because other modules can export unrelated functions with
        // the same name or keep helper thunks in delay-load tables.
        return dllName && _stricmp(dllName, "wuceffectsi.dll") == 0;
    }

    void PatchSlot(void** slot, void* replacement)
    {
        // IAT/delay-IAT sections are normally read-only. Patch one pointer at a time
        // and restore the original protection immediately to reduce the blast radius
        // if another module maps the same page as executable/read-only import data.
        DWORD oldProtect{};
        if (VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &oldProtect))
        {
            *slot = replacement;
            FlushInstructionCache(GetCurrentProcess(), slot, sizeof(void*));
            DWORD unused{};
            VirtualProtect(slot, sizeof(void*), oldProtect, &unused);
        }
    }

    void PatchImport(HMODULE module, ImportPatch const* patches, size_t patchCount)
    {
        // Patch already-resolved imports. This covers modules that linked
        // wuceffectsi.dll normally and whose FirstThunk entries already contain the
        // resolved CompileEffectDescription address.
        auto* base = reinterpret_cast<uint8_t*>(module);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        {
            return;
        }

        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE)
        {
            return;
        }

        auto const& imports = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (!imports.VirtualAddress || !imports.Size)
        {
            return;
        }

        auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
        for (; descriptor->Name; ++descriptor)
        {
            if (!IsTargetImport(reinterpret_cast<char const*>(base + descriptor->Name)))
            {
                continue;
            }

            auto* thunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
            for (; thunk->u1.Function; ++thunk)
            {
                auto** slot = reinterpret_cast<void**>(&thunk->u1.Function);
                for (size_t index = 0; index < patchCount; ++index)
                {
                    if (*slot == patches[index].original)
                    {
                        PatchSlot(slot, patches[index].replacement);
                        break;
                    }
                }
            }
        }
    }

    bool IsImportByOrdinal(IMAGE_THUNK_DATA const& thunk)
    {
        // Delay import name thunks can encode ordinals. Those entries have no
        // IMAGE_IMPORT_BY_NAME payload, so trying to read Name would treat an ordinal
        // value as an RVA and walk invalid memory.
#ifdef _WIN64
        return IMAGE_SNAP_BY_ORDINAL64(thunk.u1.Ordinal);
#else
        return IMAGE_SNAP_BY_ORDINAL32(thunk.u1.Ordinal);
#endif
    }

    void PatchDelayImport(HMODULE module, ImportPatch const* patches, size_t patchCount)
    {
        // Patch delay-load imports before first use. dcompi.dll reaches
        // CompileEffectDescription through a delay import table, so scanning only the
        // normal import directory makes the detour appear installed while calls still
        // go to the original export.
        auto* base = reinterpret_cast<uint8_t*>(module);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        {
            return;
        }

        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE)
        {
            return;
        }

        auto const& delayImports = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT];
        if (!delayImports.VirtualAddress || !delayImports.Size)
        {
            return;
        }

        auto* descriptor = reinterpret_cast<IMAGE_DELAYLOAD_DESCRIPTOR*>(base + delayImports.VirtualAddress);
        for (; descriptor->DllNameRVA; ++descriptor)
        {
            if (!descriptor->Attributes.RvaBased)
            {
                continue;
            }

            if (!IsTargetImport(reinterpret_cast<char const*>(base + descriptor->DllNameRVA)))
            {
                continue;
            }

            // dcompi.dll delay-loads wuceffectsi.dll, so its calls are routed through the
            // .didat delay IAT instead of the normal import directory. Matching by import
            // name here is intentional: before delay resolution the slot is a helper thunk,
            // not GetProcAddress(wuceffectsi, name), so address comparison never reaches
            // VirtualProtect.
            auto* nameThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->ImportNameTableRVA);
            auto* addressThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->ImportAddressTableRVA);
            for (; nameThunk->u1.AddressOfData && addressThunk->u1.Function; ++nameThunk, ++addressThunk)
            {
                if (IsImportByOrdinal(*nameThunk))
                {
                    continue;
                }

                auto const* importByName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + nameThunk->u1.AddressOfData);
                auto const* importName = reinterpret_cast<char const*>(importByName->Name);
                for (size_t index = 0; index < patchCount; ++index)
                {
                    if (strcmp(importName, patches[index].name) == 0)
                    {
                        auto** slot = reinterpret_cast<void**>(&addressThunk->u1.Function);
                        PatchSlot(slot, patches[index].replacement);
                        break;
                    }
                }
            }
        }
    }

    void InstallHook()
    {
        // Hook installation is process-wide and must be idempotent. It is triggered
        // lazily by CreateEffect so callers can keep using a normal WinUI shape:
        // create an effect description and pass it to CreateEffectFactory.
        std::call_once(g_hookOnce, []
        {
            LoadLibraryW(L"dwmcorei.dll");
            auto module = LoadLibraryW(L"wuceffectsi.dll");
            check_pointer(module);

            auto original = reinterpret_cast<CompileEffectDescriptionFn>(
                GetProcAddress(module, "CompileEffectDescription"));
            check_pointer(original);

            g_originalCompileEffectDescription = original;
            g_wuceffectsiModule = module;

            InitializeAllEffectTypes(module);
            PatchEffectTypeFromGuid(module);

            ImportPatch const patches[] = {
                {
                    "CompileEffectDescription",
                    reinterpret_cast<void*>(original),
                    reinterpret_cast<void*>(DetourCompileEffectDescription),
                },
            };

            auto snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
            if (snapshot != INVALID_HANDLE_VALUE)
            {
                // Patch every currently loaded module because the call chain can be
                // rooted in dcompi, dwmcorei, the app module, or helper DLLs depending
                // on load order and delay-load state.
                MODULEENTRY32W entry{};
                entry.dwSize = sizeof(entry);
                if (Module32FirstW(snapshot, &entry))
                {
                    do
                    {
                        PatchImport(entry.hModule, patches, ARRAYSIZE(patches));
                        PatchDelayImport(entry.hModule, patches, ARRAYSIZE(patches));
                    } while (Module32NextW(snapshot, &entry));
                }

                CloseHandle(snapshot);
            }

            PatchImport(GetModuleHandleW(nullptr), patches, ARRAYSIZE(patches));
            PatchDelayImport(GetModuleHandleW(nullptr), patches, ARRAYSIZE(patches));
        });
    }

    struct RuntimeGraphicsEffect :
        winrt::implements<
            RuntimeGraphicsEffect,
            IGraphicsEffect,
            IGraphicsEffectSource,
            ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop>
    {
        // This is the public WinRT-facing object. WinUI and wuceffectsi initially
        // see only standard IGraphicsEffectD2D1Interop methods: effect GUID,
        // property count/defaults, and source list. The private runtime state is
        // deliberately not exposed here; the detours recover it later from the GUID
        // and synthetic EffectType pointer.
        explicit RuntimeGraphicsEffect(CustomEffectRuntime::CustomEffectDefinition const* definition) :
            m_definition(definition),
            m_name(definition->effectName)
        {
            m_sources.reserve(definition->sourceCount);
            for (uint32_t index = 0; index < definition->sourceCount; ++index)
            {
                auto const& sourceDefinition = definition->sources[index];
                // wuceffectsi!Traverser::FindSourceFlatteningEffect matches source
                // objects by raw COM pointer identity. Returning a freshly constructed
                // CompositionEffectSourceParameter from each GetSource call makes the
                // pre-enumeration flatten wrapper undiscoverable in VisitEffectInputs.
                m_sources.push_back(CompositionEffectSourceParameter(sourceDefinition.name)
                    .as<IGraphicsEffectSource>());
            }
        }

        hstring Name() const
        {
            // IGraphicsEffect::Name is still used by WinUI for property paths and
            // diagnostics even though the native compile path is redirected later.
            return m_name;
        }

        void Name(hstring const& value)
        {
            // Keep the public WinRT behavior normal: callers can rename the effect
            // instance before passing it to CreateEffectFactory, and property paths
            // should reflect that name.
            m_name = value;
        }

        HRESULT __stdcall GetEffectId(GUID* id) noexcept final
        {
            if (!id)
            {
                return E_POINTER;
            }

            // Unknown GUIDs are rejected by wuceffectsi!EffectType::FromGuid before the
            // compile hook runs. The runtime detour registers real EffectType objects for
            // every private GUID so custom effects do not masquerade as built-in D2D IDs.
            *id = m_definition->id;
            return S_OK;
        }

        HRESULT __stdcall GetNamedPropertyMapping(
            LPCWSTR name,
            UINT* index,
            ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept final
        {
            // This mapping is consumed by Compositor::CreateEffectFactory when it
            // validates animatable property paths such as EffectName.PropertyName.
            // The returned index must match both NativePropertyMetadata and
            // ConstantBufferPropertyMapping because the compile detour later builds
            // native constant-buffer updater records from the same index.
            if (!name || !index || !mapping)
            {
                return E_POINTER;
            }

            for (uint32_t propertyIndex = 0; propertyIndex < m_definition->propertyCount; ++propertyIndex)
            {
                auto const& property = m_definition->properties[propertyIndex];
                if (property.publicName && wcscmp(name, property.publicName) == 0)
                {
                    *index = property.index;
                    *mapping = property.mapping;
                    return S_OK;
                }
            }

            return E_INVALIDARG;
        }

        HRESULT __stdcall GetPropertyCount(UINT* count) noexcept final
        {
            if (!count)
            {
                return E_POINTER;
            }

            // This count is the public WinRT property count. It may be smaller than
            // the native metadata table if future effects add internal-only fields,
            // so callers must use the explicit mappings rather than assuming indexes
            // are interchangeable by accident.
            *count = m_definition->propertyCount;
            return S_OK;
        }

        HRESULT __stdcall GetProperty(UINT index, ABI::Windows::Foundation::IPropertyValue** value) noexcept final
        {
            // Default property values are still requested through the public
            // IGraphicsEffectD2D1Interop API during traversal. Native metadata only
            // handles the later DWM-side constant-buffer update path.
            if (!value)
            {
                return E_POINTER;
            }

            *value = nullptr;
            if (index >= m_definition->propertyCount)
            {
                return E_INVALIDARG;
            }

            auto const& property = m_definition->properties[index];
            if (!property.getDefaultValue)
            {
                return E_NOTIMPL;
            }

            return property.getDefaultValue(value);
        }

        HRESULT __stdcall GetSource(
            UINT index,
            ABI::Windows::Graphics::Effects::IGraphicsEffectSource** source) noexcept final
        {
            // Return stable source COM identities. Source flattening relies on
            // pointer identity during EnumerateEffectSubgraphs/VisitEffectInputs;
            // creating a new CompositionEffectSourceParameter per call would make
            // the precomputed flatten wrapper impossible to find.
            if (!source)
            {
                return E_POINTER;
            }

            *source = nullptr;
            if (index >= m_definition->sourceCount)
            {
                return E_INVALIDARG;
            }

            try
            {
                void* abi{};
                copy_to_abi(m_sources[index], abi);
                *source = static_cast<ABI::Windows::Graphics::Effects::IGraphicsEffectSource*>(abi);
                return S_OK;
            }
            catch (...)
            {
                return to_hresult();
            }
        }

        HRESULT __stdcall GetSourceCount(UINT* count) noexcept final
        {
            if (!count)
            {
                return E_POINTER;
            }

            // Traverser enumerates this count before calling GetSource. It must match
            // the source descriptors used by InitializeSubgraphInputs, otherwise the
            // public graph and synthetic compiled graph describe different edges.
            *count = m_definition->sourceCount;
            return S_OK;
        }

    private:
        CustomEffectRuntime::CustomEffectDefinition const* m_definition{};
        hstring m_name;
        std::vector<IGraphicsEffectSource> m_sources;
    };
}

namespace CustomEffectRuntime
{
    void RegisterEffect(CustomEffectDefinition const& definition)
    {
        // RegisterEffect is intentionally cheap and idempotent. Multiple calls can
        // happen if the app creates the same effect description for several brushes;
        // all of them should share the same synthetic EffectType and shader blob.
        std::lock_guard<std::mutex> guard(g_registryMutex);
        if (FindEntryByGuidLocked(definition.id))
        {
            return;
        }

        auto* entry = new RuntimeEffectEntry(definition);
        if (g_wuceffectsiModule)
        {
            InitializeEffectType(entry, g_wuceffectsiModule);
        }

        entry->next = g_effects;
        g_effects = entry;
    }

    IGraphicsEffect CreateEffect(CustomEffectDefinition const& definition)
    {
        // The public shape must be the same shape WinUI expects from built-in effects:
        // an IGraphicsEffect that can be passed directly to Compositor::CreateEffectFactory.
        // Registration and hook installation live here only to make that object usable
        // before wuceffectsi resolves its private EffectType GUID.
        //
        // Callers should not need to know about the native compiled-result object.
        // That separation is what keeps effect definitions code-only: each effect
        // supplies metadata and HLSL source, while this runtime owns the fragile
        // build-specific ABI adaptation.
        RegisterEffect(definition);
        InstallHook();
        return make<RuntimeGraphicsEffect>(&definition);
    }

}
