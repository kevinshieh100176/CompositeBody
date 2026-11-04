using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompositeBody.Interaction.Home
{
    /// <summary>
    /// Where the top of the scene's water is, so the zone beacon can stand clear of it.
    ///
    /// The water is KriptoFX's KWS.WaterSystem, found BY TYPE NAME through reflection so Home has no
    /// compile-time reference to the package (it lives in the art scene's /Outer, not in Home).
    /// KWS draws its surface without writing the depth the light column intersects against, so a
    /// column piercing the water draws over it and its seam shimmers with the waves; the only clean
    /// answer is not to overlap.
    ///
    /// Surface top = max(water transform y + CurrentMaxWaveHeight, WaterBoundsSurfaceHeight when it
    /// is a sane world height). Both are internal fields; an unreadable or not-yet-computed wave
    /// height falls back to <see cref="k_DefaultWave"/>.
    /// </summary>
    public static class HomeWaterProbe
    {
        public const string WaterTypeName = "KWS.WaterSystem";
        const float k_DefaultWave = 0.1f;
        const float k_SaneRange = 2f; // metres from the water's own pivot

        static Type s_Type;
        static bool s_Searched;

        public static Type waterType
        {
            get
            {
                if (s_Searched) return s_Type;
                s_Searched = true;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType(WaterTypeName, false);
                    if (t == null) continue;
                    s_Type = t;
                    break;
                }
                return s_Type;
            }
        }

        /// <summary>
        /// Highest water surface among the active KWS water objects in <paramref name="scene"/>.
        /// False when there is none (or KWS is not in the project).
        /// </summary>
        public static bool TryGetSurfaceTop(Scene scene, out float top, out string detail)
        {
            top = float.NegativeInfinity;
            detail = "no KWS.WaterSystem";
            Type type = waterType;
            if (type == null || !scene.IsValid() || !scene.isLoaded) return false;

            bool found = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component water in root.GetComponentsInChildren(type, false))
                {
                    if (water == null) continue;
                    float pivot = water.transform.position.y;
                    float wave = ReadFloat(water, "CurrentMaxWaveHeight", out bool waveRead);
                    bool waveOk = waveRead && wave > 0f && wave <= k_SaneRange;
                    float bounds = ReadFloat(water, "WaterBoundsSurfaceHeight", out bool boundsRead);
                    bool boundsSane = boundsRead && !float.IsNaN(bounds) && !float.IsInfinity(bounds) &&
                                      Mathf.Abs(bounds - pivot) < k_SaneRange;

                    float t = pivot + (waveOk ? wave : k_DefaultWave);
                    if (boundsSane) t = Mathf.Max(t, bounds);
                    if (t > top)
                    {
                        top = t;
                        detail = $"'{water.name}' pivot y {pivot:0.000}, max wave " +
                                 $"{(waveOk ? wave.ToString("0.000") : $"n/a ({k_DefaultWave:0.00} assumed)")}, " +
                                 $"bounds top {(boundsSane ? bounds.ToString("0.000") : "n/a")} -> surface top {t:0.000}";
                    }
                    found = true;
                }
            }
            return found;
        }

        static float ReadFloat(object target, string name, out bool ok)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = target.GetType();
            FieldInfo field = t.GetField(name, flags);
            if (field != null && field.FieldType == typeof(float))
            {
                ok = true;
                return (float)field.GetValue(target);
            }
            PropertyInfo prop = t.GetProperty(name, flags);
            if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead)
            {
                ok = true;
                return (float)prop.GetValue(target);
            }
            ok = false;
            return 0f;
        }
    }
}
