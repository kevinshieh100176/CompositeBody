using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the end credits on the show timeline: the 合成肉身 logotype, then the staff list,
    /// both over the white S3 ends on.
    ///
    /// WHERE THE WHITE COMES FROM, AND WHY THIS DOES NOT MAKE IT. The Fade track already carries
    /// 「S3-6 轉白」, five seconds of black-to-white that ends at alpha 1, and
    /// <see cref="ScreenFade"/> holds its last value when nothing is driving it. So the screen is
    /// already white and stays white; the credits are two cards drawn on top of it, and the one
    /// thing this tool must not do is author a second fade that fights the first.
    ///
    /// THE START IS READ, NOT TYPED. 「開始的時間」 is five seconds after that white-out finishes,
    /// and where it finishes has moved twice as the back half was retimed -- the last commit
    /// pulled 80 seconds out of it. A start time typed in here would be right until the next
    /// retime and then would have the logotype fading up over the last of S3-6. So the end of
    /// the white-out clip is looked up every run and everything else is chained off it; re-run
    /// this after a retime and the whole end moves with it. Same argument
    /// <see cref="BuildS2S3Blackout"/> makes for the blackout it owns.
    ///
    /// ONE CLIP PER CARD, the <see cref="ScreenFadeBehaviour"/> rule: the curve is the whole
    /// in-hold-out, so a card cannot develop a gap in the middle of itself.
    ///
    /// THE FADES ARE ABSOLUTE, THE HOLD IS THE REMAINDER. The curves are rebuilt from each clip's
    /// actual duration every run, with <see cref="k_TitleIn"/> and friends in seconds. Widen a
    /// clip in the Timeline window because the list wants longer on screen and you get a longer
    /// hold, not a slower fade -- which is what a fixed normalised curve would have given you.
    ///
    /// IT TRIMS THE TAIL. Activation Track (3) runs to 1720s against a show whose last content
    /// ends at 1256 -- a clip dragged to the end of the ruler at some point, which is the only
    /// reason <see cref="TimelineAsset.duration"/> reports nearly half an hour. The show would
    /// otherwise sit on white for nearly eight minutes with nothing to show. Any activation clip
    /// running past the end of the credits is clamped to it, so the timeline's length is the
    /// piece's length and the white holds from the last card to the end.
    ///
    /// THE SCENE PART IS SEPARATE, AND IT OPENS NOTHING. <see cref="CreditsCards"/> has to exist
    /// in MainScene for the clips to drive anything, but every Build* tool in this folder opens
    /// MainScene Single and MainScene is where the retime in progress lives. So the scene is
    /// edited in place when it is already open and refused otherwise, and it is left dirty
    /// rather than saved: whether somebody else's unsaved work goes to disk is not this tool's
    /// decision.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildCredits.Run
    /// </summary>
    public static class BuildCredits
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_FadeTrack = "Fade";

        /// <summary>The white-out everything here hangs off. Owned by somebody else; read only.</summary>
        const string k_WhiteOut = "S3-6 轉白";

        const string k_Track = "Credits";
        const string k_TitleClip = "片尾 標準字";
        const string k_ListClip = "片尾 名單";

        const string k_TitleAsset = "片尾卡";
        const string k_ListAsset = "片尾卡";

        /// <summary>
        /// The logotype the piece opens on, reused unchanged. O-0's own card points at this same
        /// texture through <c>Assets/Materials/O0Title.mat</c> -- the artwork is shared, the
        /// material is not, and the remarks on <see cref="k_TitleMat"/> say why.
        /// </summary>
        const string k_TitleTex = "Assets/_Art/O0/合成肉身標準字2v.png";
        const string k_ListTex = "Assets/_Art/Credits/Credits.png";

        /// <summary>
        /// A second material for the same artwork, and not a reuse of O0Title.mat.
        ///
        /// That one is additive -- SrcAlpha/One -- which is the right answer for white type in
        /// the black onboarding room and exactly the wrong one here: adding anything to white is
        /// white, so O-0's card over the white-out would be a perfectly invisible title. These
        /// two are alpha blended, and they ignore depth and draw after the fade quad, which is
        /// the whole reason CompositeBody/Card exists.
        /// </summary>
        const string k_TitleMat = "Assets/Materials/CreditsTitle.mat";
        const string k_ListMat = "Assets/Materials/CreditsList.mat";
        const string k_CardShader = "CompositeBody/Card";

        const string k_Scene = "Assets/_Scenes/MainScene.unity";
        const string k_Root = "Experience";
        const string k_Holder = "Credits";

        /// <summary>「fade to white 後 5 秒」. Measured from the end of the white-out clip.</summary>
        const double k_AfterWhite = 5d;

        /// <summary>Title: two seconds up, six on screen, two down -- O-0's own opening timing.</summary>
        const double k_TitleLength = 10d;
        const float k_TitleIn = 2f;
        const float k_TitleOut = 2f;

        /// <summary>
        /// How wide the logotype should be across the view.
        ///
        /// NOT O-0's 24°, which is what reusing the opening card literally would give: 0.85 m at
        /// two metres, the number <c>BuildO0Timeline</c> carries. That size is right where it is
        /// — a small bright title in a black room, the first thing in the piece, with nothing
        /// else to compete with. Put the same 24° on a full white field at the end and it reads
        /// as a sticker in the middle of nowhere, and the pale cyan THE SHAPE / THAT REMAINS
        /// under it comes out around a third of a degree tall, which is not type any more.
        ///
        /// Forty-five is a title card: a bit under half the view across, the logotype itself
        /// about 37° of that, the English small but legible. Same artwork, same timing, read at
        /// the size an ending wants rather than the size an opening wanted.
        /// </summary>
        const float k_TitleInkDegrees = 45f;

        /// <summary>Used only if the logotype cannot be measured.</summary>
        const float k_TitleFallbackDegrees = 46f;

        /// <summary>Clean white between the two cards, so they do not cross-fade into each other.</summary>
        const double k_Between = 1d;

        /// <summary>
        /// List: two up, twenty-two on screen, four down. Fifteen lines of Chinese at about two
        /// degrees a line; twenty-two seconds is a steady read with a little room to go back to
        /// the top. The long tail out is the piece ending rather than a card changing.
        ///
        /// The 1106 artwork added two lines -- 演員 and 改編文本 -- to the thirteen that were
        /// here, so the same twenty-two seconds is now about 1.5 seconds a line rather than 1.7.
        /// Widening this clip is safe and costs only show length: the curves are rebuilt from
        /// whatever duration the clip has, and TrimTail moves the end of the timeline with it.
        /// </summary>
        const double k_ListLength = 28d;
        const float k_ListIn = 2f;
        const float k_ListOut = 4f;

        /// <summary>
        /// How wide the staff list's TYPE should be across the view -- the widest LINE of it,
        /// which is what <see cref="InkWidthFraction"/> measures. Twenty-six degrees puts each
        /// of the fifteen lines a little under two degrees tall, which is a comfortable read
        /// even on a headset at the low end of PCVR's angular resolution. The card itself comes
        /// out much wider than this, because the page is mostly margin: the 1106 artwork's
        /// widest line covers 32% of it, so the card lands near 72°.
        /// </summary>
        const float k_InkDegrees = 26f;

        /// <summary>Used only if the artwork cannot be measured.</summary>
        const float k_ListFallbackDegrees = 70f;

        /// <summary>
        /// How much taller than the median line a row may be and still count as type.
        ///
        /// Half again. The 1106 page is 142 rows a line and 240 for the row carrying the two
        /// funder marks, which is the gap this has to see; a page whose lines genuinely differ
        /// by less than this is one where either answer is within a few per cent. Raise it and
        /// the marks start setting the type size again; lower it much and one line with a
        /// 《》 or a parenthesis overshooting the others gets thrown out.
        /// </summary>
        const float k_BodyLineSlack = 1.5f;

        [MenuItem("Tools/Show/Build the End Credits")]
        public static void Run()
        {
            Debug.Log("[Credits] Starting...");

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            if (!TryFindWhiteOut(timeline, out double white)) return;
            if (!BuildMaterials(out Material titleMat, out Material listMat)) return;

            PlayableTrack track = EnsureTrack(timeline);

            TimelineClip title = EnsureClip(track, k_TitleClip, k_TitleAsset, CreditsCard.Title,
                                            k_TitleLength);
            TimelineClip list = EnsureClip(track, k_ListClip, k_ListAsset, CreditsCard.List,
                                           k_ListLength);

            // Chained, so the gaps stay the gaps whatever anybody does to the durations.
            title.start = white + k_AfterWhite;
            list.start = title.start + title.duration + k_Between;

            Shape(title, k_TitleIn, k_TitleOut);
            Shape(list, k_ListIn, k_ListOut);

            double end = list.start + list.duration;
            TrimTail(timeline, end);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[Credits] white-out ends {white:F1}s -> " +
                      $"'{k_TitleClip}' {title.start:F1}-{title.start + title.duration:F1}s -> " +
                      $"'{k_ListClip}' {list.start:F1}-{end:F1}s.");

            if (!WireScene(titleMat, listMat)) return;

            Debug.Log("[Credits] RESULT: PASS");
        }

        /// <summary>
        /// Where the screen finishes turning white, read off the clip that does it.
        ///
        /// Everything else here is chained off this one number, so it is worth being loud about
        /// the two ways it can be wrong: a white-out that is not actually white, and one that
        /// does not end opaque. Either would leave the credits over a live S3 set.
        /// </summary>
        static bool TryFindWhiteOut(TimelineAsset timeline, out double end)
        {
            end = 0d;

            PlayableTrack fade = null;
            foreach (TrackAsset t in timeline.GetRootTracks())
                if (t is PlayableTrack p && t.name == k_FadeTrack) fade = p;

            if (fade == null)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - no '{k_FadeTrack}' track on the " +
                               "timeline, so there is nothing to hang the end off.");
                return false;
            }

            foreach (TimelineClip clip in fade.GetClips())
            {
                if (clip.displayName != k_WhiteOut) continue;

                var asset = clip.asset as ScreenFadeClip;
                if (asset == null)
                {
                    Debug.LogError($"[Credits] RESULT: FAIL - '{k_WhiteOut}' is not a " +
                                   "ScreenFadeClip; refusing to guess where the white starts.");
                    return false;
                }

                Color c = asset.template.color;
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f)
                {
                    Debug.LogWarning($"[Credits] '{k_WhiteOut}' fades to {c}, which is not white. " +
                                     "The staff list is black type on a transparent background " +
                                     "and will be invisible on anything dark.");
                }

                float ends = asset.template.alpha.Evaluate(1f);
                if (ends < 0.99f)
                {
                    Debug.LogWarning($"[Credits] '{k_WhiteOut}' ends at alpha {ends:0.##}, not 1. " +
                                     "Nothing drives the fade after the clip, so the credits " +
                                     "would play over a partly visible S3.");
                }

                end = clip.start + clip.duration;
                Debug.Log($"[Credits] '{k_WhiteOut}' runs {clip.start:F1}-{end:F1}s, to {c}.");
                return true;
            }

            Debug.LogError($"[Credits] RESULT: FAIL - no clip called '{k_WhiteOut}' on the " +
                           $"'{k_FadeTrack}' track. The credits sit on the white that clip " +
                           "makes; without it there is nothing to measure from.");
            return false;
        }

        static PlayableTrack EnsureTrack(TimelineAsset timeline)
        {
            foreach (TrackAsset t in timeline.GetRootTracks())
                if (t is PlayableTrack p && t.name == k_Track) return p;

            Debug.Log($"[Credits] created the '{k_Track}' track.");
            return timeline.CreateTrack<PlayableTrack>(null, k_Track);
        }

        /// <summary>
        /// Find this tool's clip or make it. Only the start is re-asserted on a later run -- a
        /// duration somebody has widened because the list wants longer on screen is a decision,
        /// and <see cref="Shape"/> turns the extra into hold rather than into slower fades.
        /// </summary>
        static TimelineClip EnsureClip(PlayableTrack track, string displayName, string assetName,
                                       CreditsCard card, double length)
        {
            foreach (TimelineClip existing in track.GetClips())
                if (existing.displayName == displayName) return existing;

            TimelineClip clip = track.CreateClip<CreditsCardClip>();
            clip.displayName = displayName;
            clip.duration = length;

            var asset = (CreditsCardClip)clip.asset;
            asset.name = assetName;
            asset.template.card = card;

            Debug.Log($"[Credits] created '{displayName}' ({card}), {length:F0}s.");
            return clip;
        }

        /// <summary>
        /// Rebuild a card's curve from its own duration: up over <paramref name="fadeIn"/>
        /// seconds, flat, down over <paramref name="fadeOut"/>.
        ///
        /// Flat tangents at every key, which is what makes the rise and fall ease and the hold
        /// actually hold -- the same four-key shape the blackouts on the Fade track use. Both
        /// ends are 0 on purpose: nothing drives a card outside its clip, so a curve that ended
        /// high would leave it on the white for the rest of the show.
        /// </summary>
        static void Shape(TimelineClip clip, float fadeIn, float fadeOut)
        {
            var asset = (CreditsCardClip)clip.asset;
            float length = (float)clip.duration;

            if (fadeIn + fadeOut >= length)
            {
                Debug.LogWarning($"[Credits] '{clip.displayName}' is only {length:F1}s, which is " +
                                 $"shorter than its {fadeIn:F0}s up plus {fadeOut:F0}s down. The " +
                                 "card will fade in and straight back out with no hold.");
                fadeIn = Mathf.Min(fadeIn, length * 0.5f);
                fadeOut = Mathf.Min(fadeOut, length * 0.5f);
            }

            asset.template.alpha = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(fadeIn / length, 1f, 0f, 0f),
                new Keyframe((length - fadeOut) / length, 1f, 0f, 0f),
                new Keyframe(1f, 0f, 0f, 0f));

            EditorUtility.SetDirty(asset);

            Debug.Log($"[Credits] '{clip.displayName}': {fadeIn:F0}s up, " +
                      $"{length - fadeIn - fadeOut:F0}s on screen, {fadeOut:F0}s down.");
        }

        /// <summary>
        /// Clamp anything that runs past the end of the credits.
        ///
        /// Activation Track (3) ends at 1720s against a show whose last content ends at 1256 --
        /// somebody dragged a clip to the end of the ruler, and that number alone is what makes
        /// TimelineAsset.duration report 28 minutes. The driver clamps show time to the duration,
        /// so the piece currently finishes on seven and a half minutes of white with nothing in
        /// it. Only activation clips are touched: the sections they switch on are invisible
        /// behind the white by then, and nothing else on the timeline runs this late.
        /// </summary>
        static void TrimTail(TimelineAsset timeline, double end)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is not ActivationTrack) continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    double was = clip.start + clip.duration;
                    if (was <= end + 0.001d) continue;

                    if (clip.start >= end)
                    {
                        Debug.LogWarning($"[Credits] '{track.name}' has a clip starting at " +
                                         $"{clip.start:F1}s, after the credits end at {end:F1}s. " +
                                         "Left alone; a tool that shortens a clip to nothing is " +
                                         "deleting it by the back door.");
                        continue;
                    }

                    clip.duration = end - clip.start;
                    Debug.Log($"[Credits] trimmed '{track.name}' from {was:F1}s to {end:F1}s.");
                }
            }
        }

        static bool BuildMaterials(out Material title, out Material list)
        {
            title = null;
            list = null;

            var shader = Shader.Find(k_CardShader);
            if (shader == null)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - no shader '{k_CardShader}'. It should " +
                               "be at Assets/Shaders/Card.shader.");
                return false;
            }

            // The logotype is O-0's artwork and this tool changes nothing about it EXCEPT the one
            // import setting that is squashing it; see Unsquash.
            Unsquash(k_TitleTex, "the O-0 opening card shows the same artwork and is corrected " +
                                 "along with the end card");
            Unsquash(k_ListTex, "the staff list is type and a 12.5% squash is legible as a " +
                                "distortion");
            ImportList();

            title = EnsureMaterial(k_TitleMat, shader, k_TitleTex);
            list = EnsureMaterial(k_ListMat, shader, k_ListTex);
            return title != null && list != null;
        }

        /// <summary>
        /// Stop Unity resampling a card's artwork to the nearest power of two.
        ///
        /// THIS IS NOT A TUNING KNOB, it is a distortion. The default npotScale is ToNearest,
        /// which resizes each axis independently: the logotype's 918×272 becomes 1024×256, so
        /// 合成肉身 is drawn 18.5% flatter than it was designed, and the staff list's 1920×1080
        /// would become 2048×1024 and lose 12.5% of its height. Both cards size themselves from
        /// their texture's proportions, so the squash is in the geometry and no amount of
        /// scaling the quad takes it out.
        ///
        /// It costs nothing here. Both files have sides divisible by four, so they still
        /// block-compress at their own size; a file that does not would fall back to
        /// uncompressed, which for a single card is a megabyte and worth it.
        ///
        /// Said out loud every time it changes something, because for the logotype it also
        /// changes a beat this tool does not own: O-0's opening title is the same texture, and
        /// correcting the end card corrects the opening one with it.
        /// </summary>
        static void Unsquash(string texPath, string why)
        {
            if (AssetImporter.GetAtPath(texPath) is not TextureImporter importer) return;
            if (importer.npotScale == TextureImporterNPOTScale.None) return;

            var before = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            string was = before != null ? $"{before.width}x{before.height}" : "?";

            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var after = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            string now = after != null ? $"{after.width}x{after.height}" : "?";

            Debug.LogWarning($"[Credits] {texPath} was being imported at {was} and is now {now} -- " +
                             $"Unity's npotScale was resampling each axis to the nearest power of " +
                             $"two, which squashes the artwork. Changed because {why}.");
        }

        /// <summary>
        /// The staff list's own import settings. This tool owns that file; the logotype belongs
        /// to O-0 and nothing but <see cref="Unsquash"/> touches it.
        ///
        /// Clamped, not repeated: the card is one page, and a sample that wraps at its edge
        /// brings the opposite margin round with it. BC7 rather than the DXT5 Unity picks by
        /// default -- the artwork is pure black type whose every detail lives in the alpha
        /// channel, which is exactly where DXT5's four-bit interpolated block shows as fringing
        /// on glyph edges. Same eight bits a pixel, visibly better on type.
        /// </summary>
        static void ImportList()
        {
            if (AssetImporter.GetAtPath(k_ListTex) is not TextureImporter importer) return;

            bool changed = false;
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (!importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }

            var standalone = importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden || standalone.format != TextureImporterFormat.BC7)
            {
                standalone.overridden = true;
                standalone.maxTextureSize = 2048;
                standalone.format = TextureImporterFormat.BC7;
                standalone.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SetPlatformTextureSettings(standalone);
                changed = true;
            }

            if (!changed) return;

            importer.SaveAndReimport();
            Debug.Log($"[Credits] imported {k_ListTex}: clamp, sRGB, alpha is transparency, BC7 on Standalone.");
        }

        /// <summary>One card material, pointed at its artwork.</summary>
        static Material EnsureMaterial(string path, Shader shader, string texPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - no texture at {texPath}.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
                Debug.Log($"[Credits] created {path}.");
            }

            if (mat.shader != shader) mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);

            // CardFade pushes the fade through a property block on _BaseColor; the material's own
            // value is the starting point, so it is full white and the artwork is untinted.
            mat.SetColor("_BaseColor", Color.white);

            // -1 is "whatever the shader's queue tag says", which is Overlay+100 -- one queue
            // later than the fade quad, which is the only thing making these visible at all.
            mat.renderQueue = -1;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// How wide a card has to be for the ARTWORK on it to come out
        /// <paramref name="inkDegrees"/> across, measured off the file rather than typed in.
        ///
        /// WHY THIS IS NOT JUST A NUMBER IN THE INSPECTOR. What matters is how big the names are
        /// in the headset, and the card is not the names: the staff list is a 16:9 sheet whose
        /// type only covers the middle 31%, so a card sized by eye sizes the margins and the
        /// lines come out wherever they land. Measuring the ink means a redrawn page with
        /// different margins still reads at the same size, instead of silently shrinking the
        /// names the day somebody re-exports it. The logotype is a tight crop and comes out
        /// near enough its own card either way -- it goes through the same arithmetic so the
        /// two numbers mean the same thing, and so a future logotype with air around it does
        /// not quietly shrink.
        ///
        /// Worked in tangent space, not degrees, because a card is a flat quad: the ink's share
        /// of the card's WIDTH is constant, its share of the ANGLE is not, and treating the two
        /// as the same thing is wrong by several degrees at this size.
        ///
        /// Read from the file on disk rather than the imported Texture2D, so this does not care
        /// whether the asset happens to be marked readable.
        /// </summary>
        static float CardWidthDegrees(string texPath, float inkDegrees, float fallback, string what)
        {
            float fraction = InkWidthFraction(texPath);
            if (fraction <= 0f)
            {
                Debug.LogWarning($"[Credits] could not measure any artwork in {texPath}; the " +
                                 $"{what} card is left at {fallback:F0}°.");
                return fallback;
            }

            float half = Mathf.Tan(inkDegrees * 0.5f * Mathf.Deg2Rad) / fraction;
            float degrees = 2f * Mathf.Atan(half) * Mathf.Rad2Deg;
            degrees = Mathf.Clamp(degrees, 10f, 110f);

            Debug.Log($"[Credits] the {what} covers {fraction * 100f:F1}% of its page's width, so " +
                      $"a {degrees:F1}° card puts it {inkDegrees:F0}° across the view.");
            return degrees;
        }

        /// <summary>
        /// How much of a card's width its artwork actually marks, 0 to 1: the width of the
        /// widest LINE OF TYPE on it, not of the whole page's bounding box.
        ///
        /// THE DIFFERENCE IS THE LOGOS. On a page that is nothing but centred type the two
        /// measures are the same number -- the staff list this replaced came out at 30.5%
        /// either way, and so does the logotype, which is one line all by itself. The 1106
        /// artwork ends on 「補助單位｜」 followed by the 文化部 and 國立臺灣文學館 marks,
        /// which run most of the way to the right edge and sit well off centre. As one bounding
        /// box that page is 58% ink; as lines, its widest line is the 48% the logos occupy.
        /// Either number sizes the MARKS to <see cref="k_InkDegrees"/> and leaves the names at
        /// 1.1° to 1.3° a line, half the size they had been -- precisely the silent shrink
        /// <see cref="CardWidthDegrees"/> exists to stop.
        ///
        /// SO THE LOGO ROW IS NOT A LINE OF TYPE, and the page says so itself: it is 240 rows
        /// tall where every line of names is 142. The lines are measured, the median line
        /// height is taken as what a line of this page's type looks like, and anything more
        /// than <see cref="k_BodyLineSlack"/> times that is a device on a row of its own rather
        /// than a line to be read -- free to be wider than the names without dragging them
        /// down, since the card is centred on the page and the names are centred on it too.
        ///
        /// A LINE here is a run of consecutive rows with any ink in them, which is all the
        /// structure these cards have: leading separates the lines and nothing else does.
        /// </summary>
        static float InkWidthFraction(string path)
        {
            string full = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, path);
            if (!System.IO.File.Exists(full)) return 0f;

            var probe = new Texture2D(2, 2);
            try
            {
                if (!probe.LoadImage(System.IO.File.ReadAllBytes(full))) return 0f;

                Color32[] px = probe.GetPixels32();
                int w = probe.width, h = probe.height;

                var widths = new List<int>();
                var heights = new List<int>();

                int min = w, max = -1, top = -1;

                // One row past the bottom, so artwork that runs to the edge still closes its
                // last line instead of leaving it unmeasured.
                for (int y = 0; y <= h; y++)
                {
                    bool inked = false;

                    if (y < h)
                    {
                        int row = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            // A tenth of full alpha: antialiasing tapers to nothing at a
                            // glyph's edge, and a threshold of zero measures the taper
                            // instead of the type.
                            if (px[row + x].a < 26) continue;
                            inked = true;
                            if (x < min) min = x;
                            if (x > max) max = x;
                        }
                    }

                    if (inked)
                    {
                        if (top < 0) top = y;
                        continue;
                    }

                    // A blank row ends the line. Bank it and start the next one empty.
                    if (max >= min)
                    {
                        widths.Add(max - min + 1);
                        heights.Add(y - top);
                    }

                    min = w;
                    max = -1;
                    top = -1;
                }

                if (widths.Count == 0) return 0f;

                var ordered = new List<int>(heights);
                ordered.Sort();
                int body = ordered[ordered.Count / 2];
                int tallest = Mathf.CeilToInt(body * k_BodyLineSlack);

                int widest = 0;
                for (int i = 0; i < widths.Count; i++)
                    if (heights[i] <= tallest && widths[i] > widest)
                        widest = widths[i];

                // The median line always passes that test, so this cannot come back empty.
                return widest / (float)w;
            }
            finally { Object.DestroyImmediate(probe); }
        }

        /// <summary>
        /// Put <see cref="CreditsCards"/> in MainScene, in the scene that is already open.
        ///
        /// Refuses to open it. Every other Build* tool here calls OpenScene Single and takes the
        /// open scene with it; MainScene is where the back half is being retimed and that work is
        /// unsaved as often as not. Left dirty rather than saved for the same reason -- what else
        /// is in the scene right now is not this tool's business.
        /// </summary>
        static bool WireScene(Material titleMat, Material listMat)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_Scene)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - the timeline is built, but the open " +
                               $"scene is '{scene.path}' and the CreditsCards holder belongs in " +
                               $"{k_Scene}. Open MainScene and run this again; nothing else will " +
                               "be done twice.");
                return false;
            }

            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == k_Root) root = go;

            if (root == null)
            {
                Debug.LogError($"[Credits] RESULT: FAIL - no '{k_Root}' root in MainScene. " +
                               "ScreenFade and LightingDirector live under it and the credits " +
                               "holder belongs beside them.");
                return false;
            }

            Transform holder = root.transform.Find(k_Holder);
            if (holder == null)
            {
                var go = new GameObject(k_Holder);
                Undo.RegisterCreatedObjectUndo(go, "Create Credits");
                go.transform.SetParent(root.transform, false);
                holder = go.transform;
                Debug.Log($"[Credits] created {k_Root}/{k_Holder}.");
            }

            var cards = holder.GetComponent<CreditsCards>();
            if (cards == null)
            {
                cards = Undo.AddComponent<CreditsCards>(holder.gameObject);
                Debug.Log("[Credits] added CreditsCards.");
            }

            var so = new SerializedObject(cards);
            so.FindProperty("m_TitleMaterial").objectReferenceValue = titleMat;
            so.FindProperty("m_ListMaterial").objectReferenceValue = listMat;
            so.FindProperty("m_TitleWidthDegrees").floatValue =
                CardWidthDegrees(k_TitleTex, k_TitleInkDegrees, k_TitleFallbackDegrees, "logotype");
            so.FindProperty("m_ListWidthDegrees").floatValue =
                CardWidthDegrees(k_ListTex, k_InkDegrees, k_ListFallbackDegrees, "type");
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Credits] wired {k_Root}/{k_Holder} to {titleMat.name} and {listMat.name}. " +
                      "MainScene is dirty and NOT saved -- save it yourself once you have " +
                      "looked at what else is pending in it.");
            return true;
        }

        /// <summary>
        /// Read the end back off the asset and print it, so a run can be judged without opening
        /// the Timeline window. Track names and clip times do not pair up in the .playable file's
        /// own order, so grepping it tells you nothing.
        /// </summary>
        [MenuItem("Tools/Show/Report the End Credits")]
        public static void Report()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[Credits] no TimelineAsset at {k_Timeline}.");
                return;
            }

            var lines = new List<string>
            {
                $"[Credits] {k_Timeline} duration {timeline.duration:F1}s"
            };

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.start + clip.duration < 1200d) continue;

                    string extra = "";
                    if (clip.asset is CreditsCardClip card)
                    {
                        var k = card.template.alpha.keys;
                        extra = $"  {card.template.card}, keys " + string.Join(" ",
                            System.Array.ConvertAll(k, f => $"{f.time:0.###}:{f.value:0.##}"));
                    }
                    else if (clip.asset is ScreenFadeClip fade)
                    {
                        extra = $"  -> {fade.template.color}";
                    }

                    lines.Add($"[Credits] {clip.start,8:F1} - {clip.start + clip.duration,8:F1}  " +
                              $"[{track.name}] {clip.displayName}{extra}");
                }
            }

            lines.Sort(1, lines.Count - 1, System.StringComparer.Ordinal);
            Debug.Log(string.Join("\n", lines));
        }
    }
}
