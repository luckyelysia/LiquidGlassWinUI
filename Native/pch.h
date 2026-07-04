#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <d3dcompiler.h>
#include <restrictederrorinfo.h>
#include <hstring.h>
#include <tlhelp32.h>
#include <algorithm>
#include <atomic>
#include <cstring>
#include <cwctype>
#include <memory>
#include <mutex>
#include <utility>
#include <vector>

// Undefine GetCurrentTime macro to prevent
// conflict with Storyboard::GetCurrentTime
#undef GetCurrentTime

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Microsoft.UI.Composition.h>
#include <windows.graphics.effects.interop.h>
#include <wil/cppwinrt_helpers.h>
