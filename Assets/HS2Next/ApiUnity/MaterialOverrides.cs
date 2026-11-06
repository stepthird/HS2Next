// Typed material and renderer overrides (engine side of the plugin Api): one slot's float, colour, texture or shader, or
// a renderer's enabled flag. Every mosaic member and every material with a mosaic shader is refused (Protected): the
// warning api.mosaicProtected goes to the character's findings (the scene's for other renderers), one line to the
// plugin's log, and the refusal is counted in the plugin's report (it is not a fault).
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>The result of an override.</summary>
    public enum OverrideResult
    {
        /// <summary>The value was set.</summary>
        Applied = 0,
        /// <summary>Refused: the renderer is a mosaic member or the material has a mosaic shader.</summary>
        Protected = 1,
        /// <summary>No such renderer, slot, material or property.</summary>
        Missing = 2,
    }

    /// <summary>Typed overrides on a renderer's material slots. Main thread only. Implemented by the host.</summary>
    public interface IMaterialOverrides
    {
        /// <summary>Sets the float <paramref name="property"/> of the material in <paramref name="slot"/>.</summary>
        OverrideResult SetFloat(Renderer renderer, int slot, string property, float value);
        /// <summary>Sets the colour <paramref name="property"/> of the material in <paramref name="slot"/>.</summary>
        OverrideResult SetColor(Renderer renderer, int slot, string property, Color value);
        /// <summary>Sets the texture <paramref name="property"/> of the material in <paramref name="slot"/> (the texture stays the plugin's).</summary>
        OverrideResult SetTexture(Renderer renderer, int slot, string property, Texture value);
        /// <summary>Gives the slot's material another shader (its render queue is kept).</summary>
        OverrideResult SetShader(Renderer renderer, int slot, Shader shader);
        /// <summary>Shows or hides the renderer.</summary>
        OverrideResult SetEnabled(Renderer renderer, bool enabled);
    }
}
