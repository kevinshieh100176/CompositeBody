using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CompositeBody.Tests
{
    /// <summary>
    /// Does switching looks leave exactly one section's lighting loaded?
    ///
    /// The thing being checked is not that the switch happens -- it is that the OLD one goes
    /// away first. A carrier claims the global lightmap array and the one set of fog, ambient
    /// and skybox the moment it is enabled, so a switch that turns the new one on before the old
    /// one off has both up for a frame, and the fog cannot be two colours: one of them loses,
    /// quietly, and which one depends on the order the components happened to enable in. That is
    /// the sort of thing that works at a desk and is wrong in a rehearsal.
    ///
    /// Also checked: a name that does not match leaves the lighting where it is. A typo in a
    /// timeline clip should cost a wrong-looking transition, not a black room.
    ///
    /// Fake textures, no bake -- this is bookkeeping, and whether the rooms LOOK right still
    /// needs a headset.
    /// </summary>
    public class LightingDirectorTests
    {
        /// <summary>
        /// Reached by name because this test assembly does not reference Assembly-CSharp -- an
        /// asmdef cannot, Unity builds the predefined assemblies after it -- which is how
        /// BakedLightmapCarrierTests next door reaches the same types.
        /// </summary>
        static readonly System.Type k_Carrier = System.Type.GetType(
            "CompositeBody.Experience.BakedLightmapCarrier, Assembly-CSharp");

        static readonly System.Type k_Director = System.Type.GetType(
            "CompositeBody.Experience.LightingDirector, Assembly-CSharp");

        LightmapData[] m_Restore;

        [SetUp]
        public void SetUp()
        {
            Assert.NotNull(k_Carrier, "BakedLightmapCarrier was not found in Assembly-CSharp.");
            Assert.NotNull(k_Director, "LightingDirector was not found in Assembly-CSharp. It " +
                                       "has been renamed, moved to another namespace, or put " +
                                       "behind an asmdef.");
            m_Restore = LightmapSettings.lightmaps;
        }

        [TearDown]
        public void TearDown() => LightmapSettings.lightmaps = m_Restore;

        [Test]
        public void OnlyOneLooksLightingIsEverLoaded()
        {
            LightmapSettings.lightmaps = new LightmapData[0];

            GameObject a = Carrier("A", maps: 2, out Renderer rendererA);
            GameObject b = Carrier("B", maps: 3, out Renderer rendererB);
            GameObject host = Director("A", ("A", a), ("B", b));

            try
            {
                Assert.AreEqual(2, LightmapSettings.lightmaps.Length,
                    "The start look was A, so A's two lightmaps and nothing else should be " +
                    "loaded. Three or five here means the director let a carrier it did not " +
                    "choose keep its claim.");
                Assert.IsTrue(a.activeSelf, "A's root should have been switched on.");
                Assert.IsFalse(b.activeSelf, "B's root should have been switched off.");
                Assert.AreEqual(0, rendererA.lightmapIndex,
                    "A is the only carrier loaded, so its maps start at 0.");

                Apply(host, "B");

                Assert.AreEqual(3, LightmapSettings.lightmaps.Length,
                    "Only B's three lightmaps should be loaded now. Five means A was never " +
                    "released -- the switch turned B on without turning A off, and both are " +
                    "holding the environment.");
                Assert.IsFalse(a.activeSelf, "A's root should have gone with A's lighting. Left " +
                                             "visible, its renderers keep indices that now " +
                                             "point into B's maps, which reads as a broken bake.");
                Assert.IsTrue(b.activeSelf, "B's root should have been switched on.");
                Assert.AreEqual(0, rendererB.lightmapIndex,
                    "B is now the only carrier loaded, so its maps start at 0.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ALookWithNoCarrierHandsTheLightingBackToTheScene()
        {
            // Stand in for a scene that was baked in its own right, which MainScene is.
            var sceneOwn = new[] { new LightmapData { lightmapColor = Texture2D.whiteTexture } };
            LightmapSettings.lightmaps = sceneOwn;

            GameObject a = Carrier("A", maps: 2, out _);
            GameObject host = Director("A", ("A", a), ("Scene", null));

            try
            {
                Assert.AreEqual(3, LightmapSettings.lightmaps.Length,
                    "The scene's one lightmap plus A's two.");

                Apply(host, "Scene");

                Assert.AreEqual(1, LightmapSettings.lightmaps.Length,
                    "A look holding no carrier should switch every carrier off, which is what " +
                    "leaves the scene's own bake in place and nothing else. This is how the " +
                    "onboarding room is named on the timeline.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(a);
            }
        }

        [Test]
        public void AnUnknownLookLeavesTheLightingAlone()
        {
            LightmapSettings.lightmaps = new LightmapData[0];

            GameObject a = Carrier("A", maps: 2, out _);
            GameObject host = Director("A", ("A", a));

            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "no look called 'S2'"));

                Apply(host, "S2");

                Assert.AreEqual(2, LightmapSettings.lightmaps.Length,
                    "A's lighting should still be loaded. A clip asking for a name that does " +
                    "not exist must not strip the bake off the room the players are standing in.");
                Assert.IsTrue(a.activeSelf, "A's root should still be up.");
                Assert.AreEqual("A", Current(host),
                    "The current look should not have moved to a name that was refused.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(a);
            }
        }

        // --- building the things under test --------------------------------------------------

        /// <summary>
        /// A director with the given looks, each one's carrier and root taken from the carrier
        /// object built by <see cref="Carrier"/>. A null object makes a look that carries
        /// nothing, which is the scene's own lighting.
        /// </summary>
        static GameObject Director(string startLook, params (string name, GameObject obj)[] looks)
        {
            var host = new GameObject("LightingDirector");
            host.SetActive(false);

            Component director = host.AddComponent(k_Director);

            FieldInfo field = Field(k_Director, "m_Looks");
            System.Type element = field.FieldType.GetElementType();
            var array = System.Array.CreateInstance(element, looks.Length);

            for (int i = 0; i < looks.Length; i++)
            {
                object look = array.GetValue(i);
                Set(look, "name", looks[i].name);

                if (looks[i].obj != null)
                {
                    Set(look, "carrier", looks[i].obj.GetComponent(k_Carrier));
                    Set(look, "root", looks[i].obj);
                }

                array.SetValue(look, i);
            }

            field.SetValue(director, array);
            Field(k_Director, "m_StartLook").SetValue(director, startLook);

            // Awake applies the start look, so everything has to be in place before this.
            host.SetActive(true);
            return host;
        }

        static void Apply(GameObject host, string lookName) =>
            k_Director.GetMethod("ApplyLook").Invoke(host.GetComponent(k_Director), new object[] { lookName });

        static string Current(GameObject host) =>
            (string)k_Director.GetProperty("current").GetValue(host.GetComponent(k_Director));

        /// <summary>
        /// A carrier holding <paramref name="maps"/> fake lightmaps with one renderer pointed at
        /// the first of them. Left active: the director is what switches these off, and a test
        /// that handed it carriers already disabled would not be testing anything.
        /// </summary>
        static GameObject Carrier(string name, int maps, out Renderer renderer)
        {
            var go = new GameObject(name);
            go.SetActive(false);

            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(go.transform);
            renderer = child.GetComponent<Renderer>();

            Component carrier = go.AddComponent(k_Carrier);

            object entries = MakeArray(k_Carrier, "m_Entries", 1);
            SetElement(entries, 0, new (string, object)[]
            {
                ("renderer", renderer),
                ("index", 0),
                ("scaleOffset", new Vector4(1f, 1f, 0f, 0f)),
            });

            object lightmaps = MakeArray(k_Carrier, "m_Lightmaps", maps);
            for (int i = 0; i < maps; i++)
                SetElement(lightmaps, i, new (string, object)[] { ("color", Texture2D.whiteTexture) });

            Field(k_Carrier, "m_Entries").SetValue(carrier, entries);
            Field(k_Carrier, "m_Lightmaps").SetValue(carrier, lightmaps);

            // Carrying the environment is left off: these are stand-ins with no bake behind
            // them, and the point here is the lightmap array, not the fog.
            Field(k_Carrier, "m_CarryEnvironment").SetValue(carrier, false);

            go.SetActive(true);
            return go;
        }

        static FieldInfo Field(System.Type type, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        static object MakeArray(System.Type owner, string field, int length) =>
            System.Array.CreateInstance(Field(owner, field).FieldType.GetElementType(), length);

        static void Set(object element, string name, object value) =>
            element.GetType()
                   .GetField(name, BindingFlags.Instance | BindingFlags.Public)
                   .SetValue(element, value);

        static void SetElement(object array, int at, (string name, object value)[] values)
        {
            var typed = (System.Array)array;
            object element = typed.GetValue(at);

            foreach ((string name, object value) in values) Set(element, name, value);

            typed.SetValue(element, at);
        }
    }
}
