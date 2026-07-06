using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// backdrop into a real texture the glass sampler reads. The DPI the glass
    /// scales its bands by is auto-measured from the system DPI when the brush
    /// connects (see <see cref="Dpr"/>), so no code-behind is required.
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
        // Maps each parameter's DependencyProperty to its KEY (also the effect property
        // name). The full animatable path is <EffectName>.<key>.
        private static readonly Dictionary<DependencyProperty, string> s_paramKeys = new();

        // Post-processing parameter keys — routed to PostProcessingEffect instead of the glass brush.
        private static readonly HashSet<string> s_postProcessKeys = new()
        {
            "BloomAmount", "Brightness", "Contrast", "Saturation",
            "Temperature", "Exposure", "Vibrance",
        };

        private static DependencyProperty RegisterParam(string key, double defaultValue)
        {
            var dp = DependencyProperty.Register(key, typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(defaultValue, OnParamChanged));
            s_paramKeys[dp] = key;
            return dp;
        }

        // Glass parameter: default comes from LiquidGlassEffect.Params (single source of truth).
        private static DependencyProperty RegisterGlassParam(string key)
        {
            float defaultVal = LiquidGlassEffect.Params.First(p => p.Key == key).Default;
            return RegisterParam(key, defaultVal);
        }

        // ---- dependency properties: one per material parameter ----

        // ---- Refraction ----

        /// <summary>Backing dependency property for <see cref="RefThickness"/>.</summary>
        public static readonly DependencyProperty RefThicknessProperty = RegisterGlassParam("RefThickness");
        /// <summary>Refraction edge thickness, in logical pixels (default 20).</summary>
        public double RefThickness { get => (double)GetValue(RefThicknessProperty); set => SetValue(RefThicknessProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFactor"/>.</summary>
        public static readonly DependencyProperty RefFactorProperty = RegisterGlassParam("RefFactor");
        /// <summary>Index of refraction driving how strongly the backdrop is bent (default 1.4).</summary>
        public double RefFactor { get => (double)GetValue(RefFactorProperty); set => SetValue(RefFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefDispersion"/>.</summary>
        public static readonly DependencyProperty RefDispersionProperty = RegisterGlassParam("RefDispersion");
        /// <summary>Chromatic dispersion spreading the refraction by wavelength (default 7).</summary>
        public double RefDispersion { get => (double)GetValue(RefDispersionProperty); set => SetValue(RefDispersionProperty, value); }

        /// <summary>Backing dependency property for <see cref="DispersionRange"/>.</summary>
        public static readonly DependencyProperty DispersionRangeProperty = RegisterGlassParam("DispersionRange");
        /// <summary>Scales chromatic dispersion: 0 = no dispersion (single UV sample), 1 = full (default 1.0).</summary>
        public double DispersionRange { get => (double)GetValue(DispersionRangeProperty); set => SetValue(DispersionRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelRange"/>.</summary>
        public static readonly DependencyProperty RefFresnelRangeProperty = RegisterGlassParam("RefFresnelRange");
        /// <summary>Width of the Fresnel refraction band near grazing angles (default 30).</summary>
        public double RefFresnelRange { get => (double)GetValue(RefFresnelRangeProperty); set => SetValue(RefFresnelRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelHardness"/>.</summary>
        public static readonly DependencyProperty RefFresnelHardnessProperty = RegisterGlassParam("RefFresnelHardness");
        /// <summary>Hardness (sharpness) of the Fresnel refraction band falloff (default 20).</summary>
        public double RefFresnelHardness { get => (double)GetValue(RefFresnelHardnessProperty); set => SetValue(RefFresnelHardnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelFactor"/>.</summary>
        public static readonly DependencyProperty RefFresnelFactorProperty = RegisterGlassParam("RefFresnelFactor");
        /// <summary>Strength multiplier applied to the Fresnel refraction term (default 20).</summary>
        public double RefFresnelFactor { get => (double)GetValue(RefFresnelFactorProperty); set => SetValue(RefFresnelFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="Magnification"/>.</summary>
        public static readonly DependencyProperty MagnificationProperty = RegisterGlassParam("Magnification");
        /// <summary>Backdrop zoom factor centered on the glass: 1.0 = none, >1 = zoom in. Cannot go below 1 — sampling outside the backdrop content rect would read void.</summary>
        public double Magnification { get => (double)GetValue(MagnificationProperty); set => SetValue(MagnificationProperty, value); }

        // ---- Glare ----

        /// <summary>Backing dependency property for <see cref="GlareRange"/>.</summary>
        public static readonly DependencyProperty GlareRangeProperty = RegisterGlassParam("GlareRange");
        /// <summary>Angular width of the specular glare streak (default 30).</summary>
        public double GlareRange { get => (double)GetValue(GlareRangeProperty); set => SetValue(GlareRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareHardness"/>.</summary>
        public static readonly DependencyProperty GlareHardnessProperty = RegisterGlassParam("GlareHardness");
        /// <summary>Hardness (sharpness) of the glare streak falloff (default 20).</summary>
        public double GlareHardness { get => (double)GetValue(GlareHardnessProperty); set => SetValue(GlareHardnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareFactor"/>.</summary>
        public static readonly DependencyProperty GlareFactorProperty = RegisterGlassParam("GlareFactor");
        /// <summary>Intensity of the glare highlight (default 90).</summary>
        public double GlareFactor { get => (double)GetValue(GlareFactorProperty); set => SetValue(GlareFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareConvergence"/>.</summary>
        public static readonly DependencyProperty GlareConvergenceProperty = RegisterGlassParam("GlareConvergence");
        /// <summary>How tightly the glare converges toward its center (default 50).</summary>
        public double GlareConvergence { get => (double)GetValue(GlareConvergenceProperty); set => SetValue(GlareConvergenceProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareOppositeFactor"/>.</summary>
        public static readonly DependencyProperty GlareOppositeFactorProperty = RegisterGlassParam("GlareOppositeFactor");
        /// <summary>Intensity of the secondary, opposite-facing glare highlight (default 80).</summary>
        public double GlareOppositeFactor { get => (double)GetValue(GlareOppositeFactorProperty); set => SetValue(GlareOppositeFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareAngle"/>.</summary>
        public static readonly DependencyProperty GlareAngleProperty = RegisterGlassParam("GlareAngle");
        /// <summary>Direction of the glare streak, in degrees (default -45).</summary>
        public double GlareAngle { get => (double)GetValue(GlareAngleProperty); set => SetValue(GlareAngleProperty, value); }

        // ---- Blur ----

        /// <summary>Backing dependency property for <see cref="BlurAmount"/>.</summary>
        public static readonly DependencyProperty BlurAmountProperty = RegisterParam("BlurAmount", 1.0);
        /// <summary>
        /// Backdrop blur radius in pixels (default 1). Drives the separable H/V 1D
        /// blur passes upstream of the glass.
        /// </summary>
        public double BlurAmount { get => (double)GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }

        // ---- Tint ----

        /// <summary>Backing dependency property for <see cref="TintR"/>.</summary>
        public static readonly DependencyProperty TintRProperty = RegisterGlassParam("TintR");
        /// <summary>Red channel of the glass tint, 0–255 (default 255).</summary>
        public double TintR { get => (double)GetValue(TintRProperty); set => SetValue(TintRProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintG"/>.</summary>
        public static readonly DependencyProperty TintGProperty = RegisterGlassParam("TintG");
        /// <summary>Green channel of the glass tint, 0–255 (default 255).</summary>
        public double TintG { get => (double)GetValue(TintGProperty); set => SetValue(TintGProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintB"/>.</summary>
        public static readonly DependencyProperty TintBProperty = RegisterGlassParam("TintB");
        /// <summary>Blue channel of the glass tint, 0–255 (default 255).</summary>
        public double TintB { get => (double)GetValue(TintBProperty); set => SetValue(TintBProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintA"/>.</summary>
        public static readonly DependencyProperty TintAProperty = RegisterGlassParam("TintA");
        /// <summary>Alpha (opacity) of the glass tint, 0–1 (default 0 = untinted).</summary>
        public double TintA { get => (double)GetValue(TintAProperty); set => SetValue(TintAProperty, value); }

        // Exposure moved to PostProcessingEffect.

        // ---- Bloom (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="BloomAmount"/>.</summary>
        public static readonly DependencyProperty BloomAmountProperty = RegisterParam("BloomAmount", 0.0);
        /// <summary>Cross-fade between blurred and raw backdrop: 0 = pure blurred glass (default), 1 = fully sharp.</summary>
        public double BloomAmount { get => (double)GetValue(BloomAmountProperty); set => SetValue(BloomAmountProperty, value); }

        // ---- Colour Adjustments (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="Brightness"/>.</summary>
        public static readonly DependencyProperty BrightnessProperty = RegisterParam("Brightness", 0.0);
        /// <summary>Additive brightness: -1 = fully dark, 0 = unchanged, +1 = fully bright.</summary>
        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="Contrast"/>.</summary>
        public static readonly DependencyProperty ContrastProperty = RegisterParam("Contrast", 1.0);
        /// <summary>Contrast multiplier around mid-grey: 0 = flat grey, 1 = unchanged, 2 = doubled.</summary>
        public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }

        /// <summary>Backing dependency property for <see cref="Saturation"/>.</summary>
        public static readonly DependencyProperty SaturationProperty = RegisterParam("Saturation", 1.0);
        /// <summary>Saturation multiplier: 0 = greyscale, 1 = unchanged, 2 = oversaturated.</summary>
        public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

        /// <summary>Backing dependency property for <see cref="Temperature"/>.</summary>
        public static readonly DependencyProperty TemperatureProperty = RegisterParam("Temperature", 0.0);
        /// <summary>Colour temperature shift: -1 = cool (blue), 0 = unchanged, +1 = warm (yellow/red).</summary>
        public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }

        /// <summary>Backing dependency property for <see cref="Exposure"/> (PostProcessingEffect).</summary>
        public static readonly DependencyProperty ExposureProperty = RegisterParam("Exposure", 1.0);
        /// <summary>Exposure gain: 0.5 = half brightness, 1 = unchanged, 2 = double brightness.</summary>
        public double Exposure { get => (double)GetValue(ExposureProperty); set => SetValue(ExposureProperty, value); }

        /// <summary>Backing dependency property for <see cref="Vibrance"/>.</summary>
        public static readonly DependencyProperty VibranceProperty = RegisterParam("Vibrance", 0.0);
        /// <summary>Smart vibrance boost targeting low-saturation regions: 0 = off, 1 = full.</summary>
        public double Vibrance { get => (double)GetValue(VibranceProperty); set => SetValue(VibranceProperty, value); }

        // ---- Shape ----

        /// <summary>Backing dependency property for <see cref="ShapeRadius"/>.</summary>
        public static readonly DependencyProperty ShapeRadiusProperty = RegisterGlassParam("ShapeRadius");
        /// <summary>Corner radius as a 0–1 fraction of the shorter half-side (default 0.4).</summary>
        public double ShapeRadius { get => (double)GetValue(ShapeRadiusProperty); set => SetValue(ShapeRadiusProperty, value); }

        /// <summary>Backing dependency property for <see cref="ShapeRoundness"/>.</summary>
        public static readonly DependencyProperty ShapeRoundnessProperty = RegisterGlassParam("ShapeRoundness");
        /// <summary>Superellipse roundness exponent n (default 5).</summary>
        public double ShapeRoundness { get => (double)GetValue(ShapeRoundnessProperty); set => SetValue(ShapeRoundnessProperty, value); }

        /// <summary>
        /// Optional DPI override (physical px per logical px). Leave at 0 (the
        /// default) to auto-measure from the system DPI when the brush connects, so
        /// the brush is fully usable from XAML with no code-behind. If set to a value
        /// greater than 0, that value is used as-is.
        /// </summary>
        public float Dpr { get; set; }

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

                        var glassEffect = new LiquidGlassEffect
                        {
                            Dpr = Dpr > 0 ? Dpr : MeasureDpr()
                        }.Create();
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
                if (BlurAmount <= 0)
                {
                    _blurBypassed = true;
                    _postProcessBrush.SetSourceParameter("Backdrop", _backdropBrush);
                }
                else
                {
                    _postProcessBrush.SetSourceParameter("Backdrop", _vBlurBrush);
                }

                // Push every parameter's current value (DPs may have been set before
                // the brush connected). After this, OnParamChanged keeps them in sync.
                foreach (var pair in s_paramKeys)
                {
                    ApplyValue(pair.Value, (float)(double)GetValue(pair.Key));
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

        private static void OnParamChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var brush = (LiquidGlassBrush)d;
            if (s_paramKeys.TryGetValue(e.Property, out string key))
            {
                brush.ApplyValue(key, (float)(double)e.NewValue);
            }
        }

        // Route one parameter to the right effect brush. Post-processing params
        // (bloom + colour adjustments) go to the PostProcessingEffect. BlurAmount
        // drives both 1D separable blur passes; when it drops to ≤ 0 the blur chain
        // is bypassed at the post-process stage. Glass params go to the
        // LiquidGlassEffect. No-op until the pipeline is connected; OnConnected
        // applies all values at once.
        private void ApplyValue(string key, float value)
        {
            // Post-processing params: route to the PostProcessingEffect brush.
            if (s_postProcessKeys.Contains(key))
            {
                _postProcessBrush?.Properties.InsertScalar(
                    PostProcessingEffect.EffectNameValue + "." + key, value);
                return;
            }

            if (key == "BlurAmount")
            {
                bool bypass = value <= 0;
                if (bypass != _blurBypassed && _postProcessBrush != null)
                {
                    _blurBypassed = bypass;
                    // Swap the post-process "Backdrop" source only — "RawBackdrop"
                    // stays connected to _backdropBrush; glass always reads from
                    // _postProcessBrush. No backdrop dispose/recreate needed.
                    _postProcessBrush.SetSourceParameter("Backdrop",
                        bypass ? _backdropBrush : _vBlurBrush);

                    // SetSourceParameter resets animatable properties, so re-sync
                    // post-processing and glass parameters after the swap.
                    foreach (var kv in s_paramKeys)
                    {
                        if (kv.Value == "BlurAmount") continue;
                        float v = (float)(double)GetValue(kv.Key);
                        if (s_postProcessKeys.Contains(kv.Value))
                        {
                            _postProcessBrush.Properties.InsertScalar(
                                PostProcessingEffect.EffectNameValue + "." + kv.Value, v);
                        }
                        else
                        {
                            _glassBrush.Properties.InsertScalar(
                                LiquidGlassEffect.EffectNameValue + "." + kv.Value, v);
                        }
                    }
                }
                if (!bypass)
                {
                    _hBlurBrush?.Properties.InsertScalar(BlurHEffect.BlurAmountPropertyPath, value);
                    _vBlurBrush?.Properties.InsertScalar(BlurVEffect.BlurAmountPropertyPath, value);
                }
                return;
            }
            _glassBrush?.Properties.InsertScalar(LiquidGlassEffect.EffectNameValue + "." + key, value);
        }

        /// <summary>
        /// Animates a named scalar property using a compositor-thread
        /// <see cref="Microsoft.UI.Composition.ScalarKeyFrameAnimation"/> with cubic
        /// ease-out. Runs entirely on the compositor thread — no UI-thread timers.
        /// </summary>
        /// <param name="key">Parameter key (e.g. "TintA", "GlareAngle").</param>
        /// <param name="to">Target value.</param>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void AnimateScalar(string key, float to, double durationMs)
        {
            if (_compositor == null) return;

            // Post-processing keys animate on the PostProcessingEffect brush.
            if (s_postProcessKeys.Contains(key))
            {
                if (_postProcessBrush == null) return;
                var path = PostProcessingEffect.EffectNameValue + "." + key;
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                anim.InsertKeyFrame(1.0f, to,
                    _compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.215f, 0.61f),
                        new System.Numerics.Vector2(0.355f, 1.0f)));
                _postProcessBrush.Properties.StartAnimation(path, anim);
                return;
            }

            if (_glassBrush == null) return;

            var fullPath = LiquidGlassEffect.EffectNameValue + "." + key;
            var animation = _compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);
            animation.InsertKeyFrame(1.0f, to,
                _compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.215f, 0.61f),   // ease-out cubic
                    new System.Numerics.Vector2(0.355f, 1.0f)));

            _glassBrush.Properties.StartAnimation(fullPath, animation);
        }

        /// <summary>
        /// Animates every animatable material parameter toward the values stored in
        /// <paramref name="target"/>. All animations are batched into a single
        /// <see cref="CompositionCommitBatch"/> so they start on the same compositor
        /// frame. <c>BlurAmount</c> is set directly (it lives on the H/V blur brushes,
        /// not the glass cbuffer).
        /// </summary>
        /// <remarks>
        /// The <paramref name="target"/> brush does not need to be connected — only its
        /// <see cref="DependencyProperty"/> values are read.
        /// </remarks>
        public void TransitionTo(LiquidGlassBrush target, double durationMs)
        {
            if (_glassBrush == null || _compositor == null || target == null) return;

            var batch = _compositor.GetCommitBatch(CompositionBatchTypes.Animation);

            foreach (var (dp, key) in s_paramKeys)
            {
                var targetValue = (float)(double)target.GetValue(dp);

                // BlurAmount drives the separable H/V blur passes, not the glass
                // cbuffer. Set it directly so OnParamChanged → ApplyValue updates
                // both blur brushes. Not animated, but it's 1 param out of 22.
                if (key == "BlurAmount")
                {
                    SetValue(dp, (double)targetValue);
                }
                else if (s_postProcessKeys.Contains(key))
                {
                    // Post-processing params animate on the PostProcessingEffect brush.
                    if (_postProcessBrush != null && _compositor != null)
                    {
                        var path = PostProcessingEffect.EffectNameValue + "." + key;
                        var anim = _compositor.CreateScalarKeyFrameAnimation();
                        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                        anim.InsertKeyFrame(1.0f, targetValue,
                            _compositor.CreateCubicBezierEasingFunction(
                                new System.Numerics.Vector2(0.215f, 0.61f),
                                new System.Numerics.Vector2(0.355f, 1.0f)));
                        _postProcessBrush.Properties.StartAnimation(path, anim);
                    }
                }
                else
                {
                    AnimateScalar(key, targetValue, durationMs);
                }
            }
        }

        // System DPI (physical px per logical px). GetDpiForSystem needs no window
        // handle, so the brush can measure it itself in OnConnected — this is what
        // makes the brush usable from XAML with no code-behind. (Returns the primary
        // monitor's DPI; falls back to 1.0 on any failure.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private static float MeasureDpr()
        {
            try
            {
                return GetDpiForSystem() / 96f;
            }
            catch
            {
                return 1.0f;
            }
        }
    }
}
