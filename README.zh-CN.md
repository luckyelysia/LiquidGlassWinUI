# LiquidGlassWinUI

[English](./README.md)

一个 WinUI 3 `XamlCompositionBrushBase`，在背景内容之上渲染 Apple 风格的液态玻璃材质。
只需在任意元素的 `Background` 中放入 `<lg:LiquidGlassBrush />`，即可获得实时折射、色散、
菲涅耳边缘光、镜面高光、染色玻璃、Bloom 混合及完整色彩调整——所有参数均由依赖属性驱动，
可直接从 XAML 绑定和动画化，无需任何代码后置。

```xml
xmlns:lg="using:LiquidGlassWinUI"
...
<Rectangle Fill="{x:Null}">
  <Rectangle.Background>
    <lg:LiquidGlassBrush BlurAmount="1.93"
                         BloomAmount="0.15"
                         Brightness="0.05"
                         Contrast="1.1"
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
                         Saturation="1.15"
                         ShapeRadius="0.92"
                         ShapeRoundness="3.84"
                         Temperature="0.1"
                         Vibrance="0.2"/>
  </Rectangle.Background>
</Rectangle>
```

![Liquid glass 预览](LiquidGlassWinUI/Assets/liquidglass.png)

## 为什么需要 LiquidGlassWinUI

现代桌面应用越来越多地采用半透明、具有深度感的材质来传达层次与焦点。WinUI 3 内置了
`AcrylicBrush` 和 `MicaBackdrop`，但两者都无法提供真正玻璃材质应有的折射深度、色彩分离
和镜面反射效果。LiquidGlassWinUI 填补了这一空白。

其核心思路是将自定义 HLSL 像素着色器直接注册到 DWM 合成管线中——无需离屏渲染目标，
无需 CPU 侧图像处理。最终得到的是一个合成器原生的效果，以完整帧率运行，读取实时背景，
并将每个材质参数暴露为可动画化的 `DependencyProperty`。

## 安装

```powershell
dotnet add package LiquidGlassWinUI
```

**环境要求：**
- Windows 11 x64，或 Windows 10 x64（版本 19041 及以上）
- .NET 8

> [!IMPORTANT]
> **Windows App SDK 2.2.0** 是硬性要求。原生 IAT Hook 针对此版本
> `wuceffectsi.dll` 的内部符号定位；更早或更新的版本二进制布局不同，
> Hook 将无法安装。
> **仅限 x64 进程。** 原生运行时（`CustomEffectRuntimeNative.dll`）不提供 x86 或 ARM64 构建。

## 快速开始

### 1. 添加命名空间

```xml
xmlns:lg="using:LiquidGlassWinUI"
```

### 2. 应用画刷

直接在任意 `Border` 或 `Panel`（Grid、StackPanel 等）上设置 `Background`：

```xml
<Grid>
  <!-- 背景内容 -->
  <Image Source="Assets/background.jpg" Stretch="UniformToFill"/>

  <!-- 玻璃面板 -->
  <Border>
    <Border.Background>
      <lg:LiquidGlassBrush BlurAmount="2.0"
                           RefThickness="30"
                           ShapeRadius="0.5"/>
    </Border.Background>
    <TextBlock Text="Hello, glass."/>
  </Border>
</Grid>
```

画刷通过 WinUI 合成器背景机制读取元素后方的内容。`Border` 背后的内容会被折射和模糊；
玻璃材质仅绘制在元素边界之内，子内容（如上方的 `TextBlock`）会渲染在玻璃之上。

### 3. 调节外观

每个参数都是 `DependencyProperty`，可绑定到 ViewModel 属性或用视觉状态动画化：

```xml
<lg:LiquidGlassBrush RefThickness="{x:Bind ViewModel.GlassThickness, Mode=OneWay}"
                     GlareAngle="{x:Bind ViewModel.LightAngle, Mode=OneWay}"
                     TintA="0.15"
                     TintR="180"
                     TintG="200"
                     TintB="255"/>
```

### 4. 检查错误

如果效果管线编译失败（例如着色器超出 DWM 限制，或原生 Hook 无法安装），画刷会回退为
纯红色填充——一个显眼的异常信号，但不会导致应用崩溃。通过以下方式读取诊断信息：

```csharp
if (LiquidGlassBrush.LastError is not null)
    Debug.WriteLine(LiquidGlassBrush.LastError);
```

## 参数

全部 28 个材质参数均注册为 `DependencyProperty`，并采用工厂池化的可动画路径。
按功能分组如下：

### 折射 (Refraction)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `RefThickness` | 20 | 1--80 | 折射最强的边缘厚度（逻辑像素） |
| `RefFactor` | 1.4 | 1--4 | 折射率 (IOR)，值越高背景弯曲越明显 |
| `RefDispersion` | 7 | 0--50 | 色散强度（按波长分离色彩通道） |
| `DispersionRange` | 1.0 | 0--1 | 色散从边缘向内传播的范围（0 = 仅边缘，1 = 全部内部） |
| `RefFresnelRange` | 30 | 0--100 | 掠射角附近菲涅耳折射带的角宽度 |
| `RefFresnelHardness` | 20 | 0--100 | 菲涅耳带的衰减锐度 |
| `RefFresnelFactor` | 20 | 0--100 | 菲涅耳边缘光的强度系数 |
| `Magnification` | 1.0 | 1--3 | 以玻璃为中心的背景缩放倍率（1.0 = 无缩放，不可小于 1） |

### 高光 (Glare)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `GlareRange` | 30 | 0--100 | 镜面高光条纹的角宽度 |
| `GlareHardness` | 20 | 0--100 | 高光条纹的衰减锐度 |
| `GlareFactor` | 90 | 0--100 | 主高光强度 |
| `GlareConvergence` | 50 | 0--100 | 高光向中心汇聚的紧密度 |
| `GlareOppositeFactor` | 80 | 0--100 | 次高光（反方向）强度 |
| `GlareAngle` | -45 | -- | 高光条纹方向（度） |

### 模糊 (Blur)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `BlurAmount` | 1.0 | 0+ | 背景模糊半径（逻辑像素）。设为 0 可完全绕过模糊通道；玻璃仍会对清晰的背景进行折射。 |

### 染色 (Tint)

染色在玻璃着色器内部与折射后的颜色相乘。所有通道默认为 255（白色 = 无染色），alpha 为
0（完全透明染色）。

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `TintR` | 255 | 0--255 | 玻璃染色红色通道 |
| `TintG` | 255 | 0--255 | 玻璃染色绿色通道 |
| `TintB` | 255 | 0--255 | 玻璃染色蓝色通道 |
| `TintA` | 0 | 0--1 | 染色不透明度（0 = 无色透明，1 = 完全染色） |

### 后处理 (Post-Processing)

Bloom 混合与色彩调整在模糊通道和玻璃着色器之间的独立后处理阶段运行。所有参数均可通过
合成器线程的 `ScalarKeyFrameAnimation` 动画化。

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `BloomAmount` | 1 | 0--1 | 清晰与模糊背景之间的混合：0 = 完全清晰（原始背景），1 = 完全模糊 |
| `Exposure` | 1.0 | 0.5--2 | 乘法亮度增益 |
| `Brightness` | 0 | -1--1 | 加法亮度偏移（负值变暗，正值变亮） |
| `Contrast` | 1.0 | 0--2 | 以中灰为中心的对比度系数（1.0 = 不变） |
| `Saturation` | 1.0 | 0--2 | 色彩饱和度（0 = 灰度，1 = 不变，2 = 过饱和） |
| `Temperature` | 0 | -1--1 | 色温偏移（负值偏冷/蓝，正值偏暖/黄） |
| `Vibrance` | 0 | 0--1 | 智能自然饱和度增强，针对低饱和度区域，同时保护肤色不过饱和 |

### 形状 (Shape)

| 属性 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| `ShapeRadius` | 0.4 | 0--1 | 圆角半径占短半边的比例 |
| `ShapeRoundness` | 5 | 2--8 | 超椭圆圆角度指数 n（越大角越方） |

### 诊断

| 成员 | 说明 |
|---|---|
| `LastError` (static string) | 效果编译或链接失败时，错误信息写入此处而非抛出异常。 |

## 动画

每个材质参数都是 `DependencyProperty`，因此标准 XAML `Storyboard` 动画可直接作用于
命名画刷，无需任何代码后置即可驱动材质。

### XAML Storyboard（推荐）

通过名称引用画刷，用 `DoubleAnimation` 对任意参数做动画：

```xml
<Page.Resources>
  <Storyboard x:Name="MorphStoryboard"
              AutoReverse="True"
              RepeatBehavior="Forever">
    <DoubleAnimation
      Storyboard.TargetName="GlassBrush"
      Storyboard.TargetProperty="RefThickness"
      From="6" To="52" Duration="0:0:3.5"
      EnableDependentAnimation="True">
      <DoubleAnimation.EasingFunction>
        <CubicEase EasingMode="EaseInOut"/>
      </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
    <DoubleAnimation
      Storyboard.TargetName="GlassBrush"
      Storyboard.TargetProperty="GlareFactor"
      From="35" To="120" Duration="0:0:3.5"
      EnableDependentAnimation="True">
      <DoubleAnimation.EasingFunction>
        <CubicEase EasingMode="EaseInOut"/>
      </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
    <DoubleAnimation
      Storyboard.TargetName="GlassBrush"
      Storyboard.TargetProperty="BlurAmount"
      From="0.6" To="4.2" Duration="0:0:3.5"
      EnableDependentAnimation="True">
      <DoubleAnimation.EasingFunction>
        <CubicEase EasingMode="EaseInOut"/>
      </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
  </Storyboard>

  <!-- GlareAngle 是周期性的：0 到 360 度无缝循环，
       不会像 AutoReverse 那样来回摆动 -->
  <Storyboard x:Name="GlareSpinStoryboard" RepeatBehavior="Forever">
    <DoubleAnimation
      Storyboard.TargetName="GlassBrush"
      Storyboard.TargetProperty="GlareAngle"
      From="0" To="360" Duration="0:0:6"
      EnableDependentAnimation="True"/>
  </Storyboard>
</Page.Resources>

<Border CornerRadius="34">
  <Border.Background>
    <lg:LiquidGlassBrush x:Name="GlassBrush"
                         BlurAmount="1.5"
                         GlareFactor="90"
                         RefThickness="20"
                         ShapeRadius="0.5"/>
  </Border.Background>
</Border>
```

> [!WARNING]
> 每个针对自定义依赖属性的 `DoubleAnimation` 都必须加上
> `EnableDependentAnimation="True"`。WinUI 默认禁用依赖动画；缺少此标记动画会静默无效。

在代码后置中通过标准的 `Begin`/`Pause`/`Resume`/`Stop` 方法控制 Storyboard，
并可调整 `SpeedRatio` 实现变速播放。

### C# 动画 API

编程式场景下，画刷还提供两个方法：

**逐属性动画** -- `AnimateScalar` 完全在合成器线程上运行，采用三次缓出曲线：

```csharp
myBrush.AnimateScalar("Exposure", 1.5f, durationMs: 800);
myBrush.AnimateScalar("GlareAngle", 45f, durationMs: 1200);
```

**批量过渡** -- `TransitionTo` 在单个合成器帧内将所有参数动画化到另一个画刷实例的目标值，
通过 `CompositionCommitBatch` 确保同步：

```csharp
var target = new LiquidGlassBrush
{
    BlurAmount = 4.0,
    GlareFactor = 60,
    TintA = 0.3,
};
currentBrush.TransitionTo(target, durationMs: 600);
```

批量完成后，源画刷的依赖属性会同步到目标值，确保后续重连或外部 `SetValue` 不会覆盖
动画终点。

### 代码构建的 Storyboard

也可以完全在 C# 中构建 `Storyboard`——同样需要 `EnableDependentAnimation`：

```csharp
var anim = new DoubleAnimation
{
    From = GlassBrush.Magnification,
    To = 2.2,
    Duration = TimeSpan.FromMilliseconds(280),
    AutoReverse = true,
    EnableDependentAnimation = true,
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
};
Storyboard.SetTarget(anim, GlassBrush);
Storyboard.SetTargetProperty(anim, "Magnification");

var sb = new Storyboard();
sb.Children.Add(anim);
sb.Begin();
```

## 架构

### 效果管线

```
backdrop --> BlurH --> BlurV --> PostProcessing --> LiquidGlassEffect
                |                       |
                |   BlurAmount 驱动     |   BloomAmount 在模糊和
                |   H 和 V 通道         |   清晰背景之间混合
```

- **BlurH / BlurV** -- 一维可分离高斯模糊（双线性合并，21 tap 折叠为 10 对采样）。
  `BlurAmount` 同时驱动两个通道；设为 0 时断开模糊链，将原始背景直接送入后处理阶段。
- **PostProcessing** -- Bloom 混合（`Backdrop` 与 `RawBackdrop` 之间）加上七项色彩调整
  （曝光、亮度、对比度、饱和度、色温、自然饱和度）。即使模糊被绕过也会运行。
- **LiquidGlassEffect** -- 主玻璃材质：带超椭圆角的 SDF 形状、法线推导、通过背景 UV
  偏移实现的折射、色散（三个波长采样）、菲涅耳边缘光、双向镜面高光、染色乘法和 4x MSAA
  边缘抗锯齿。

### CustomEffectRuntime

管线中的效果节点并非内置 Win2D 效果，而是通过 `CustomEffectRuntimeNative.dll`
注册的自定义 HLSL 像素着色器。这是一个 C++/WinRT 库，在运行时动态修补 DWM 合成管线：

1. **对 `wuceffectsi.dll` 的 IAT Hook** 拦截 `EffectType::FromGuid` 和
   `CompileEffectDescription`，使自定义效果 GUID 被合成器识别为合法的效果类型。
2. **`RuntimeGraphicsEffect`** 实现了 `IGraphicsEffect` 和 `IGraphicsEffectD2D1Interop`，
   可被 `Compositor::CreateEffectFactory` 视为标准效果节点接受。
3. **着色器链接** 在 DWM 遍历效果图时返回合成的 `CompiledResult`，内含自定义 HLSL
   字节码、入口点、采样器绑定和 cbuffer 布局。

C# 层（`Effects/CustomEffectBase.cs`、`Interop/CustomEffectBuilder.cs`、
`Interop/CustomEffectInterop.cs`）从嵌入的 HLSL 资源组装效果定义，并通过 P/Invoke
编组到原生运行时。着色器以 `<EmbeddedResource>` 嵌入程序集——消费者输出目录中无散落文件。

### 工厂池化

`CompositionEffectFactory` 将可动画属性注册到合成器的全局跟踪表中，该表的硬上限为
256 条聚合记录。如果在每次画刷连接/断开时都创建新工厂，页面导航会迅速耗尽此配额。
因此工厂仅创建一次并静态持有；只有每个实例的画刷会被释放。此上限对消费者透明。

## 项目结构

| 目录 | 用途 |
|---|---|
| `LiquidGlassWinUI/` | C# 类库——`LiquidGlassBrush` 及其效果管线。以 NuGet 包形式发布。 |
| `LiquidGlassDemo/` | WinUI 3 演示应用，提供交互式参数滑块、实时预览和 XAML/C# 代码片段导出。 |
| `Native/` | C++/WinRT DLL，负责将自定义 HLSL 着色器注入 DWM 合成管线。 |
| `BlendProbe/` | 内部研究沙箱，含 26+ 测试着色器，用于探测 DWM 效果与链接器行为。不随类库发布。 |

解决方案文件：`LiquidGlassWinUI.slnx`（需要 Visual Studio 2022 及以上，或支持 `.slnx`
的 `dotnet` CLI）。

## 从源码构建

### 前置条件

- Windows 11 x64 或 Windows 10 x64（版本 19041+）
- Visual Studio 2022（含 Windows App SDK 工作负载）
- .NET 8 SDK

### 1. 构建原生运行时

```powershell
msbuild Native/CustomEffectRuntime.Native.vcxproj /p:Configuration=Release /p:Platform=x64
```

产出 `Native/Output/x64/Release/CustomEffectRuntimeNative.dll`。托管项目的 `.csproj`
引用此路径；NuGet 打包目标会检查该文件是否存在。

### 2. 构建托管项目

```powershell
dotnet build LiquidGlassWinUI/LiquidGlassWinUI.csproj -c Release
dotnet build LiquidGlassDemo/LiquidGlassDemo.csproj -c Release
```

或在 Visual Studio 中打开 `LiquidGlassWinUI.slnx`，构建整个解决方案。

### 项目引用方式（不使用 NuGet）

> [!NOTE]
> MSBuild 不会传递复制 `<ProjectReference>` 中的原生 DLL。如果你直接引用
> `LiquidGlassWinUI.csproj` 而非通过 NuGet 包，请在构建原生 DLL 后向你的项目添加：
>
> ```xml
> <Content Include="path\to\Native\Output\x64\Release\CustomEffectRuntimeNative.dll"
>          CopyToOutputDirectory="PreserveNewest"
>          Link="CustomEffectRuntimeNative.dll"
>          Visible="false" />
> ```

## 错误处理与诊断

> [!WARNING]
> 当效果管线无法编译或链接时——无论是 HLSL 着色器超出 DWM profile 预算（512 指令槽，
> `ps_4_0_level_9_3` 目标），还是原生 IAT Hook 无法安装——画刷会将 `CompositionBrush`
> 设为纯红色填充而非抛出异常。它不优雅，但不会弄崩宿主应用。完整的错误消息和堆栈跟踪可
> 通过静态属性 `LiquidGlassBrush.LastError` 获取。

## BlendProbe

`BlendProbe/` 是内部研究工具，刻意与发布的类库分离。它包含 26+ HLSL 测试着色器及对应
的效果定义，用于系统性测绘 DWM 效果与链接器行为：多源拓扑、采样器类型、Flatten 模式和
着色器参数组合。新的效果模式在此沙箱中验证后，才会提升到生产级 `LiquidGlassWinUI`
类库。计划进行着色器扩展的贡献者应从这里开始。

## 参与贡献

欢迎贡献。以下方面尤其需要帮助：

- **ARM64 支持** -- 原生运行时（`CustomEffectRuntimeNative.dll`）目前仅支持 x64。将
  IAT Hook 和 `RuntimeGraphicsEffect` 移植到 ARM64 可将平台覆盖扩展到 Snapdragon X
  设备。
- **更多玻璃形状** -- 当前玻璃形状为超椭圆。添加任意 SDF 形状（胶囊形、星形、文字路径）
  将拓展应用场景。
- **性能分析** -- 刻画多个玻璃画刷同时使用时的合成器侧开销，优化可分离模糊卷积核。
- **演示应用** -- 展示材质集成到导航视图、媒体播放器或设置面板中的真实使用示例。

在开始较大工作前，请先开 Issue 讨论方案。`BlendProbe/` 沙箱是进行着色器层面实验的推荐
起点。

## 相关项目

- [WUILiquidGlassDemo](https://github.com/apkipa/WUILiquidGlassDemo) -- 率先逆向工程
  Windows App SDK 合成管线，开创了使自定义效果注册成为可能的 IAT Hook 技术。
- [liquid-glass-studio](https://github.com/iyinchao/liquid-glass-studio) -- 液态玻璃
  着色器的 WebGL 参考实现，包括本项目 HLSL 移植所参考的 SDF 形状模型、色散采样、
  菲涅耳公式和双向高光模型。

## 致谢

本项目直接建立在 Windows 合成逆向工程和实时玻璃渲染社区的前期工作之上：

- **apkipa** 的 [WUILiquidGlassDemo](https://github.com/apkipa/WUILiquidGlassDemo)，
  首次公开展示了将自定义 HLSL 效果注入 DWM 合成图的可行性。本项目的 IAT Hook 策略、
  `RuntimeGraphicsEffect` 模式和 P/Invoke 编组均源于该项工作。
- **iyinchao** 的 [liquid-glass-studio](https://github.com/iyinchao/liquid-glass-studio)，
  参考着色器，其 SDF 形状模型、色散采样、菲涅耳公式和双向高光模型被移植到 HLSL
  以运行于 Windows 合成器中。
- 所有提交 Bug 报告、测试预览版、或不断试探 DWM 效果链接器极限的社区成员。

## 许可证

MIT -- 详见 [LICENSE](LICENSE) 文件。
