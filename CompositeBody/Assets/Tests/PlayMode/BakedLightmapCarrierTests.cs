using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CompositeBody.Tests
{
    /// <summary>
    /// Does a carrier give back its slice of the lightmap array without disturbing the others?
    ///
    /// This is the whole reason the component rebuilds the array rather than patching it. A
    /// prefab's renderers hold an index into a global array; remove one prefab's entries from
    /// the middle and every index after them now points one slot too far. Nothing throws. The
    /// walls simply get somebody else's lighting, and only once a second scene has been unloaded
    /// -- which, in a piece that loads three of them in sequence, is a fault that would first
    /// appear in a rehearsal rather than at a desk.
    ///
    /// The arithmetic is checked here with fake textures and no bake, because it is arithmetic.
    /// Whether the lighting LOOKS right still needs a headset, and this does not claim to say.
    /// </summary>
    public class BakedLightmapCarrierTests
    {
        /// <summary>
        /// Reached by name because this test assembly does not reference Assembly-CSharp -- an
        /// asmdef cannot, Unity builds the predefined assemblies after it -- and PlayerSpawnTests
        /// next door reaches the project's own types the same way.
        /// </summary>
        static readonly System.Type k_Carrier = System.Type.GetType(
            "CompositeBody.Experience.BakedLightmapCarrier, Assembly-CSharp");

        LightmapData[] m_Restore;

        [SetUp]
        public void SetUp()
        {
            Assert.NotNull(k_Carrier, "BakedLightmapCarrier was not found in Assembly-CSharp. " +
                                      "It has been renamed, moved to another namespace, or put " +
                                      "behind an asmdef.");
            m_Restore = LightmapSettings.lightmaps;
        }

        [TearDown]
        public void TearDown() => LightmapSettings.lightmaps = m_Restore;

        [Test]
        public void UnloadingOneCarrierRebasesTheRest()
        {
            GameObject a = Carrier("A", maps: 2, localIndex: 1, out Renderer rendererA);
            GameObject b = Carrier("B", maps: 3, localIndex: 0, out Renderer rendererB);

            try
            {
                a.SetActive(true);
                b.SetActive(true);

                Assert.AreEqual(5, LightmapSettings.lightmaps.Length,
                    "Both carriers' lightmaps should be loaded, two then three.");

                Assert.AreEqual(1, rendererA.lightmapIndex,
                    "A came first, so its local index 1 should still be 1.");
                Assert.AreEqual(2, rendererB.lightmapIndex,
                    "B's maps start after A's two, so its local index 0 should be 2.");

                // The moment that matters: A goes away, and B must be told where its maps went.
                a.SetActive(false);

                Assert.AreEqual(3, LightmapSettings.lightmaps.Length,
                    "A's two lightmaps should have been released.");
                Assert.AreEqual(0, rendererB.lightmapIndex,
                    "B is now first, so its local index 0 should have been rebased to 0. If this " +
                    "is still 2, the array was rebuilt without reassigning the renderers and " +
                    "everything left loaded is lit by the wrong maps.");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void TheScenesOwnLightmapsAreNotMoved()
        {
            LightmapSettings.lightmaps = new[]
            {
                new LightmapData { lightmapColor = Texture2D.whiteTexture },
            };

            GameObject a = Carrier("A", maps: 2, localIndex: 0, out Renderer renderer);

            try
            {
                a.SetActive(true);

                Assert.AreEqual(3, LightmapSettings.lightmaps.Length,
                    "The scene's one lightmap plus the carrier's two.");
                Assert.AreEqual(1, renderer.lightmapIndex,
                    "The carrier's maps must come AFTER the scene's, or the scene's own " +
                    "renderers -- which this component never touches -- would be relit.");
            }
            finally
            {
                Object.DestroyImmediate(a);
            }
        }

        /// <summary>
        /// A carrier holding <paramref name="maps"/> fake lightmaps, with one renderer pointed at
        /// <paramref name="localIndex"/>. Built inactive so OnEnable does not run before the
        /// fields are in place.
        /// </summary>
        static GameObject Carrier(string name, int maps, int localIndex, out Renderer renderer)
        {
            var go = new GameObject(name);
            go.SetActive(false);

            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(go.transform);
            renderer = child.GetComponent<Renderer>();

            Component carrier = go.AddComponent(k_Carrier);
            System.Type type = k_Carrier;

            object entries = MakeArray(type, "m_Entries", 1);
            SetElement(entries, 0, new (string, object)[]
            {
                ("renderer", renderer),
                ("index", localIndex),
                ("scaleOffset", new Vector4(1f, 1f, 0f, 0f)),
            });

            object lightmaps = MakeArray(type, "m_Lightmaps", maps);
            for (int i = 0; i < maps; i++)
                SetElement(lightmaps, i, new (string, object)[] { ("color", Texture2D.whiteTexture) });

            Field(type, "m_Entries").SetValue(carrier, entries);
            Field(type, "m_Lightmaps").SetValue(carrier, lightmaps);

            return go;
        }

        static FieldInfo Field(System.Type type, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        static object MakeArray(System.Type owner, string field, int length) =>
            System.Array.CreateInstance(Field(owner, field).FieldType.GetElementType(), length);

        static void SetElement(object array, int at, (string name, object value)[] values)
        {
            var typed = (System.Array)array;
            object element = typed.GetValue(at);

            foreach ((string name, object value) in values)
            {
                element.GetType()
                       .GetField(name, BindingFlags.Instance | BindingFlags.Public)
                       .SetValue(element, value);
            }

            typed.SetValue(element, at);
        }
    }
}
