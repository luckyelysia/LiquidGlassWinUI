# LiquidGlassWinUI

[Simplified Chinese](./README.zh-CN.md)

A WinUI 3 `XamlCompositionBrushBase` that renders an Apple-style liquid glass material
over backdrop content. Drop `<lg:LiquidGlassBrush />` into any element's `Background` and
get real-time refraction, chromatic dispersion, Fresnel rim lighting, specular glare,
tinted glass, bloom blending, and full colour adjustments -- all driven by dependency
properties that bind and animate directly from XAML with no code-behind required.

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

![Liquid glass preview](LiquidGlassWinUI/Assets/liquidglass.png)

## Why LiquidGlassWinUI

Modern desktop applications increasingly adopt translucent, depth-aware materials to
convey hierarchy and focus. WinUI 3 ships with `AcrylicBrush` and `MicaBackdrop`, but
neither provides the refractive depth, chromatic separation, or specular response of a
true glass material. LiquidGlassWinUI fills that gap.

At its core, the library registers custom HLSL pixel shaders directly into the DWM
composition pipeline -- no off-screen render targets, no CPU-side image processing. The
result is a compositor-native effect that runs at full frame rate, reads the live
backdrop, and exposes every material parameter as an animatable `DependencyProperty`.

## Installation

```powershell
dotnet add package LiquidGlassWinUI
```

**Requirements:**
- Windows 11 x64, or Windows 10 x64 (build 19041 or later)
- .NET 8

> [!IMPORTANT]
> **Windows App SDK 2.2.0** is a hard requirement. The native IAT hook targets
> specific internal symbols in `wuceffectsi.dll` that ship with this version;
> earlier or later releases have different binary layouts and the hook will fail
> to install.
> **x64 process only.** The native runtime (`CustomEffectRuntimeNative.dll`) has no x86 or ARM64 build.

## Quick Start

### 1. Add the namespace

```xml
xmlns:lg="using:LiquidGlassWinUI"
```

### 2. Apply the brush

Set `Background` on any `Border` or `Panel` (Grid, StackPanel, etc.):

```xml
<Grid>
  <!-- Your backdrop content goes here -->
  <Image Source="Assets/background.jpg" Stretch="UniformToFill"/>

  <!-- Glass panel -->
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

The brush reads whatever is visually behind the element through the WinUI compositor
backdrop mechanism. Content behind the `Border` is refracted and blurred; the glass
material is drawn only within the element's bounds, and child content (like the
`TextBlock` above) renders on top.

### 3. Tune the look

Every parameter is a `DependencyProperty`. Bind them to view-model properties or animate
them with visual states:

```xml
<lg:LiquidGlassBrush RefThickness="{x:Bind ViewModel.GlassThickness, Mode=OneWay}"
                     GlareAngle="{x:Bind ViewModel.LightAngle, Mode=OneWay}"
                     TintA="0.15"
                     TintR="180"
                     TintG="200"
                     TintB="255"/>
```

### 4. Check for errors

If the effect pipeline fails to compile (for example, the shader exceeds DWM limits or
the native hook cannot install), the brush falls back to a solid red fill as a loud
visual indicator, instead of crashing the application. Read the diagnostic message from:

```csharp
if (LiquidGlassBrush.LastError is not null)
    Debug.WriteLine(LiquidGlassBrush.LastError);
```

## Parameters

All 28 material parameters are registered as `DependencyProperty` with factory-pooled
animatable paths. Grouped by function:

### Refraction

| Property | Default | Range | Description |
|---|---|---|---|
| `RefThickness` | 20 | 1--80 | Edge thickness in logical pixels where refraction is strongest |
| `RefFactor` | 1.4 | 1--4 | Index of refraction -- higher values bend the backdrop more aggressively |
| `RefDispersion` | 7 | 0--50 | Chromatic dispersion strength (separates colour channels by wavelength) |
| `DispersionRange` | 1.0 | 0--1 | How far dispersion propagates inward from the edge (0 = edge only, 1 = full interior) |
| `RefFresnelRange` | 30 | 0--100 | Angular width of the Fresnel refraction band near grazing angles |
| `RefFresnelHardness` | 20 | 0--100 | Falloff sharpness of the Fresnel band |
| `RefFresnelFactor` | 20 | 0--100 | Intensity multiplier for the Fresnel rim highlight |
| `Magnification` | 1.0 | 1--3 | Backdrop scale factor centred on the glass (1.0 = identity, cannot go below 1) |

### Glare

| Property | Default | Range | Description |
|---|---|---|---|
| `GlareRange` | 30 | 0--100 | Angular width of the specular glare streak |
| `GlareHardness` | 20 | 0--100 | Falloff sharpness of the glare streak |
| `GlareFactor` | 90 | 0--100 | Primary glare intensity |
| `GlareConvergence` | 50 | 0--100 | How tightly glare converges toward its centre |
| `GlareOppositeFactor` | 80 | 0--100 | Secondary (opposite-direction) glare intensity |
| `GlareAngle` | -45 | -- | Glare streak direction in degrees |

### Blur

| Property | Default | Range | Description |
|---|---|---|---|
| `BlurAmount` | 1.0 | 0+ | Backdrop blur radius in logical pixels. Set to 0 to bypass the blur passes entirely; the glass still refracts the sharp backdrop. |

### Tint

Tint is multiplied into the refracted colour inside the glass shader. All channels
default to 255 (white = no tint) with alpha at 0 (fully transparent tint).

| Property | Default | Range | Description |
|---|---|---|---|
| `TintR` | 255 | 0--255 | Red channel of the glass tint |
| `TintG` | 255 | 0--255 | Green channel of the glass tint |
| `TintB` | 255 | 0--255 | Blue channel of the glass tint |
| `TintA` | 0 | 0--1 | Tint opacity (0 = clear, 1 = fully opaque tint) |

### Post-Processing

Bloom blending and colour adjustments run in a dedicated post-processing stage between
the blur passes and the glass shader. All parameters are animatable via compositor-thread
`ScalarKeyFrameAnimation`.

| Property | Default | Range | Description |
|---|---|---|---|
| `BloomAmount` | 1 | 0--1 | Cross-fade between sharp and blurred backdrop: 0 = fully sharp (raw backdrop), 1 = fully blurred |
| `Exposure` | 1.0 | 0.5--2 | Multiplicative brightness gain |
| `Brightness` | 0 | -1--1 | Additive brightness offset (negative = darker, positive = brighter) |
| `Contrast` | 1.0 | 0--2 | Contrast multiplier around mid-grey (1.0 = unchanged) |
| `Saturation` | 1.0 | 0--2 | Colour saturation (0 = greyscale, 1 = unchanged, 2 = oversaturated) |
| `Temperature` | 0 | -1--1 | Colour temperature shift (negative = cooler/blue, positive = warmer/yellow) |
| `Vibrance` | 0 | 0--1 | Smart vibrance boost that targets low-saturation regions while preserving skin tones |

### Shape

| Property | Default | Range | Description |
|---|---|---|---|
| `ShapeRadius` | 0.4 | 0--1 | Corner radius as a fraction of the shorter half-side |
| `ShapeRoundness` | 5 | 2--8 | Superellipse roundness exponent (higher = squarer corners) |

### Diagnostics

| Member | Description |
|---|---|
| `LastError` (static string) | If the effect fails to compile or link, the error message is written here instead of throwing. |

## Animation

Since every material parameter is a `DependencyProperty`, standard XAML `Storyboard`
animations work directly against a named brush. No code-behind is needed to drive the
material.

### XAML Storyboard (preferred)

Target the brush by name and animate any parameter with `DoubleAnimation`:

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

  <!-- GlareAngle is periodic: 0-to-360 loops seamlessly
       instead of the pendulum AutoReverse gives -->
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
> `EnableDependentAnimation="True"` is required on every `DoubleAnimation` that
> targets a custom dependency property. WinUI disables dependent animations by
> default; without this flag the animation silently does nothing.

Control the storyboard from code-behind with the standard `Begin`/`Pause`/`Resume`/`Stop`
methods, and adjust `SpeedRatio` for variable playback speed.

### C# animation API

For programmatic use, the brush also exposes two methods:

**Per-property animation** -- `AnimateScalar` runs entirely on the compositor thread
with cubic ease-out:

```csharp
myBrush.AnimateScalar("Exposure", 1.5f, durationMs: 800);
myBrush.AnimateScalar("GlareAngle", 45f, durationMs: 1200);
```

**Batch transition** -- `TransitionTo` animates every parameter toward the values of
another brush instance in a single compositor frame via `CompositionCommitBatch`:

```csharp
var target = new LiquidGlassBrush
{
    BlurAmount = 4.0,
    GlareFactor = 60,
    TintA = 0.3,
};
currentBrush.TransitionTo(target, durationMs: 600);
```

After the batch completes, the source brush's dependency properties are synchronised
to the target values so a later reconnect or external `SetValue` does not overwrite
the animation endpoint.

### Code-built Storyboard

You can also construct a `Storyboard` entirely in C# -- same `EnableDependentAnimation`
requirement applies:

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

## Architecture

### Effect pipeline

```
backdrop --> BlurH --> BlurV --> PostProcessing --> LiquidGlassEffect
                |                       |
                |   BlurAmount drives   |   BloomAmount cross-fades
                |   H and V passes      |   between blurred and raw
```

- **BlurH / BlurV** -- 1D separable Gaussian blur (bilinear-merged, 21 taps folded into
  10 sample pairs). `BlurAmount` drives both passes; setting it to 0 disconnects the blur
  chain and routes the raw backdrop into the post-processing stage.
- **PostProcessing** -- Bloom cross-fade (`Backdrop` vs `RawBackdrop`) plus seven colour
  adjustments (exposure, brightness, contrast, saturation, temperature, vibrance). Runs
  even when blur is bypassed.
- **LiquidGlassEffect** -- The main glass material: signed-distance-field shape with
  superellipse corners, normal derivation, refraction via backdrop UV offset, chromatic
  dispersion (three wavelength samples), Fresnel rim, dual specular glare lobes, tint
  multiplication, and 4x MSAA edge anti-aliasing.

### CustomEffectRuntime

The effect nodes in the pipeline are not built-in Win2D effects. They are custom HLSL
pixel shaders registered through `CustomEffectRuntimeNative.dll`, a C++/WinRT library
that patches the DWM composition pipeline at runtime:

1. **IAT hook on `wuceffectsi.dll`** intercepts `EffectType::FromGuid` and
   `CompileEffectDescription`, making custom effect GUIDs appear as legitimate effect
   types to the compositor.
2. **`RuntimeGraphicsEffect`** implements `IGraphicsEffect` and
   `IGraphicsEffectD2D1Interop`, which `Compositor::CreateEffectFactory` accepts as a
   standard effect node.
3. **Shader linking** returns a synthetic `CompiledResult` containing the custom HLSL
   bytecode, entry points, sampler bindings, and cbuffer layouts when DWM traverses the
   effect graph.

The C# layer (`Effects/CustomEffectBase.cs`, `Interop/CustomEffectBuilder.cs`,
`Interop/CustomEffectInterop.cs`) assembles effect definitions from embedded HLSL
resources and marshals them to the native runtime via P/Invoke. Shaders are embedded as
`<EmbeddedResource>` in the assembly -- no loose files in the consumer's output
directory.

### Factory pooling

`CompositionEffectFactory` registers animatable properties in the compositor's global
tracking table, which has a hard cap of 256 aggregate entries. Creating new factories on
every brush connect/disconnect cycle would exhaust this budget under page navigation.
Instead, factories are created once and stored statically; only per-instance brushes are
disposed. The cap is not a concern for consumers.

## Project structure

| Directory | Role |
|---|---|
| `LiquidGlassWinUI/` | C# class library -- `LiquidGlassBrush` and its effect pipeline. Ships as a NuGet package. |
| `LiquidGlassDemo/` | WinUI 3 demo application with interactive parameter sliders, live preview, and XAML/C# snippet export. |
| `Native/` | C++/WinRT DLL that patches custom HLSL shaders into the DWM composition pipeline. |
| `BlendProbe/` | Internal research sandbox with 26+ test shaders for probing DWM effect and linker behaviour. Not part of the shipped library. |

Solution file: `LiquidGlassWinUI.slnx` (requires Visual Studio 2022 or later, or the
`dotnet` CLI with `.slnx` support).

## Building from source

### Prerequisites

- Windows 11 x64 or Windows 10 x64 (build 19041+)
- Visual Studio 2022 with the Windows App SDK workload
- .NET 8 SDK

### 1. Build the native runtime

```powershell
msbuild Native/CustomEffectRuntime.Native.vcxproj /p:Configuration=Release /p:Platform=x64
```

This produces `Native/Output/x64/Release/CustomEffectRuntimeNative.dll`. The managed
`.csproj` references this path; the NuGet pack target checks that it exists.

### 2. Build the managed projects

```powershell
dotnet build LiquidGlassWinUI/LiquidGlassWinUI.csproj -c Release
dotnet build LiquidGlassDemo/LiquidGlassDemo.csproj -c Release
```

Or open `LiquidGlassWinUI.slnx` in Visual Studio and build the entire solution.

### Project references (without NuGet)

> [!NOTE]
> MSBuild does not transitively copy native DLLs from `<ProjectReference>`. If you
> reference `LiquidGlassWinUI.csproj` directly rather than consuming the NuGet package,
> add this to your project after building the native DLL:
>
> ```xml
> <Content Include="path\to\Native\Output\x64\Release\CustomEffectRuntimeNative.dll"
>          CopyToOutputDirectory="PreserveNewest"
>          Link="CustomEffectRuntimeNative.dll"
>          Visible="false" />
> ```

## Error handling and diagnostics

> [!WARNING]
> When the effect pipeline cannot compile or link -- whether because the HLSL
> shader exceeds the DWM profile budget (512 instruction slots,
> `ps_4_0_level_9_3` target), or the native IAT hook cannot be installed -- the
> brush sets its `CompositionBrush` to a solid red fill instead of throwing an
> exception. It is not subtle, but it will not crash the host application. The
> full error message and stack trace are available on the static
> `LiquidGlassBrush.LastError` property.

## BlendProbe

`BlendProbe/` is an internal research tool kept separate from the shipping library. It
contains 26+ HLSL test shaders and corresponding effect definitions used to
systematically map DWM effect and linker behaviour: multi-source topologies, sampler
types, flatten modes, and shader argument combinations. New effect patterns are verified
in this sandbox before being promoted into the production `LiquidGlassWinUI` library.
Contributors working on shader extensions should start here.

## Contributing

Contributions are welcome. Areas where help is especially valuable:

- **ARM64 support** -- The native runtime (`CustomEffectRuntimeNative.dll`) is currently
  x64-only. Porting the IAT hook and `RuntimeGraphicsEffect` to ARM64 would expand
  platform coverage to Snapdragon X devices.
- **Additional glass primitives** -- The current glass shape is a superellipse. Adding
  arbitrary SDF shapes (capsules, stars, text paths) would broaden use cases.
- **Performance profiling** -- Characterising the compositor-side cost of multiple
  simultaneous glass brushes and optimising the separable blur kernel.
- **Demo applications** -- Real-world usage examples that show the material integrated
  into navigation views, media players, or settings panes.

Before starting significant work, please open an issue to discuss the approach. The
`BlendProbe/` sandbox is the recommended starting point for shader-level experimentation.

## Related projects

- [WUILiquidGlassDemo](https://github.com/apkipa/WUILiquidGlassDemo) -- Pioneering
  reverse-engineering of the Windows App SDK composition pipeline and the IAT hook
  technique that makes custom effect registration possible.
- [liquid-glass-studio](https://github.com/iyinchao/liquid-glass-studio) -- Reference
  WebGL implementation of the liquid glass shader, including the signed-distance-field
  shape, smooth-min blending, and specular glare model that inspired the HLSL port in
  this project.

## Acknowledgements

This project builds directly on the work of others in the Windows composition
reverse-engineering and real-time glass rendering communities:

- **apkipa** for [WUILiquidGlassDemo](https://github.com/apkipa/WUILiquidGlassDemo), the
  first public demonstration that custom HLSL effects could be injected into the DWM
  composition graph. The IAT hook strategy, `RuntimeGraphicsEffect` pattern, and
  P/Invoke marshalling in this project are derived from that work.
- **iyinchao** for [liquid-glass-studio](https://github.com/iyinchao/liquid-glass-studio),
  the reference shader whose SDF shape model, chromatic dispersion sampling, Fresnel
  formulation, and dual-lobe glare model were ported to HLSL for the Windows compositor.
- Everyone who contributed bug reports, tested preview builds, or pushed the limits of
  what the DWM effect linker would accept.

## License

MIT -- see the [LICENSE](LICENSE) file.
