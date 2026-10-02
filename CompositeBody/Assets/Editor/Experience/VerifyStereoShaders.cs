using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Checks every hand-written shader in the project compiles and supports Single Pass
    /// Instanced stereo rendering.
    ///
    /// The project renders XR in Single Pass Instanced mode: both eyes are drawn in one pass as
    /// two instances, and a shader that does not declare the instancing input, carry the eye
    /// index to the fragment stage and set it there renders to one eye only. That failure is
    /// invisible everywhere except inside a headset, and from in there it reads as a broken
    /// display rather than as a shader problem -- which is why it is worth a harness rather than
    /// a hardware test.
    ///
    /// Each pass must declare all five macros. They are checked per HLSLPROGRAM block, because a
    /// shader whose colour pass is correct and whose depth pass is not still renders wrongly.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.VerifyStereoShaders.Run
    /// </summary>
    public static class VerifyStereoShaders
    {
        const string k_ShaderDir = "Assets/Shaders";

        static readonly (string macro, string where)[] k_Required =
        {
            ("UNITY_VERTEX_INPUT_INSTANCE_ID", "the vertex input struct"),
            ("UNITY_VERTEX_OUTPUT_STEREO", "the vertex output struct"),
            ("UNITY_SETUP_INSTANCE_ID", "the top of the vertex function"),
            ("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO", "the top of the vertex function"),
            ("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX", "the top of the fragment function"),
        };

        static int s_Checks;
        static int s_Failures;

        public static void Run()
        {
            Debug.Log("[Stereo] Starting...");
            s_Checks = 0;
            s_Failures = 0;

            string[] files = Directory.GetFiles(k_ShaderDir, "*.shader", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                Debug.LogError($"[Stereo] RESULT: FAIL - no shaders found under {k_ShaderDir}.");
                return;
            }

            foreach (string file in files)
            {
                string path = file.Replace('\\', '/');
                Debug.Log($"[Stereo] --- {Path.GetFileName(path)} ---");
                CheckCompiles(path);
                CheckMacros(path);
            }

            Debug.Log($"[Stereo] {files.Length} shader(s), {s_Checks} checks, {s_Failures} failed.");
            Debug.Log(s_Failures == 0 ? "[Stereo] RESULT: PASS" : "[Stereo] RESULT: FAIL");
        }

        static void CheckCompiles(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (!Check(shader != null, $"{Path.GetFileName(path)} loads as a shader")) return;

            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Stereo]   {message.severity} line {message.line}: {message.message}");
            }

            Check(!ShaderUtil.ShaderHasError(shader), $"{shader.name} compiles without errors");
        }

        static void CheckMacros(string path)
        {
            string text = File.ReadAllText(path);

            var blocks = new List<string>();
            foreach (Match match in Regex.Matches(text, @"HLSLPROGRAM(.*?)ENDHLSL", RegexOptions.Singleline))
                blocks.Add(match.Groups[1].Value);

            if (!Check(blocks.Count > 0, $"{Path.GetFileName(path)} has at least one HLSL pass")) return;

            for (int i = 0; i < blocks.Count; i++)
            {
                string block = blocks[i];
                string label = blocks.Count == 1 ? "pass" : $"pass {i + 1}/{blocks.Count}";

                foreach (var (macro, where) in k_Required)
                    Check(block.Contains(macro), $"{label} declares {macro} in {where}");

                Check(block.Contains("multi_compile_instancing"),
                    $"{label} has #pragma multi_compile_instancing");
            }
        }

        static bool Check(bool condition, string description)
        {
            s_Checks++;
            if (condition)
            {
                Debug.Log($"[Stereo] ok   {description}");
                return true;
            }

            s_Failures++;
            Debug.LogError($"[Stereo] FAILED: {description}");
            return false;
        }
    }
}
