using System;
using System.IO;

namespace BlendProbe.Effects
{
    // Loads HLSL source from loose files deployed next to the exe under
    // Effects/Shaders/. Loose files let you tweak a shader at runtime without
    // recompiling; the csproj copies them to the output directory via
    // <None CopyToOutputDirectory>.
    internal static class ShaderSourceLoader
    {
        private const string ShaderFolder = "Effects\\Shaders";

        public static string Load(string shaderFileName)
        {
            if (string.IsNullOrEmpty(shaderFileName))
            {
                throw new ArgumentException("shaderFileName is required.", nameof(shaderFileName));
            }

            string path = Path.Combine(AppContext.BaseDirectory, ShaderFolder, shaderFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Shader file not found: " + path +
                    ". Ensure the Effects\\Shaders\\*.hlsl files are deployed next to the exe.",
                    path);
            }

            return File.ReadAllText(path);
        }
    }
}
