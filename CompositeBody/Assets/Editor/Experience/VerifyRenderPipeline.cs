using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Reports the render pipeline actually in use, the renderer it points at, and every active
    /// feature on it.
    ///
    /// WHY THIS EXISTS. Disabling a renderer feature and then checking that same feature is
    /// inactive proves nothing: the question is whether that renderer is in the chain at all.
    /// Importing AERO repointed URP-Performant's renderer list at the package's own demo renderer,
    /// so for a while this project rendered through PC_Renderer -- AERO's fog, SSAO and an extra
    /// light-count pass -- while URP-Performant-Renderer sat unused. A feature switched off on an
    /// unused renderer looks exactly like a feature switched off.
    ///
    /// FAILS if a renderer outside the project's own Settings folder is in the chain, because a
    /// package's demo renderer carries demo settings and will be silently replaced the next time
    /// that package updates.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyRenderPipeline.Run
    /// </summary>
    public static class VerifyRenderPipeline
    {
        const string k_OwnSettings = "Assets/VRMPAssets/Settings/";

        [MenuItem("Tools/Verify/Render Pipeline")]
        public static void Run()
        {
            Debug.Log("[Pipeline] Starting...");

            // QualitySettings wins when the active level overrides it; otherwise the project
            // default. This is the same order URP resolves at runtime.
            RenderPipelineAsset active = QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline
                : GraphicsSettings.defaultRenderPipeline;

            if (active == null)
            {
                Debug.LogError("[Pipeline] RESULT: FAIL - no render pipeline asset is assigned.");
                return;
            }

            string pipelinePath = AssetDatabase.GetAssetPath(active);
            var report = new StringBuilder();
            report.AppendLine($"[Pipeline] active asset: {pipelinePath}");

            var so = new SerializedObject(active);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray || list.arraySize == 0)
            {
                Debug.LogError($"[Pipeline] RESULT: FAIL - {pipelinePath} has no renderers.");
                return;
            }

            bool foreign = false;
            int totalActive = 0;

            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue
                    as ScriptableRendererData;
                string path = data != null ? AssetDatabase.GetAssetPath(data) : "<missing>";

                bool mine = path.StartsWith(k_OwnSettings);
                if (!mine) foreign = true;

                report.AppendLine($"[Pipeline]   renderer[{i}]: {path}" +
                                  (mine ? string.Empty : "   <-- NOT the project's own renderer"));

                if (data == null) continue;

                if (data.rendererFeatures.Count == 0)
                {
                    report.AppendLine("[Pipeline]       (no features)");
                    continue;
                }

                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if (feature == null)
                    {
                        report.AppendLine("[Pipeline]       <null feature>");
                        continue;
                    }

                    bool on = feature.isActive;
                    if (on) totalActive++;
                    report.AppendLine($"[Pipeline]       [{(on ? "ON " : "off")}] " +
                                      $"{feature.name}  ({feature.GetType().Name})");
                }
            }

            report.AppendLine($"[Pipeline] {totalActive} active feature(s) in the chain.");
            report.AppendLine(totalActive == 0
                ? "[Pipeline] no fullscreen pass, so no intermediate target and no MSAA resolve."
                : "[Pipeline] at least one feature is live; expect an intermediate colour target " +
                  "and an MSAA resolve per eye.");

            if (foreign)
            {
                report.AppendLine($"[Pipeline] RESULT: FAIL - a renderer outside {k_OwnSettings} " +
                                  "is in the chain. Point the pipeline asset back at the " +
                                  "project's own renderer.");
                Debug.LogError(report.ToString());
                return;
            }

            report.Append("[Pipeline] RESULT: PASS");
            Debug.Log(report.ToString());
        }
    }
}
