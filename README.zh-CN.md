# LiquidGlassWinUI

一个 WinUI 3 `XamlCompositionBrushBase`，在 backdrop 内容之上渲染 Apple 风格的"液态玻璃"材质。
只需在 XAML 页面控件的 Background 中放入 `<lg:LiquidGlassBrush />` 即可获得实时折射、色散、菲涅耳边缘光、
镜面高光和染色玻璃效果——所有参数均由依赖属性驱动，可直接从 XAML 绑定和动画化。

```xml
xmlns:lg="using:LiquidGlassWinUI"
...
<Rectangle Fill="{x:Null}">
  <Rectangle.Background>
    <lg:LiquidGlassBrush BlurAmount="1.93"
                         DispersionRange="0.39"
                         Exposure="1.2"
                         GlareAngle="-135"
                         GlareConvergence="100"
                         GlareFactor="71.52"
                         GlareHardness="13"
                         GlareRange="36.13"
                         RefDispersion="3.5"
                         RefFactor="3.37"
                         RefFresnelFactor="21.96"
                         RefFresnelHardness="0"
                         RefFresnelRange="57.84"
                         RefThickness="80"
                         ShapeRadius="0.92"
                         ShapeRoundness="3.84"/>
  </Rectangle.Background>
</Rectangle>
```

![Liquid glass 预览](LiquidGlassWinUI/Assets/liquidglass.png)

## 项目结构

| 目录 | 用途 |
|---|---|
| `LiquidGlassWinUI/` | C# 类库 —— `LiquidGlassBrush` 及其效果管线。以 NuGet 包形式发布。 |
| `LiquidGlassDemo/` | WinUI 3 演示应用 —— 交互式参数调节、XAML/C# 代码片段导出。 |
| `Native/` | C++/WinRT DLL —— `CustomEffectRuntimeNative.dll`，负责将自定义 HLSL 着色器注入 DWM 合成管线。 |
| `BlendProbe/` | 内部研究工具 —— 26+ 测试着色器，用于验证 DWM 效果/链接器行为。不随类库发布。 |

解决方案文件：`LiquidGlassWinUI.slnx`（需要 Visual Studio 2022+ 或支持 `.slnx` 扩展的 `dotnet` CLI）。

## LiquidGlassBrush 公共 API

所有材质参数均暴露为 `DependencyProperty`。全部 20 个参数均可从 XAML 直接绑定和动画化，无需代码后置。

### 折射 (Refraction)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `RefThickness` | 20 | 1–80 | 折射边缘厚度（逻辑像素） |
| `RefFactor` | 1.4 | 1–4 | 折射率 (IOR) |
| `RefDispersion` | 7 | 0–50 | 色散强度 |
| `DispersionRange` | 1.0 | 0–1 | 色散在玻璃内部的衰减范围（0 = 仅边缘，1 = 全部） |
| `RefFresnelRange` | 30 | 0–100 | 掠射角附近的菲涅耳折射带宽度 |
| `RefFresnelHardness` | 20 | 0–100 | 菲涅耳带衰减锐度 |
| `RefFresnelFactor` | 20 | 0–100 | 菲涅耳边缘光强度系数 |
| `Magnification` | 1.0 | 1–3 | 玻璃背后的 backdrop 放大倍率（1.0 = 无放大，>1 = 放大，不可小于 1） |

### 高光 (Glare)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `GlareRange` | 30 | 0–100 | 镜面高光条纹的角度宽度 |
| `GlareHardness` | 20 | 0–100 | 高光条纹衰减锐度 |
| `GlareFactor` | 90 | 0–100 | 主高光强度 |
| `GlareConvergence` | 50 | 0–100 | 高光向中心汇聚的紧密度 |
| `GlareOppositeFactor` | 80 | 0–100 | 次高光（反方向）强度 |
| `GlareAngle` | -45 | — | 高光条纹方向（度） |

### 模糊与染色 (Blur & Tint)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `BlurAmount` | 1.0 | — | backdrop 模糊半径（像素）。设为 0 可完全绕过模糊链。 |
| `TintR` / `TintG` / `TintB` | 255 | 0–255 | 玻璃染色 RGB 通道 |
| `TintA` | 0 | 0–1 | 染色不透明度（0 = 无色透明，1 = 完全染色） |
| `Exposure` | 1.0 | 0.6–1.6 | backdrop 亮度增益 |

### 形状 (Shape)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `ShapeRadius` | 0.4 | 0–1 | 圆角半径占短半边的比例 |
| `ShapeRoundness` | 5 | 2–8 | 超椭圆圆角度指数 n |

### 诊断

| 成员 | 说明 |
|---|---|
| `Dpr` (float) | 物理像素/逻辑像素覆盖值。保持 0 可从系统 DPI 自动测量。 |
| `LastError` (static string) | 效果编译/链接失败时，错误信息写入此处，而非导致崩溃。 |

## 底层原理

`LiquidGlassBrush` 构建的是 Win2D `CompositionEffectBrush` 链，但效果节点**并非**内置 Win2D
效果——它们是通过 `CustomEffectRuntime` 注册的自定义 HLSL 着色器。

### CustomEffectRuntime（原生层）

`Native/CustomEffectRuntime.Native.vcxproj` 构建 `CustomEffectRuntimeNative.dll`，一个
C++/WinRT DLL，在运行时动态修补 DWM 合成管线：

1. **IAT Hook on `wuceffectsi.dll`** —— 拦截 `EffectType::FromGuid` 和
   `CompileEffectDescription`，使自定义效果 GUID 被识别为合法的效果类型。
2. **`RuntimeGraphicsEffect`** —— 一个 C++/WinRT 类，实现 `IGraphicsEffect` 和
   `IGraphicsEffectD2D1Interop`，可被 `Compositor::CreateEffectFactory` 作为标准效果节点接受。
3. **着色器链接** —— DWM 遍历效果图时，Hook 返回一个合成的 `CompiledResult`，包含自定义
   HLSL 字节码、入口点和链接参数（自定义采样器 UV、sampler data、cbuffer 绑定）。

C# 侧（`Effects/CustomEffectBase.cs`、`Interop/CustomEffectBuilder.cs`、
`Interop/CustomEffectInterop.cs`）组装效果定义——HLSL 源码（嵌入资源）、cbuffer 布局、源绑定、
着色器参数——并通过 P/Invoke 编组到原生运行时。

最终效果：自定义像素着色器在标准 `CompositionEffectBrush` 管线内运行，无需应用侧自绘
中间表面或渲染目标。

### HLSL 着色器（嵌入资源）

| 着色器 | 用途 |
|---|---|
| `LiquidGlass.hlsl` | 主玻璃材质 —— SDF、折射、色散、菲涅耳、高光、染色、抗锯齿 |
| `BlurH.hlsl` | 1D 水平可分离高斯模糊（双线性合并，21 taps → 10 对） |
| `BlurV.hlsl` | 1D 垂直可分离高斯模糊（相同卷积核） |

所有着色器均以 `<EmbeddedResource>` 嵌入程序集——消费者输出目录中无散落文件。

## 构建

### 前置条件

- Windows 11 (x64) 或 Windows 10 (x64, 19041+)
- Visual Studio 2022（含 Windows App SDK 工作负载）
- .NET 8 SDK

### 先构建原生 DLL

```powershell
msbuild Native/CustomEffectRuntime.Native.vcxproj /p:Configuration=Release /p:Platform=x64
```

产出 `Native/Output/x64/Release/CustomEffectRuntimeNative.dll`。托管类库的 `.csproj`
引用此路径；NuGet 打包目标会检查该文件是否存在。

### 构建托管项目

```powershell
# 构建类库
dotnet build LiquidGlassWinUI/LiquidGlassWinUI.csproj -c Release

# 构建并运行演示
dotnet build LiquidGlassDemo/LiquidGlassDemo.csproj -c Release
```

或在 Visual Studio 中打开 `LiquidGlassWinUI.slnx`，构建整个解决方案。

```powershell
dotnet add package LiquidGlassWinUI
```

## 平台支持

- **仅 x64。** 原生运行时（`CustomEffectRuntimeNative.dll`）仅构建 x64 版本。NuGet 的
  `.targets` 会在 x86 或 ARM64 上以可操作的错误消息提前终止构建。
- **Windows App SDK 2.2.0+**（目标框架 `net8.0-windows10.0.19041.0`）。

## 错误诊断

如果自定义效果编译或链接失败（例如着色器对当前 DWM 版本过于复杂，或 Hook 无法安装），
brush 会将 `CompositionBrush` 设为纯红色填充而非抛出异常，并将错误信息写入
`LiquidGlassBrush.LastError`。宿主应用不会崩溃。

## BlendProbe

`BlendProbe/` 是内部研究工具，刻意与发布的类库分离。它包含 26+ HLSL 测试着色器和对应
的效果定义，用于系统性探测 DWM 效果/链接器行为——多源拓扑、采样器类型、Flatten 模式
和着色器参数组合。新效果模式在此沙箱中验证后，才会提升到生产级 `LiquidGlassWinUI` 类库。

## 致谢

- [WUILiquidGlassDemo](https://github.com/apkipa/WUILiquidGlassDemo) — Windows App SDK 逆向与 composition hook 参考
- [liquid-glass-studio](https://github.com/iyinchao/liquid-glass-studio) — Liquid glass shader 参考实现
