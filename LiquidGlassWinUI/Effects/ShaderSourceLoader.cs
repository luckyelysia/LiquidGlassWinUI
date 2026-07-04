using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LiquidGlassWinUI.Effects
{
    // Loads HLSL source for the custom effects from a manifest resource embedded
    // in this assembly (see the <EmbeddedResource> item in the .csproj). The
    // native runtime consumes HLSL source as a UTF-8 byte buffer, so reading from
    // an embedded resource is functionally identical to the old loose-file path
    // but keeps the consumer's output directory clean and removes any disk-path
    // fragility — appropriate for a redistributable library.
    internal static class ShaderSourceLoader
    {
        // Reads the embedded shader whose resource name ends with the requested
        // file name (e.g. "LiquidGlass.hlsl"). The manifest name is derived from
        // the RootNamespace + the project-relative path, so matching on the
        // suffix keeps this independent of the exact folder layout.
        public static string Load(string shaderFileName)
        {
            if (string.IsNullOrEmpty(shaderFileName))
            {
                throw new ArgumentException("shaderFileName is required.", nameof(shaderFileName));
            }

            Assembly asm = typeof(ShaderSourceLoader).GetTypeInfo().Assembly;
            string resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("." + shaderFileName, StringComparison.Ordinal));

            if (resourceName == null)
            {
                throw new InvalidOperationException(
                    "Embedded shader resource not found for '" + shaderFileName +
                    "'. Ensure the .hlsl is included as an <EmbeddedResource> in LiquidGlassWinUI.csproj.");
            }

            using Stream stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("Could not open embedded shader resource '" + resourceName + "'.");
            }

            // The shader source is UTF-8 text.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
