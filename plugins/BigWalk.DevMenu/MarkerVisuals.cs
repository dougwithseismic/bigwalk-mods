using System;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// Builds the geometry and materials for in-world markers.
///
/// The hard part in an IL2CPP/URP build is getting a material at all: shader
/// stripping means Shader.Find returns null for anything the game didn't ship,
/// and a null shader renders magenta (or nothing). So we resolve a shader by
/// searching what is already loaded, in preference order, and report which one
/// we landed on so a bad pick is diagnosable in-game rather than a mystery.
/// </summary>
internal static class MarkerVisuals
{
    /// <summary>Which shader we ended up on, for the diagnostics panel.</summary>
    public static string ResolvedShader { get; private set; } = "<not yet resolved>";

    private static Shader _shader;
    private static Mesh _quad;

    // Preferred first: unlit shaders ignore scene lighting, so a marker reads the
    // same colour at midnight as at noon - which matters when the colour IS the
    // data. Sprite/particle shaders are the next best thing and are almost always
    // retained because the game uses them for VFX.
    private static readonly string[] ShaderPreference =
    {
        "Universal Render Pipeline/Unlit",
        "Unlit/Color",
        "Sprites/Default",
        "Universal Render Pipeline/Particles/Unlit",
        "Particles/Standard Unlit",
    };

    private static readonly string[] ShaderFragments = { "unlit", "sprites/default", "particles" };

    public static Shader Resolve()
    {
        if (_shader != null) return _shader;

        // Shader.Find only sees shaders retained in the build, but it is cheap and
        // exact, so try the named preferences first.
        foreach (var name in ShaderPreference)
        {
            var s = Shader.Find(name);
            if (s != null)
            {
                _shader = s;
                ResolvedShader = $"{s.name} (found)";
                Plugin.Trace.LogInfo($"Marker shader: {s.name} via Shader.Find.");
                return _shader;
            }
        }

        // Fall back to whatever unlit-ish shader is already loaded in memory.
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Shader>();
            if (all != null)
            {
                foreach (var frag in ShaderFragments)
                {
                    foreach (var s in all)
                    {
                        if (s == null || string.IsNullOrEmpty(s.name)) continue;
                        if (s.name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        _shader = s;
                        ResolvedShader = $"{s.name} (scanned)";
                        Plugin.Trace.LogInfo($"Marker shader: {s.name} via loaded-shader scan.");
                        return _shader;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogWarning($"Shader scan failed: {e.Message}");
        }

        ResolvedShader = "<none - markers will use lights only>";
        Plugin.Trace.LogWarning("No usable marker shader; falling back to light-only markers.");
        return null;
    }

    /// <summary>
    /// A unit quad on the XY plane. Built by hand rather than via CreatePrimitive
    /// so we never inherit the default material, which is exactly the thing that
    /// renders magenta on a stripped build.
    /// </summary>
    public static Mesh Quad()
    {
        if (_quad != null) return _quad;

        var m = new Mesh { name = "BigWalk.MarkerQuad" };

        m.vertices = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4)
        {
            [0] = new Vector3(-0.5f, -0.5f, 0f),
            [1] = new Vector3(0.5f, -0.5f, 0f),
            [2] = new Vector3(-0.5f, 0.5f, 0f),
            [3] = new Vector3(0.5f, 0.5f, 0f),
        };

        m.uv = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2>(4)
        {
            [0] = new Vector2(0f, 0f),
            [1] = new Vector2(1f, 0f),
            [2] = new Vector2(0f, 1f),
            [3] = new Vector2(1f, 1f),
        };

        m.triangles = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(6)
        {
            [0] = 0, [1] = 2, [2] = 1,
            [3] = 2, [4] = 3, [5] = 1,
        };

        m.RecalculateBounds();
        m.hideFlags = HideFlags.HideAndDontSave;
        _quad = m;
        return _quad;
    }

    /// <summary>
    /// A transparent, optionally depth-ignoring material. Property names differ
    /// between URP (_BaseColor) and built-in (_Color), so set whichever exists.
    /// </summary>
    public static Material NewMaterial(bool throughWalls)
    {
        var shader = Resolve();
        if (shader == null) return null;

        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        TrySetFloat(mat, "_Surface", 1f);   // URP: 0 opaque, 1 transparent
        TrySetFloat(mat, "_Blend", 0f);     // alpha blend
        TrySetFloat(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        TrySetFloat(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        TrySetFloat(mat, "_ZWrite", 0f);
        TrySetFloat(mat, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);

        // ZTest Always (8) is what makes a marker visible through terrain - the
        // whole point of a "who is talking over there" indicator.
        if (throughWalls) TrySetFloat(mat, "_ZTest", 8f);

        mat.renderQueue = 4000;
        return mat;
    }

    public static void Tint(Material mat, Color c)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c);
    }

    private static void TrySetFloat(Material mat, string prop, float value)
    {
        if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
    }

    /// <summary>
    /// Green (near) through amber to red (edge of audibility). Two-stage so the
    /// midpoint is yellow rather than the muddy brown a direct lerp passes through.
    /// </summary>
    public static Color DistanceColour(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? Color.Lerp(new Color(0.30f, 1f, 0.35f), new Color(1f, 0.85f, 0.20f), t * 2f)
            : Color.Lerp(new Color(1f, 0.85f, 0.20f), new Color(1f, 0.25f, 0.20f), (t - 0.5f) * 2f);
    }
}
