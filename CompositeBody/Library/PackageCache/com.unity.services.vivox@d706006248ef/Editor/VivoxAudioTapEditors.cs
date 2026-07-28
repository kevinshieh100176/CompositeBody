using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Services.Vivox.AudioTaps;
using System.Linq;

namespace Unity.Services.Vivox.Editor
{
    internal abstract class AudioTapEditor : UnityEditor.Editor
    {
        private const float kSingleLineHeight = 18f;
        private static Rect s_LastRect;
        private static Texture2D s_HorizontalVUTexture;
        private SmoothingData[] m_data;
        private const float VU_SPLIT = 0.9f;
        private static string status;
        private static MessageType statusType;

        private struct SmoothingData
        {
            public float lastValue;
            public float peakValue;
            public float peakValueTime;
        }

        protected virtual bool ShowAudioFilterGUI => true;
        protected virtual bool ShowTapStatusGUI => true;
        protected virtual bool ShowTapAutoAcquireGUI => true;

        private static Texture2D horizontalVUTexture
        {
            get
            {
                if (s_HorizontalVUTexture == null)
                {
                    s_HorizontalVUTexture = EditorGUIUtility.Load("VUMeterTextureHorizontal.png") as Texture2D;
                }
                return s_HorizontalVUTexture;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.DrawInspectorExcept("m_Script");
            var behaviour = target as MonoBehaviour;
            if (behaviour == null)
            {
                return;
            }

            if (ShowTapAutoAcquireGUI)
            {
                DrawTapAutoAcquireGUI(behaviour);
            }

            if (ShowTapStatusGUI)
            {
                DrawTapStatusGUI(behaviour);
            }

            if (ShowAudioFilterGUI)
            {
                DrawAudioFilterGUI(behaviour);
            }
        }


        private static void DrawTapAutoAcquireGUI(MonoBehaviour behaviour)
        {
            if (behaviour is VivoxAudioTap tap)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    tap.AutoAcquireChannel = EditorGUILayout.Toggle(VivoxAudioTap.AutoAcquireChannelLabel, tap.AutoAcquireChannel);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawTapStatusGUI(MonoBehaviour behaviour)
        {
            if (behaviour is VivoxAudioTap tap)
            {
                EditorGUILayout.BeginHorizontal();
                {
                    if (tap.Status != status)
                    {
                        status = tap.Status;
                        statusType = MessageType.Info;
                        if (tap.Status.ToLower().Contains("failed:"))
                        {
                            statusType = MessageType.Error;
                        }
                        else if (tap.Status.ToLower().Contains("warning:"))
                        {
                            statusType = MessageType.Warning;
                        }
                    }
                    EditorStyles.helpBox.fontSize = EditorStyles.toggle.fontSize;
                    EditorGUILayout.HelpBox($" Status: {tap.Status}", statusType);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawAudioFilterGUI(MonoBehaviour behaviour)
        {
            int channelCount = 0;
            float maxData = 0;
            if (behaviour is VivoxAudioTap tap)
            {
                channelCount = tap.ChannelCount;
                maxData = tap.MeterMaxData;
            }

            if (channelCount > 0)
            {
                if (m_data == null || m_data.Length != channelCount)
                {
                    m_data = new SmoothingData[channelCount];
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(13);
                GUILayout.BeginVertical();
                EditorGUILayout.Space();
                for (int c = 0; c < channelCount; ++c)
                {
                    VUMeterHorizontal(maxData, ref m_data[c], GUILayout.MinWidth(50), GUILayout.Height(5));
                }
                GUILayout.EndVertical();

                // Room for a text read-out here

                GUILayout.EndHorizontal();
                EditorGUILayout.Space();

                // force repaint
                Repaint();
            }
        }

        private static void VUMeterHorizontal(float value, ref SmoothingData data, params GUILayoutOption[] options)
        {
            Rect r = s_LastRect = EditorGUILayout.GetControlRect(false, kSingleLineHeight, EditorStyles.numberField, options);
            HorizontalMeter(r, value, ref data, horizontalVUTexture, Color.grey);
        }

        private static void HorizontalMeter(Rect position, float value, float peak, Texture2D foregroundTexture, Color peakColor)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color temp = GUI.color;

            // Draw background
            EditorStyles.helpBox.Draw(position, false, false, false, false); // progressBarBack

            // Draw foreground
            GUI.color = new Color(1f, 1f, 1f, GUI.enabled ? 1 : 0.5f);
            float width = position.width * value - 2;
            if (width < 2)
                width = 2;
            Rect newRect = new Rect(position.x + 1, position.y + 1, width, position.height - 2);
            Rect uvRect = new Rect(0, 0, value, 1);
            GUI.DrawTextureWithTexCoords(newRect, foregroundTexture, uvRect);

            // Draw peak indicator
            GUI.color = peakColor;
            float peakpos = position.width * peak - 2;
            if (peakpos < 2)
                peakpos = 2;
            newRect = new Rect(position.x + peakpos, position.y + 1, 1, position.height - 2);
            GUI.DrawTexture(newRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill);

            // Reset color
            GUI.color = temp;
        }

        // Auto smoothing version
        private static void HorizontalMeter(Rect position, float value, ref SmoothingData data, Texture2D foregroundTexture, Color peakColor)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            float renderValue, renderPeak;
            SmoothVUMeterData(ref value, ref data, out renderValue, out renderPeak);
            HorizontalMeter(position, renderValue, renderPeak, foregroundTexture, peakColor);
        }

        private static void SmoothVUMeterData(ref float value, ref SmoothingData data, out float renderValue, out float renderPeak)
        {
            if (value <= data.lastValue)
            {
                value = Mathf.Lerp(data.lastValue, value, Time.smoothDeltaTime * 7.0f);
            }
            else
            {
                value = Mathf.Lerp(value, data.lastValue, Time.smoothDeltaTime * 2.0f);
                data.peakValue = value;
                data.peakValueTime = Time.realtimeSinceStartup;
            }

            if (value > 1.0f / VU_SPLIT)
                value = 1.0f / VU_SPLIT;
            if (data.peakValue > 1.0f / VU_SPLIT)
                data.peakValue = 1.0f / VU_SPLIT;

            renderValue = value * VU_SPLIT;
            renderPeak = data.peakValue * VU_SPLIT;

            data.lastValue = value;
        }
    }

#if VIVOX_ENABLE_CAPTURE_SINK_TAP
    [CustomEditor(typeof(VivoxCaptureSinkTap))]
    [CanEditMultipleObjects]
    public class VivoxCaptureSinkTapEditor : AudioTapEditor
    {
    }
#endif

    [CustomEditor(typeof(VivoxCaptureSourceTap))]
    [CanEditMultipleObjects]
    internal class VivoxCaptureSourceTapEditor : AudioTapEditor
    {
    }

    [CustomEditor(typeof(VivoxChannelAudioTap))]
    [CanEditMultipleObjects]
    internal class VivoxChannelAudioEditor : AudioTapEditor
    {
    }

    [CustomEditor(typeof(VivoxParticipantTap))]
    [CanEditMultipleObjects]
    internal class VivoxParticipantTapEditor : AudioTapEditor
    {
        protected override bool ShowTapAutoAcquireGUI => false;
    }

    internal static class SerializedObjectHelpers
    {
        public static void DrawInspectorExcept(this SerializedObject serializedObject, params string[] fieldsToSkip)
        {
            serializedObject.Update();
            SerializedProperty prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (fieldsToSkip.Any(prop.name.Contains))
                        continue;

                    EditorGUILayout.PropertyField(serializedObject.FindProperty(prop.name), true);
                }
                while (prop.NextVisible(false));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
