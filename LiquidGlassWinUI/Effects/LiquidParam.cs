namespace LiquidGlassWinUI.Effects
{
    // One parameter of the LiquidGlass material: its cbuffer byte offset, the
    // default value, and (carried over from the reference) the UI range/label.
    // This array is the single source of truth: the effect's cbuffer (offsets/
    // defaults) and the brush's dependency properties (defaults) both read from
    // it. The cbuffer stores the RAW control value; the shader applies any
    // normalization (/100, *PI/180, /255, radius derivation) itself.
    internal sealed class LiquidParam
    {
        public string Key { get; set; }       // cbuffer field name + DP name
        public uint Offset { get; set; }      // byte offset in the 128-byte cbuffer
        public float Default { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public float Step { get; set; }
        public string Group { get; set; }
        public string Label { get; set; }
    }
}
