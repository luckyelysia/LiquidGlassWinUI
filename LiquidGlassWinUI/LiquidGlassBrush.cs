using System;
using System.Collections.Generic;
using System.Linq;
using LiquidGlassWinUI.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LiquidGlassWinUI
{
    /// <summary>
    /// A XAML composition brush that renders an Apple-style "liquid glass" material
    /// over whatever is behind the element it fills (the backdrop).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brush owns the pipeline
    /// <c>backdrop -&gt; BlurH -&gt; BlurV -&gt; LiquidGlassEffect</c> (two 1D
    /// separable blur passes + glass) and exposes every material parameter of the
    /// glass effect as a <see cref="DependencyProperty"/>, so each one can be bound
    /// — and animated — directly from XAML with no code-behind. Set it as any
    /// element's <c>Fill</c>/<c>Background</c> (or use a <c>Rectangle</c> overlay)
    /// and let it read the backdrop through it.
    /// </para>
    /// <para>
    /// The glass effect has <c>FlattenSource</c> enabled, so DWM materializes the
    /// backdrop into a real texture the glass sampler reads. All material parameters
    /// are in logical pixels (the glass is DPI-agnostic), so no code-behind is required.
    /// </para>
    /// <para>
    /// If the effect fails to compile/link (for example, if the shader is too complex
    /// for the current DWM), the brush degrades to transparent instead of throwing,
    /// so it never crashes the host app.
    /// </para>
    /// <para>
    /// Requires an x64 process: the underlying native runtime is x64-only.
    /// </para>
    /// <example>
    /// <code>
    /// xmlns:lg="using:LiquidGlassWinUI"
    /// ...
    /// &lt;Rectangle Fill="{x:Null}"&gt;
    ///   &lt;Rectangle.Background&gt;
    ///     &lt;lg:LiquidGlassBrush RefThickness="20"
    ///                          GlareFactor="90"
    ///                          BlurAmount="1.5"
    ///                          TintA="0.1"
    ///                          ShapeRadius="0.4"/&gt;
    ///   &lt;/Rectangle.Background&gt;
    /// &lt;/Rectangle&gt;
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class LiquidGlassBrush : XamlCompositionBrushBase
    {
        /// <summary>
        /// Which effect brush a parameter is routed to: the glass cbuffer or the
        /// post-processing effect (bloom + colour adjustments). <see cref="BlurAmount"/>
        /// is handled separately — it drives the H/V blur passes and toggles bypass,
        /// so it never goes through <see cref="ApplyParam"/>.
        /// </summary>
        private enum ParamTarget { Glass, PostProcess }

        // ---- dependency properties: one per material parameter ----

        // ---- Refraction ----

        /// <summary>Backing dependency property for <see cref="RefThickness"/>.</summary>
        public static readonly DependencyProperty RefThicknessProperty =
            DependencyProperty.Register(
                nameof(RefThickness), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefThickness").Default,
                    OnRefThicknessChanged));
        /// <summary>Refraction edge thickness, in logical pixels (default 20).</summary>
        public double RefThickness { get => (double)GetValue(RefThicknessProperty); set => SetValue(RefThicknessProperty, value); }
        private static void OnRefThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefThickness",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="RefFactor"/>.</summary>
        public static readonly DependencyProperty RefFactorProperty =
            DependencyProperty.Register(
                nameof(RefFactor), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefFactor").Default,
                    OnRefFactorChanged));
        /// <summary>Index of refraction driving how strongly the backdrop is bent (default 1.4).</summary>
        public double RefFactor { get => (double)GetValue(RefFactorProperty); set => SetValue(RefFactorProperty, value); }
        private static void OnRefFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefFactor",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="RefDispersion"/>.</summary>
        public static readonly DependencyProperty RefDispersionProperty =
            DependencyProperty.Register(
                nameof(RefDispersion), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefDispersion").Default,
                    OnRefDispersionChanged));
        /// <summary>Chromatic dispersion spreading the refraction by wavelength (default 7).</summary>
        public double RefDispersion { get => (double)GetValue(RefDispersionProperty); set => SetValue(RefDispersionProperty, value); }
        private static void OnRefDispersionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefDispersion",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="DispersionRange"/>.</summary>
        public static readonly DependencyProperty DispersionRangeProperty =
            DependencyProperty.Register(
                nameof(DispersionRange), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "DispersionRange").Default,
                    OnDispersionRangeChanged));
        /// <summary>Scales chromatic dispersion: 0 = no dispersion (single UV sample), 1 = full (default 1.0).</summary>
        public double DispersionRange { get => (double)GetValue(DispersionRangeProperty); set => SetValue(DispersionRangeProperty, value); }
        private static void OnDispersionRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".DispersionRange",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="RefFresnelRange"/>.</summary>
        public static readonly DependencyProperty RefFresnelRangeProperty =
            DependencyProperty.Register(
                nameof(RefFresnelRange), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefFresnelRange").Default,
                    OnRefFresnelRangeChanged));
        /// <summary>Width of the Fresnel refraction band near grazing angles (default 30).</summary>
        public double RefFresnelRange { get => (double)GetValue(RefFresnelRangeProperty); set => SetValue(RefFresnelRangeProperty, value); }
        private static void OnRefFresnelRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefFresnelRange",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="RefFresnelHardness"/>.</summary>
        public static readonly DependencyProperty RefFresnelHardnessProperty =
            DependencyProperty.Register(
                nameof(RefFresnelHardness), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefFresnelHardness").Default,
                    OnRefFresnelHardnessChanged));
        /// <summary>Hardness (sharpness) of the Fresnel refraction band falloff (default 20).</summary>
        public double RefFresnelHardness { get => (double)GetValue(RefFresnelHardnessProperty); set => SetValue(RefFresnelHardnessProperty, value); }
        private static void OnRefFresnelHardnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefFresnelHardness",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="RefFresnelFactor"/>.</summary>
        public static readonly DependencyProperty RefFresnelFactorProperty =
            DependencyProperty.Register(
                nameof(RefFresnelFactor), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "RefFresnelFactor").Default,
                    OnRefFresnelFactorChanged));
        /// <summary>Strength multiplier applied to the Fresnel refraction term (default 20).</summary>
        public double RefFresnelFactor { get => (double)GetValue(RefFresnelFactorProperty); set => SetValue(RefFresnelFactorProperty, value); }
        private static void OnRefFresnelFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".RefFresnelFactor",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Magnification"/>.</summary>
        public static readonly DependencyProperty MagnificationProperty =
            DependencyProperty.Register(
                nameof(Magnification), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "Magnification").Default,
                    OnMagnificationChanged));
        /// <summary>Backdrop zoom factor centered on the glass: 1.0 = none, >1 = zoom in. Cannot go below 1 — sampling outside the backdrop content rect would read void.</summary>
        public double Magnification { get => (double)GetValue(MagnificationProperty); set => SetValue(MagnificationProperty, value); }
        private static void OnMagnificationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".Magnification",
                (float)(double)e.NewValue);

        // ---- Glare ----

        /// <summary>Backing dependency property for <see cref="GlareRange"/>.</summary>
        public static readonly DependencyProperty GlareRangeProperty =
            DependencyProperty.Register(
                nameof(GlareRange), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareRange").Default,
                    OnGlareRangeChanged));
        /// <summary>Angular width of the specular glare streak (default 30).</summary>
        public double GlareRange { get => (double)GetValue(GlareRangeProperty); set => SetValue(GlareRangeProperty, value); }
        private static void OnGlareRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareRange",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="GlareHardness"/>.</summary>
        public static readonly DependencyProperty GlareHardnessProperty =
            DependencyProperty.Register(
                nameof(GlareHardness), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareHardness").Default,
                    OnGlareHardnessChanged));
        /// <summary>Hardness (sharpness) of the glare streak falloff (default 20).</summary>
        public double GlareHardness { get => (double)GetValue(GlareHardnessProperty); set => SetValue(GlareHardnessProperty, value); }
        private static void OnGlareHardnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareHardness",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="GlareFactor"/>.</summary>
        public static readonly DependencyProperty GlareFactorProperty =
            DependencyProperty.Register(
                nameof(GlareFactor), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareFactor").Default,
                    OnGlareFactorChanged));
        /// <summary>Intensity of the glare highlight (default 90).</summary>
        public double GlareFactor { get => (double)GetValue(GlareFactorProperty); set => SetValue(GlareFactorProperty, value); }
        private static void OnGlareFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareFactor",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="GlareConvergence"/>.</summary>
        public static readonly DependencyProperty GlareConvergenceProperty =
            DependencyProperty.Register(
                nameof(GlareConvergence), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareConvergence").Default,
                    OnGlareConvergenceChanged));
        /// <summary>How tightly the glare converges toward its center (default 50).</summary>
        public double GlareConvergence { get => (double)GetValue(GlareConvergenceProperty); set => SetValue(GlareConvergenceProperty, value); }
        private static void OnGlareConvergenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareConvergence",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="GlareOppositeFactor"/>.</summary>
        public static readonly DependencyProperty GlareOppositeFactorProperty =
            DependencyProperty.Register(
                nameof(GlareOppositeFactor), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareOppositeFactor").Default,
                    OnGlareOppositeFactorChanged));
        /// <summary>Intensity of the secondary, opposite-facing glare highlight (default 80).</summary>
        public double GlareOppositeFactor { get => (double)GetValue(GlareOppositeFactorProperty); set => SetValue(GlareOppositeFactorProperty, value); }
        private static void OnGlareOppositeFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareOppositeFactor",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="GlareAngle"/>.</summary>
        public static readonly DependencyProperty GlareAngleProperty =
            DependencyProperty.Register(
                nameof(GlareAngle), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "GlareAngle").Default,
                    OnGlareAngleChanged));
        /// <summary>Direction of the glare streak, in degrees (default -45).</summary>
        public double GlareAngle { get => (double)GetValue(GlareAngleProperty); set => SetValue(GlareAngleProperty, value); }
        private static void OnGlareAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".GlareAngle",
                (float)(double)e.NewValue);

        // ---- Blur (special: drives the H/V separable blur passes, not a cbuffer slot) ----

        /// <summary>Backing dependency property for <see cref="BlurAmount"/>.</summary>
        public static readonly DependencyProperty BlurAmountProperty =
            DependencyProperty.Register(
                nameof(BlurAmount), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(1.0, OnBlurAmountChanged));
        /// <summary>
        /// Backdrop blur radius in pixels (default 1). Drives the separable H/V 1D
        /// blur passes upstream of the glass. At ≤ 0 the blur chain is bypassed.
        /// </summary>
        public double BlurAmount { get => (double)GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }
        private static void OnBlurAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyBlurAmount((float)(double)e.NewValue);

        // ---- Tint ----

        /// <summary>Backing dependency property for <see cref="TintR"/>.</summary>
        public static readonly DependencyProperty TintRProperty =
            DependencyProperty.Register(
                nameof(TintR), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "TintR").Default,
                    OnTintRChanged));
        /// <summary>Red channel of the glass tint, 0–255 (default 255).</summary>
        public double TintR { get => (double)GetValue(TintRProperty); set => SetValue(TintRProperty, value); }
        private static void OnTintRChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".TintR",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="TintG"/>.</summary>
        public static readonly DependencyProperty TintGProperty =
            DependencyProperty.Register(
                nameof(TintG), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "TintG").Default,
                    OnTintGChanged));
        /// <summary>Green channel of the glass tint, 0–255 (default 255).</summary>
        public double TintG { get => (double)GetValue(TintGProperty); set => SetValue(TintGProperty, value); }
        private static void OnTintGChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".TintG",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="TintB"/>.</summary>
        public static readonly DependencyProperty TintBProperty =
            DependencyProperty.Register(
                nameof(TintB), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "TintB").Default,
                    OnTintBChanged));
        /// <summary>Blue channel of the glass tint, 0–255 (default 255).</summary>
        public double TintB { get => (double)GetValue(TintBProperty); set => SetValue(TintBProperty, value); }
        private static void OnTintBChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".TintB",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="TintA"/>.</summary>
        public static readonly DependencyProperty TintAProperty =
            DependencyProperty.Register(
                nameof(TintA), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "TintA").Default,
                    OnTintAChanged));
        /// <summary>Alpha (opacity) of the glass tint, 0–1 (default 0 = untinted).</summary>
        public double TintA { get => (double)GetValue(TintAProperty); set => SetValue(TintAProperty, value); }
        private static void OnTintAChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".TintA",
                (float)(double)e.NewValue);

        // Exposure moved to PostProcessingEffect.

        // ---- Bloom (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="BloomAmount"/>.</summary>
        public static readonly DependencyProperty BloomAmountProperty =
            DependencyProperty.Register(
                nameof(BloomAmount), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(1.0, OnBloomAmountChanged));
        /// <summary>Cross-fade between blurred and raw backdrop: 0 = pure blurred glass (default), 1 = fully sharp.</summary>
        public double BloomAmount { get => (double)GetValue(BloomAmountProperty); set => SetValue(BloomAmountProperty, value); }
        private static void OnBloomAmountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.BloomAmountPropertyPath,
                (float)(double)e.NewValue);

        // ---- Colour Adjustments (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="Brightness"/>.</summary>
        public static readonly DependencyProperty BrightnessProperty =
            DependencyProperty.Register(
                nameof(Brightness), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(0.0, OnBrightnessChanged));
        /// <summary>Additive brightness: -1 = fully dark, 0 = unchanged, +1 = fully bright.</summary>
        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
        private static void OnBrightnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.BrightnessPropertyPath,
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Contrast"/>.</summary>
        public static readonly DependencyProperty ContrastProperty =
            DependencyProperty.Register(
                nameof(Contrast), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(1.0, OnContrastChanged));
        /// <summary>Contrast multiplier around mid-grey: 0 = flat grey, 1 = unchanged, 2 = doubled.</summary>
        public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }
        private static void OnContrastChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.ContrastPropertyPath,
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Saturation"/>.</summary>
        public static readonly DependencyProperty SaturationProperty =
            DependencyProperty.Register(
                nameof(Saturation), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(1.0, OnSaturationChanged));
        /// <summary>Saturation multiplier: 0 = greyscale, 1 = unchanged, 2 = oversaturated.</summary>
        public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
        private static void OnSaturationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.SaturationPropertyPath,
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Temperature"/>.</summary>
        public static readonly DependencyProperty TemperatureProperty =
            DependencyProperty.Register(
                nameof(Temperature), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(0.0, OnTemperatureChanged));
        /// <summary>Colour temperature shift: -1 = cool (blue), 0 = unchanged, +1 = warm (yellow/red).</summary>
        public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
        private static void OnTemperatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.TemperaturePropertyPath,
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Exposure"/> (PostProcessingEffect).</summary>
        public static readonly DependencyProperty ExposureProperty =
            DependencyProperty.Register(
                nameof(Exposure), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(1.0, OnExposureChanged));
        /// <summary>Exposure gain: 0.5 = half brightness, 1 = unchanged, 2 = double brightness.</summary>
        public double Exposure { get => (double)GetValue(ExposureProperty); set => SetValue(ExposureProperty, value); }
        private static void OnExposureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.ExposurePropertyPath,
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="Vibrance"/>.</summary>
        public static readonly DependencyProperty VibranceProperty =
            DependencyProperty.Register(
                nameof(Vibrance), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(0.0, OnVibranceChanged));
        /// <summary>Smart vibrance boost targeting low-saturation regions: 0 = off, 1 = full.</summary>
        public double Vibrance { get => (double)GetValue(VibranceProperty); set => SetValue(VibranceProperty, value); }
        private static void OnVibranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.PostProcess, PostProcessingEffect.VibrancePropertyPath,
                (float)(double)e.NewValue);

        // ---- Shape ----

        /// <summary>Backing dependency property for <see cref="ShapeRadius"/>.</summary>
        public static readonly DependencyProperty ShapeRadiusProperty =
            DependencyProperty.Register(
                nameof(ShapeRadius), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "ShapeRadius").Default,
                    OnShapeRadiusChanged));
        /// <summary>Corner radius as a 0–1 fraction of the shorter half-side (default 0.4).</summary>
        public double ShapeRadius { get => (double)GetValue(ShapeRadiusProperty); set => SetValue(ShapeRadiusProperty, value); }
        private static void OnShapeRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".ShapeRadius",
                (float)(double)e.NewValue);

        /// <summary>Backing dependency property for <see cref="ShapeRoundness"/>.</summary>
        public static readonly DependencyProperty ShapeRoundnessProperty =
            DependencyProperty.Register(
                nameof(ShapeRoundness), typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(
                    (double)LiquidGlassEffect.Params.First(p => p.Key == "ShapeRoundness").Default,
                    OnShapeRoundnessChanged));
        /// <summary>Superellipse roundness exponent n (default 5).</summary>
        public double ShapeRoundness { get => (double)GetValue(ShapeRoundnessProperty); set => SetValue(ShapeRoundnessProperty, value); }
        private static void OnShapeRoundnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((LiquidGlassBrush)d).ApplyParam(
                ParamTarget.Glass, LiquidGlassEffect.EffectNameValue + ".ShapeRoundness",
                (float)(double)e.NewValue);

        // ---- parameter registry ----
        //
        // Every routine parameter (glass cbuffer + post-processing) in one place, each
        // with its full animatable path (effect prefix already baked in via const
        // concatenation / the effect's own *PropertyPath constants, so there is no
        // runtime string concatenation). Used to push initial values in OnConnected,
        // to animate them in TransitionTo, and to look up by key in AnimateScalar.
        //
        // BlurAmount is deliberately ABSENT: it targets the H/V blur brushes and toggles
        // bypass, so it is routed through ApplyBlurAmount, never ApplyParam.
        private static readonly (DependencyProperty dp, ParamTarget target, string key, string path)[] s_params =
        {
            // ---- Refraction ----
            (RefThicknessProperty,        ParamTarget.Glass,       "RefThickness",        LiquidGlassEffect.EffectNameValue + ".RefThickness"),
            (RefFactorProperty,           ParamTarget.Glass,       "RefFactor",           LiquidGlassEffect.EffectNameValue + ".RefFactor"),
            (RefDispersionProperty,       ParamTarget.Glass,       "RefDispersion",       LiquidGlassEffect.EffectNameValue + ".RefDispersion"),
            (DispersionRangeProperty,     ParamTarget.Glass,       "DispersionRange",     LiquidGlassEffect.EffectNameValue + ".DispersionRange"),
            (RefFresnelRangeProperty,     ParamTarget.Glass,       "RefFresnelRange",     LiquidGlassEffect.EffectNameValue + ".RefFresnelRange"),
            (RefFresnelHardnessProperty,  ParamTarget.Glass,       "RefFresnelHardness",  LiquidGlassEffect.EffectNameValue + ".RefFresnelHardness"),
            (RefFresnelFactorProperty,    ParamTarget.Glass,       "RefFresnelFactor",    LiquidGlassEffect.EffectNameValue + ".RefFresnelFactor"),
            (MagnificationProperty,       ParamTarget.Glass,       "Magnification",       LiquidGlassEffect.EffectNameValue + ".Magnification"),
            // ---- Glare ----
            (GlareRangeProperty,          ParamTarget.Glass,       "GlareRange",          LiquidGlassEffect.EffectNameValue + ".GlareRange"),
            (GlareHardnessProperty,       ParamTarget.Glass,       "GlareHardness",       LiquidGlassEffect.EffectNameValue + ".GlareHardness"),
            (GlareFactorProperty,         ParamTarget.Glass,       "GlareFactor",         LiquidGlassEffect.EffectNameValue + ".GlareFactor"),
            (GlareConvergenceProperty,    ParamTarget.Glass,       "GlareConvergence",    LiquidGlassEffect.EffectNameValue + ".GlareConvergence"),
            (GlareOppositeFactorProperty, ParamTarget.Glass,       "GlareOppositeFactor", LiquidGlassEffect.EffectNameValue + ".GlareOppositeFactor"),
            (GlareAngleProperty,          ParamTarget.Glass,       "GlareAngle",          LiquidGlassEffect.EffectNameValue + ".GlareAngle"),
            // ---- Tint ----
            (TintRProperty,               ParamTarget.Glass,       "TintR",               LiquidGlassEffect.EffectNameValue + ".TintR"),
            (TintGProperty,               ParamTarget.Glass,       "TintG",               LiquidGlassEffect.EffectNameValue + ".TintG"),
            (TintBProperty,               ParamTarget.Glass,       "TintB",               LiquidGlassEffect.EffectNameValue + ".TintB"),
            (TintAProperty,               ParamTarget.Glass,       "TintA",               LiquidGlassEffect.EffectNameValue + ".TintA"),
            // ---- Bloom + Colour Adjustments (PostProcessingEffect) ----
            (BloomAmountProperty,         ParamTarget.PostProcess, "BloomAmount",         PostProcessingEffect.BloomAmountPropertyPath),
            (BrightnessProperty,          ParamTarget.PostProcess, "Brightness",          PostProcessingEffect.BrightnessPropertyPath),
            (ContrastProperty,            ParamTarget.PostProcess, "Contrast",            PostProcessingEffect.ContrastPropertyPath),
            (SaturationProperty,          ParamTarget.PostProcess, "Saturation",          PostProcessingEffect.SaturationPropertyPath),
            (TemperatureProperty,         ParamTarget.PostProcess, "Temperature",         PostProcessingEffect.TemperaturePropertyPath),
            (ExposureProperty,            ParamTarget.PostProcess, "Exposure",            PostProcessingEffect.ExposurePropertyPath),
            (VibranceProperty,            ParamTarget.PostProcess, "Vibrance",            PostProcessingEffect.VibrancePropertyPath),
            // ---- Shape ----
            (ShapeRadiusProperty,         ParamTarget.Glass,       "ShapeRadius",         LiquidGlassEffect.EffectNameValue + ".ShapeRadius"),
            (ShapeRoundnessProperty,      ParamTarget.Glass,       "ShapeRoundness",      LiquidGlassEffect.EffectNameValue + ".ShapeRoundness"),
        };

        // ---- effect pipeline (built lazily when the brush attaches) ----
        //
        // FACTORY POOLING: CompositionEffectFactory registers animatable properties
        // in the compositor's global tracking table. The compositor has a hard cap of
        // 256 animatable properties aggregate across all factories. To avoid hitting
        // this cap when pages are navigated (and brushes are disconnected/reconnected),
        // factories are created ONCE, stored statically, and reused by every brush
        // instance. Only the brushes themselves (created from the pooled factories)
        // are per-instance and disposed in OnDisconnected.
        private static CompositionEffectFactory s_hBlurFactory;
        private static CompositionEffectFactory s_vBlurFactory;
        private static CompositionEffectFactory s_glassFactory;
        private static CompositionEffectFactory s_postProcessFactory;
        private static readonly object s_poolLock = new();

        private Compositor _compositor;
        private CompositionEffectBrush _glassBrush;
        private CompositionEffectBrush _hBlurBrush;   // separable blur (H pass)
        private CompositionEffectBrush _vBlurBrush;   // separable blur (V pass)
        private CompositionEffectBrush _postProcessBrush; // bloom + colour adjustments
        private CompositionBrush _backdropBrush;        // raw backdrop source (tracked for disposal on toggle)
        private bool _blurBypassed;                   // true when BlurAmount <= 0 (blur chain disconnected)

        /// <summary>
        /// If the effect pipeline fails to compile or link (e.g. shader too complex for
        /// the current DWM, or the native hook cannot be installed), the exception message
        /// and stack trace are written here. The brush degrades to a solid red fill
        /// instead of crashing the host app. Check this property after the brush connects
        /// to diagnose pipeline failures.
        /// </summary>
        public static string LastError { get; private set; }

        /// <summary>
        /// Builds the glass pipeline and connects it. Called by XAML when the brush
        /// is first used; not called directly from user code.
        /// </summary>
        protected override void OnConnected()
        {
            if (CompositionBrush != null)
            {
                return;
            }

            try
            {
                _compositor = CompositionTarget.GetCompositorForCurrentThread();

                CompositionEffectFactory hFactory, vFactory, gFactory, postProcessFactory;
                lock (s_poolLock)
                {
                    if (s_hBlurFactory == null)
                    {
                        s_hBlurFactory = _compositor.CreateEffectFactory(
                            new BlurHEffect().Create(),
                            new List<string> { BlurHEffect.BlurAmountPropertyPath });

                        s_vBlurFactory = _compositor.CreateEffectFactory(
                            new BlurVEffect().Create(),
                            new List<string> { BlurVEffect.BlurAmountPropertyPath });

                        var glassEffect = new LiquidGlassEffect().Create();
                        List<string> glassPaths = LiquidGlassEffect.Params
                            .Select(p => LiquidGlassEffect.EffectNameValue + "." + p.Key)
                            .ToList();
                        s_glassFactory = _compositor.CreateEffectFactory(glassEffect, glassPaths);

                        s_postProcessFactory = _compositor.CreateEffectFactory(
                            new PostProcessingEffect().Create(),
                            new List<string>
                            {
                                PostProcessingEffect.BloomAmountPropertyPath,
                                PostProcessingEffect.BrightnessPropertyPath,
                                PostProcessingEffect.ContrastPropertyPath,
                                PostProcessingEffect.SaturationPropertyPath,
                                PostProcessingEffect.TemperaturePropertyPath,
                                PostProcessingEffect.ExposurePropertyPath,
                                PostProcessingEffect.VibrancePropertyPath,
                            });
                    }
                    hFactory = s_hBlurFactory;
                    vFactory = s_vBlurFactory;
                    gFactory = s_glassFactory;
                    postProcessFactory = s_postProcessFactory;
                }

                _backdropBrush = _compositor.CreateBackdropBrush();
                // Create per-instance brushes from the pooled factories.
                _hBlurBrush = hFactory.CreateBrush();
                _hBlurBrush.SetSourceParameter("Backdrop", _backdropBrush);

                _vBlurBrush = vFactory.CreateBrush();
                _vBlurBrush.SetSourceParameter("Backdrop", _hBlurBrush);

                _postProcessBrush = postProcessFactory.CreateBrush();
                _postProcessBrush.SetSourceParameter("RawBackdrop", _backdropBrush);

                _glassBrush = gFactory.CreateBrush();
                _glassBrush.SetSourceParameter("Backdrop", _postProcessBrush);

                // When BlurAmount is 0 at connect time, both bloom sources see
                // the raw backdrop (lerp(raw, raw, B) == raw — identity). When
                // > 0, the bloom blends the blurred and raw backdrops.
                _blurBypassed = BlurAmount <= 0;
                _postProcessBrush.SetSourceParameter("Backdrop",
                    _blurBypassed ? _backdropBrush : _vBlurBrush);

                // Push every routine parameter's current value (DPs may have been set
                // before the brush connected). After this, the per-property changed
                // callbacks keep them in sync. BlurAmount is pushed separately below.
                foreach (var (dp, target, _, path) in s_params)
                {
                    ApplyParam(target, path, (float)(double)GetValue(dp));
                }

                // BlurAmount drives the H/V blur brushes (not a cbuffer slot). The
                // bypass source was already wired above, so only the scalar needs pushing.
                if (!_blurBypassed)
                {
                    float blur = (float)BlurAmount;
                    _hBlurBrush.Properties.InsertScalar(BlurHEffect.BlurAmountPropertyPath, blur);
                    _vBlurBrush.Properties.InsertScalar(BlurVEffect.BlurAmountPropertyPath, blur);
                }

                CompositionBrush = _glassBrush;
            }
            catch (Exception e)
            {
                LastError = e.Message + "\n" + e.StackTrace;

                _hBlurBrush?.Dispose();
                _vBlurBrush?.Dispose();
                _postProcessBrush?.Dispose();
                _backdropBrush?.Dispose();
                _glassBrush?.Dispose();

                CompositionBrush = _compositor.CreateColorBrush(Colors.Red);
            }
        }

        /// <summary>
        /// Drops the effect pipeline. Called by XAML when the brush is no longer used;
        /// not called directly from user code.
        /// </summary>
        protected override void OnDisconnected()
        {
            // Dispose per-instance brushes. The factories are pooled statically
            // and shared across all brush instances — they must NOT be disposed
            // here or subsequent brush instances would fail to create brushes.
            _hBlurBrush?.Dispose();
            _vBlurBrush?.Dispose();
            _postProcessBrush?.Dispose();
            _backdropBrush?.Dispose();

            // CompositionBrush == _glassBrush; dispose once via the base property.
            CompositionBrush?.Dispose();

            CompositionBrush = null;
            _glassBrush = null;
            _postProcessBrush = null;
            _hBlurBrush = null;
            _vBlurBrush = null;
            _backdropBrush = null;
            _compositor = null;
        }

        // Write one routine parameter to its effect brush. No-op until the pipeline
        // is connected; OnConnected applies all values at once.
        private void ApplyParam(ParamTarget target, string path, float value)
        {
            CompositionEffectBrush brush = target == ParamTarget.PostProcess
                ? _postProcessBrush : _glassBrush;
            brush?.Properties.InsertScalar(path, value);
        }

        // Apply BlurAmount: write the scalar to both H/V blur passes and, when the
        // value crosses the 0 boundary, swap the post-process "Backdrop" source so the
        // blur chain is bypassed (Backdrop = raw backdrop) or restored (Backdrop = V blur).
        // "RawBackdrop" and the glass source are untouched — only "Backdrop" flips.
        // SetSourceParameter resets animatable properties, so every routine parameter
        // is re-synced after the swap. No-op until the pipeline is connected.
        private void ApplyBlurAmount(float value)
        {
            bool bypass = value <= 0;
            if (bypass != _blurBypassed && _postProcessBrush != null)
            {
                _blurBypassed = bypass;
                _postProcessBrush.SetSourceParameter("Backdrop",
                    bypass ? _backdropBrush : _vBlurBrush);

                foreach (var (dp, target, _, path) in s_params)
                {
                    ApplyParam(target, path, (float)(double)GetValue(dp));
                }
            }
            if (!bypass)
            {
                _hBlurBrush?.Properties.InsertScalar(BlurHEffect.BlurAmountPropertyPath, value);
                _vBlurBrush?.Properties.InsertScalar(BlurVEffect.BlurAmountPropertyPath, value);
            }
        }

        /// <summary>
        /// Animates a named scalar property using a compositor-thread
        /// <see cref="Microsoft.UI.Composition.ScalarKeyFrameAnimation"/> with cubic
        /// ease-out. Runs entirely on the compositor thread — no UI-thread timers.
        /// </summary>
        /// <param name="key">Parameter key (e.g. "Exposure", "GlareAngle").</param>
        /// <param name="to">Target value.</param>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void AnimateScalar(string key, float to, double durationMs)
        {
            if (_compositor == null) return;

            foreach (var (_, target, entryKey, path) in s_params)
            {
                if (entryKey == key)
                {
                    CompositionEffectBrush brush = target == ParamTarget.PostProcess
                        ? _postProcessBrush : _glassBrush;
                    StartScalarAnimation(brush, path, to, durationMs);
                    return;
                }
            }

            // BlurAmount animates on both H/V blur brushes (registered as animatable in
            // their effect factories). Only meaningful while not bypassed; a target ≤ 0
            // would cross the bypass boundary, which is a discrete swap left to SetValue.
            if (key == "BlurAmount")
            {
                if (!_blurBypassed && to > 0)
                {
                    StartScalarAnimation(_hBlurBrush, BlurHEffect.BlurAmountPropertyPath, to, durationMs);
                    StartScalarAnimation(_vBlurBrush, BlurVEffect.BlurAmountPropertyPath, to, durationMs);
                }
                return;
            }

            // Unknown key: preserve the legacy behaviour of addressing the glass path
            // rather than silently no-op'ing.
            StartScalarAnimation(_glassBrush,
                LiquidGlassEffect.EffectNameValue + "." + key, to, durationMs);
        }

        private void StartScalarAnimation(CompositionBrush brush, string path, float to, double durationMs)
        {
            if (brush == null) return;
            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);
            anim.InsertKeyFrame(1.0f, to,
                _compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.215f, 0.61f),   // ease-out cubic
                    new System.Numerics.Vector2(0.355f, 1.0f)));
            brush.Properties.StartAnimation(path, anim);
        }

        /// <summary>
        /// Animates every animatable material parameter toward the values stored in
        /// <paramref name="target"/>. All animations are batched into a single
        /// <see cref="CompositionCommitBatch"/> so they start on the same compositor
        /// frame. <c>BlurAmount</c> is set directly (it lives on the H/V blur brushes,
        /// not the glass cbuffer) so its changed callback re-routes the pipeline.
        /// </summary>
        /// <remarks>
        /// The <paramref name="target"/> brush does not need to be connected — only its
        /// <see cref="DependencyProperty"/> values are read.
        /// </remarks>
        public void TransitionTo(LiquidGlassBrush target, double durationMs)
        {
            void Batch_Completed(object sender, CompositionBatchCompletedEventArgs args)
            {
                // The compositor animations have settled at the target values. Sync each
                // dependency property to the target's value so the brush's DPs match what
                // is on screen, and so a later reconnect (OnConnected re-pushes DPs) or an
                // external SetValue won't overwrite the endpoint with the pre-transition
                // value. Doing it here — after the batch completes — avoids interrupting
                // the animations.
                foreach (var (dp, _, _, _) in s_params)
                {
                    SetValue(dp, target.GetValue(dp));
                }
                // BlurAmount is not in s_params. Sync it now — this also stops the H/V
                // blur animations at their endpoint and applies any bypass source swap
                // needed when the transition crossed the blur on/off boundary.
                SetValue(BlurAmountProperty, target.BlurAmount);
            }
            if (_glassBrush == null || _compositor == null || target == null) return;

            var batch = _compositor.GetCommitBatch(CompositionBatchTypes.Animation);
            batch.Completed += Batch_Completed;


            foreach (var (dp, route, _, path) in s_params)
            {
                float targetValue = (float)(double)target.GetValue(dp);
                CompositionEffectBrush brush = route == ParamTarget.PostProcess
                    ? _postProcessBrush : _glassBrush;
                StartScalarAnimation(brush, path, targetValue, durationMs);
            }

            // BlurAmount is animatable on the H/V blur brushes (their effect factories
            // register BlurAmountPropertyPath as an animatable property). Animate it when
            // the transition stays on the blurred side. A bypass boundary crossing
            // (either side ≤ 0) is a discrete backdrop-source swap, so it is left to the
            // Completed handler's SetValue → ApplyBlurAmount rather than forced through a
            // continuous animation.
            float blurTarget = (float)target.BlurAmount;
            if (!_blurBypassed && blurTarget > 0)
            {
                StartScalarAnimation(_hBlurBrush, BlurHEffect.BlurAmountPropertyPath, blurTarget, durationMs);
                StartScalarAnimation(_vBlurBrush, BlurVEffect.BlurAmountPropertyPath, blurTarget, durationMs);
            }
        }
    }
}
