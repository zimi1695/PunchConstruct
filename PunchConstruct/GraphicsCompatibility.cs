namespace PunchConstruct;

// Native VisualShader stores an fma-based cached program that can be submitted
// before its graph regenerates for GLES3. Keep a separate, equivalent shader and
// scene path so neither the game's shared shader nor native scene cache changes.
internal static class GraphicsCompatibility
{
    internal static bool Applies(bool compatibilityRenderer, bool hasConstruct) => compatibilityRenderer && hasConstruct;
}
