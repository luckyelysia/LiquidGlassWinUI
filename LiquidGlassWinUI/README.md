# LiquidGlassWinUI

为 **WinUI 3 / Windows App SDK** 准备的「液态玻璃」(Liquid Glass) 画笔 —— 把 Apple 风格的玻璃材质叠加到控件背后的内容上。

`LiquidGlassBrush` 是一个 `XamlCompositionBrushBase`，内部管线为：

```
backdrop（背后的内容） → GaussianBlur（高斯模糊） → LiquidGlass（玻璃着色）
```

每一个材质参数都暴露为依赖属性 (DependencyProperty)，因此可以直接在 XAML 里赋值、绑定、甚至动画驱动，**无需任何 code-behind**。

> 仅支持 **x64**。底层原生运行时 `CustomEffectRuntimeNative.dll` 只编译为 x64。

---

## 目录

- [特性](#特性)
- [环境要求](#环境要求)
- [安装](#安装)
- [快速开始](#快速开始)
- [工作原理](#工作原理)
- [可调参数](#可调参数)
- [高级用法](#高级用法)
- [注意事项](#注意事项)
- [包结构与依赖](#包结构与依赖)

---

## 特性

- **纯 XAML 即可用**：所有材质参数都是依赖属性，赋值即生效。
- **完整材质模型**：折射（含色散与菲涅尔）、眩光（含双向高光与方向）、背景模糊、染色、圆角形状，共 19 个可调参数。
- **可动画**：因为是依赖属性，XAML `Storyboard` / 组合动画可直接驱动任一参数（例如让眩光缓慢旋转）。
- **优雅降级**：若着色器在当前 DWM 上编译/链接失败（例如超过 DWM 指令预算），画笔会**退化为透明**而不是抛异常崩溃宿主进程。
- **干净分发**：玻璃着色器以**嵌入资源**形式打包，不会在使用方输出目录里散落 `.hlsl` 文件；原生 DLL 通过 NuGet 自动就位。

---

## 环境要求

| 项 | 要求 |
|---|---|
| 目标框架 | `net8.0-windows10.0.19041.0` 及以上 |
| 应用类型 | WinUI 3（Windows App SDK）应用，打包或非打包均可 |
| 平台 | **仅 x64** |
| 传递依赖 | `Microsoft.WindowsAppSDK` 2.2.0、`Microsoft.Graphics.Win2D` 1.4.0（安装本包后自动引入） |

> 如果你的项目显式以 `x86` / `ARM` 为目标，构建会直接报错（`build/LiquidGlassWinUI.targets` 会在编译期拦截），避免运行时找不到原生 DLL。AnyCPU 项目可以引用本包，但应用必须运行在 x64 主机上。

---

## 安装

```xml
<ItemGroup>
  <PackageReference Include="LiquidGlassWinUI" Version="1.0.0" />
</ItemGroup>
```

引用后，XAML 里加上命名空间即可：

```xml
xmlns:lg="using:LiquidGlassWinUI"
```

---

## 快速开始

最典型的用法：在一个 `Rectangle` 上盖一层玻璃，让它模糊并「玻璃化」位于其下方的 UI 内容。

```xml
<Page x:Class="Demo.Page"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:lg="using:LiquidGlassWinUI">

  <Grid>
    <!-- 背景内容：画笔采样的是“画在它后面”的东西 -->
    <Image Source="/Assets/Background.jpg" Stretch="UniformToFill"/>

    <!-- 玻璃层 -->
    <Rectangle Width="320" Height="180"
               RadiusX="28" RadiusY="28"
               HorizontalAlignment="Center" VerticalAlignment="Center">
      <Rectangle.Fill>
        <lg:LiquidGlassBrush RefThickness="20"
                             RefFactor="1.4"
                             GlareFactor="90"
                             GlareAngle="-45"
                             BlurAmount="1.5"
                             TintA="0.08"
                             TintR="120" TintG="180" TintB="255"
                             ShapeRadius="0.4"
                             ShapeRoundness="5"/>
      </Rectangle.Fill>
    </Rectangle>
  </Grid>
</Page>
```

把任意几个参数省略，都会使用下文表格里的默认值。

> 玻璃的**圆角形状**由 `ShapeRadius` / `ShapeRoundness` 在着色器内部绘制，所以承载画笔的元素本身（上例的 `Rectangle`）用不用 `RadiusX/Y` 都可以；建议让元素保持矩形、把圆角交给画笔，避免双重裁剪。

---

## 工作原理

1. **采样背景**：画笔用 `CompositionBackdropBrush` 采样「在可视化树中画在该元素**之后、其下方**」的已渲染内容。注意：它采样的是**同一窗口内**位于该元素背后的内容，**不是**桌面壁纸或其它窗口。
2. **高斯模糊**：背景先经过 Win2D 原生 `GaussianBlurEffect`（参数 `BlurAmount` 驱动的是这一级，不是玻璃 cbuffer）。
3. **玻璃着色**：模糊后的结果进入 `LiquidGlassEffect`。该 effect 开启了 `FlattenSource` —— 这会让 DWM 把「模糊后的合成源」物化成一张真实纹理供玻璃采样器读取。没有这个标志，上游模糊会被融合成子片段，玻璃采样器就读不到它了。
4. **DPI 自适应**：画笔连接时自动测量系统 DPI（`GetDpiForSystem`），把每逻辑像素的物理像素数烘焙进初始 cbuffer（offset 124）。着色器据此缩放各条带的物理宽度，让参数值在任意缩放下都表现为「逻辑像素」量级。可用 `Dpr` 属性覆盖（见[高级用法](#高级用法)）。
5. **参数路由**：每个依赖属性变更时，按路径写入对应 effect 画笔的可动画标量属性（玻璃参数写入 `LiquidGlassEffect.<Key>`，模糊写入 `Blur.BlurAmount`）。

---

## 可调参数

所有参数均为 `double` 类型的依赖属性（`Dpr` 除外，它是 `float`）。下表中的「范围」是该参数在参考材质里的建议上下界，画笔本身不做钳制，超出范围也能用但效果未必理想。

### 折射 Refraction

| 属性 | 默认 | 范围 | 含义 |
|---|---|---|---|
| `RefThickness` | 20 | 1–80 | 折射边缘厚度（逻辑像素）。玻璃边缘把背景「掰弯」的强度，越大折射越明显。 |
| `RefFactor` | 1.4 | 1–4 | 折射率 IOR。决定背景被弯曲的程度，1 = 不折射，约 1.5 接近真实玻璃。 |
| `RefDispersion` | 7 | 0–50 | 色散。按波长把折射散开，产生彩虹色边缘；0 = 无色散。 |
| `RefFresnelRange` | 30 | 0–100 | 菲涅尔折射带的宽度（掠射角附近）。 |
| `RefFresnelHardness` | 20 | 0–100 | 菲涅尔折射带的衰减锐度。 |
| `RefFresnelFactor` | 20 | 0–100 | 菲涅尔折射项的强度系数。 |

### 眩光 Glare

| 属性 | 默认 | 范围 | 含义 |
|---|---|---|---|
| `GlareRange` | 30 | 0–100 | 镜面眩光光带的角宽度。 |
| `GlareHardness` | 20 | 0–100 | 眩光光带边缘的衰减锐度。 |
| `GlareFactor` | 90 | 0–120 | 主眩光高光的强度。 |
| `GlareConvergence` | 50 | 0–100 | 眩光向中心收敛的紧致程度。 |
| `GlareOppositeFactor` | 80 | 0–100 | 反向（次级）眩光的强度，模拟环境反射的对称高光。 |
| `GlareAngle` | -45 | -180–180 | 眩光光带的方向（度）。常用来配合「光源」朝向。 |

### 模糊 Blur

| 属性 | 默认 | 范围 | 含义 |
|---|---|---|---|
| `BlurAmount` | 1 | 0–5 | 背景模糊半径（像素）。驱动上游 Win2D `GaussianBlurEffect`，而非玻璃 cbuffer。 |

### 染色 Tint

| 属性 | 默认 | 范围 | 含义 |
|---|---|---|---|
| `TintR` / `TintG` / `TintB` | 255 | 0–255 | 染色的 RGB 通道。 |
| `TintA` | 0 | 0–1 | 染色不透明度。0 = 完全无色（纯折射/眩光），调高会叠上一层颜色滤镜。 |

### 形状 Shape

| 属性 | 默认 | 范围 | 含义 |
|---|---|---|---|
| `ShapeRadius` | 0.4 | 0–1 | 圆角半径，取短边半长 的 0–1 比例。 |
| `ShapeRoundness` | 5 | 2–7 | 超椭圆 (superellipse) 圆度指数 n。越大越接近「被削方的矩形」，越小越接近椭圆。 |

> 形状没有宽/高参数 —— 玻璃永远填满承载画笔的矩形（即控件本身）。

### DPI

| 属性 | 类型 | 默认 | 含义 |
|---|---|---|---|
| `Dpr` | `float` | 0 | 物理像素 / 逻辑像素。0 = 连接时自动测量系统 DPI；设为大于 0 的值则按该值使用。 |

---

## 高级用法

### 用动画驱动参数

因为是依赖属性，直接用 XAML `Storyboard` 动画即可（变更会通过 `OnParamChanged` 路由到 effect 画笔的可动画标量）：

```xml
<Page.Resources>
  <Storyboard x:Name="SpinGlare">
    <DoubleAnimation Storyboard.TargetName="Glass"
                     Storyboard.TargetProperty="GlareAngle"
                     From="-180" To="180"
                     Duration="0:0:8" RepeatBehavior="Forever"/>
  </Storyboard>
</Page.Resources>

<Grid>
  <Rectangle Fill="{x:Null}">
    <Rectangle.Fill>
      <lg:LiquidGlassBrush x:Name="Glass"
                           RefThickness="16" GlareFactor="100" GlareAngle="-45"/>
    </Rectangle.Fill>
  </Rectangle>
</Grid>
```

```csharp
// 启动 / 停止
SpinGlare.Begin();
SpinGlare.Stop();
```

### 在代码里创建并覆盖 DPI

需要固定 DPI（例如在多显示器、或自己管理缩放时）可在构造时设 `Dpr`：

```csharp
using LiquidGlassWinUI;

var glass = new LiquidGlassBrush
{
    Dpr = 2.0f,            // 固定为 200% 缩放，跳过自动测量
    RefThickness = 24,
    GlareFactor = 80,
    BlurAmount = 2.0,
    ShapeRadius = 0.5,
};
GlassRect.Fill = glass;
```

### 玻璃卡片 / 浮层模式

做「毛玻璃卡片」时，把玻璃矩形放在内容之上，并给矩形设置合适的圆角与边距。卡片本身的文字放在玻璃**之上**的更高一层：

```xml
<Grid>
  <Image Source="/Assets/Photo.jpg" Stretch="UniformToFill"/>

  <Rectangle Margin="24" RadiusX="32" RadiusY="32">
    <Rectangle.Fill>
      <lg:LiquidGlassBrush BlurAmount="2" TintA="0.06" ShapeRadius="0.45"/>
    </Rectangle.Fill>
  </Rectangle>

  <!-- 卡片内容：放在玻璃上方 -->
  <StackPanel Margin="40" VerticalAlignment="Center">
    <TextBlock Text="Liquid Glass" FontSize="28" Foreground="White"/>
    <TextBlock Text="叠加在玻璃之上的前景文字" Opacity="0.8"/>
  </StackPanel>
</Grid>
```

### 滚动内容上的玻璃

把玻璃矩形放在 `ScrollViewer` / `Grid` 的覆盖层（`Panel.ZIndex` 更高、且 `IsHitTestVisible` 按需设置），它就会实时模糊下方滚过的内容。

---

## 注意事项

- **必须 x64 运行**：原生 `CustomEffectRuntimeNative.dll` 只有 x64 版本。AnyCPU 应用请确保运行在 x64 主机上；显式 `x86`/`ARM` 目标会在构建期报错。
- **背后要有内容**：画笔采样的是同一窗口内位于该元素背后的已渲染内容。如果背后是空的或完全透明，玻璃会看不出效果（这不是 bug —— 玻璃本来就是「折射/反射背景」）。它**不会**捕获桌面壁纸或其它窗口。
- **静默降级，不崩溃**：若着色器超过当前 DWM 的指令预算（DWM 比 `ps_5_0` 更严），effect 会编译/链接失败，此时画笔退化为透明而不是抛异常。如果你发现画笔「没渲染」，优先怀疑这一点。
- **`BlurAmount` 增大会扩张中间纹理**：高斯模糊随半径增长需要更大的 padding；本画笔已通过 `samplerData` 让着色器重映射 UV，保证大模糊下玻璃仍居中，但极端大值会带来额外显存/性能开销。
- **每个画笔实例独立管线**：每实例都会构建自己的 effect 画笔。用于若干张卡片/面板没问题；若要在列表里给上千项各放一个，请评估性能。
- **非打包应用也能用**：原生 DLL 会被复制到应用输出目录的根目录（`DllImport` 按简单名解析的位置），打包或非打包应用均适用。
- **DPI**：默认自动测量主显示器 DPI（`GetDpiForSystem`）。多显示器下若需要按窗口所在屏幕的 DPI，用代码设置 `Dpr` 覆盖。

---

## 包结构与依赖

`LiquidGlassWinUI.1.0.0.nupkg` 内容：

```
lib/net8.0-windows10.0.19041.0/
    LiquidGlassWinUI.dll        托管程序集（公开类型仅 LiquidGlassBrush）
    LiquidGlassWinUI.xml        XML 文档注释
    LiquidGlassWinUI.pri
runtimes/win-x64/native/
    CustomEffectRuntimeNative.dll   原生 effect 运行时（x64）
build/
    LiquidGlassWinUI.targets        自动导入：把原生 DLL 复制到使用方输出目录，
                                    并在 x86/ARM 目标上提前报错
```

**传递依赖**（安装本包后自动引入）：

- `Microsoft.WindowsAppSDK` 2.2.0
- `Microsoft.Graphics.Win2D` 1.4.0
- `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2270

**公开 API**：只有 `LiquidGlassWinUI.LiquidGlassBrush`（命名空间 `LiquidGlassWinUI`）。所有 effect / 互操作 / 着色器加载基础设施均为 `internal`，不构成公开表面。

---

## 致谢

玻璃材质算法移植自 `liquid-glass-studio` 参考实现；自定义 effect 运行时基于 DWM 的 effect/linker 私有接口构建。
