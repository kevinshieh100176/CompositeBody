#pragma warning disable CS0168 // variable is declared but never used
#pragma warning disable CS0618 // member is obsolete (with message)
#pragma warning disable CS0649 // field is never assigned (but may be set in inspector)
#pragma warning disable CS0219 // variable is assigned but its value is never used
#pragma warning disable CS0414 // field is assigned but its value is never used


using System.Collections.Generic;
using UnityEngine;


namespace KWS
{
    internal static class DebugHelpers
    {
        private static Material _debugTextureSliceMaterial;

        public static void DebugFft()
        {
            if (Event.current.type.Equals(EventType.Repaint))
            {
                if (_debugTextureSliceMaterial == null) _debugTextureSliceMaterial = KWS_CoreUtils.CreateMaterial("Hidden/_KriptoFX/KWS/Water/Debug/TextureSlice");
                if (WaterSharedResources.FftWavesDisplacement == null) return;

                var fftRT = WaterSharedResources.FftWavesDisplacement.rt;
                if (fftRT != null)
                {
                    for (int i = 0; i < fftRT.volumeDepth; i++)
                    {
                        _debugTextureSliceMaterial.SetInt("_slice", i);
                        Graphics.DrawTexture(new Rect(i * fftRT.width + i * 5, 0, fftRT.width, fftRT.height), fftRT, _debugTextureSliceMaterial, 0);
                    }
                }

                var fftNormal = WaterSharedResources.FftWavesNormal.rt;
                if (fftNormal != null)
                {
                    for (int i = 0; i < fftNormal.volumeDepth; i++)
                    {
                        _debugTextureSliceMaterial.SetInt("_slice", i);
                        Graphics.DrawTexture(new Rect(i * fftNormal.width + i * 5, fftNormal.height + 5, fftNormal.width, fftNormal.height), fftNormal, _debugTextureSliceMaterial, 1);
                    }
                }

                //var debugRT = BuoyancyPass.DebugHeightRT;
                //if (debugRT != null && fftRT != null) Graphics.DrawTexture(new Rect(0, fftRT.height * 2 + 10, debugRT.width, debugRT.height), debugRT);
            }
        }

        public static void DebugDynamicWaves()
        {
            if (Event.current.type.Equals(EventType.Repaint))
            {
                var mask   = WaterSharedResources.DynamicWavesMaskRT;
                var target = WaterSharedResources.DynamicWavesRT;
                if (mask != null && target != null)
                {
                    Graphics.DrawTexture(new Rect(0, 0,        512, 512), mask);
                    Graphics.DrawTexture(new Rect(0, 512 + 10, 512, 512), target);
                }
            }
        }

        public static void DebugOrthoDepth()
        {
            if (Event.current.type.Equals(EventType.Repaint))
            {
                var depth   = WaterSharedResources.OrthoDepth;
                if (depth != null)
                {
                    Graphics.DrawTexture(new Rect(0, 0,        512, 512), depth);
                }
            }
        }


        private static WaterSurfaceRequestList _request = new WaterSurfaceRequestList();

        public static void RequestBuoyancy()
        {
            var sizeX        = 30;
            var sizeY        = 30;
            var worldPos     = new List<Vector3>();
            var worldNormals = new List<Vector3>();
            var waterPivot   = WaterSharedResources.GlobalWaterSystem.WaterPivotWorldPosition;
            waterPivot.y =  0;
            sizeX        += (int)WaterSystem.Test4.x;
            /////////////////////////////////////////////////////////////////////////////
            int idx = 0;
            for (int x = -sizeX; x < sizeX; x++)
            {
                for (int z = -sizeY; z < sizeY; z++)
                {
                    worldPos.Add(new Vector3(x, 0, z) * 0.5f + waterPivot);
                    idx++;
                }
            }

            _request.SetNewPositions(worldPos);
            WaterSystem.TryGetWaterSurfaceData(_request);
        }


        public static void DebugBuoyancy()
        {
            RequestBuoyancy();
            for (int i = 0; i < _request.Result.Count; i++)
            {
                Gizmos.DrawCube(_request.Result[i].Position, Vector3.one * 0.1f);
                // Gizmos.DrawRay(_request.Result[i].position, Vector3.up * 0.25f);
            }

            ///////////////////////////////////////////////////////////////////////////

            //Debug.Log(WaterSystem.GetWaterSurfaceData(new Vector3(10, 0, 10)).Position);
        }


        public static void DebugQuadtree(WaterSystem waterInstance)
        {
            var quadTreeInstance = waterInstance._meshQuadTree.Instances[Camera.main];
            if (waterInstance.Settings.WaterMeshType == WaterSystemScriptableData.WaterMeshTypeEnum.FiniteBox)
            {
                foreach (var visibleNode in quadTreeInstance.VisibleNodes)
                {
                    var center = visibleNode.RotatedCenter;
                    var size   = visibleNode.ChunkSize;
                    center.y -= size.y * 0.5f;
                    Gizmos.DrawWireCube(center, size);
                }
            }
            else if (waterInstance.Settings.WaterMeshType == WaterSystemScriptableData.WaterMeshTypeEnum.InfiniteOcean)
            {
                foreach (var visibleNode in quadTreeInstance.VisibleGPUChunks)
                {
                    var center = visibleNode.Position;
                    var size   = visibleNode.Size;
                    center.y -= size.y * 0.5f;
                    Gizmos.DrawWireCube(center, size);
                }
            }
            Debug.Log($"Visible nodes {quadTreeInstance.VisibleNodes.Count}");
        }

        /*
        public static IEnumerator EvaluateMaxAmplitudeTable(WaterSystem waterInstance)
        {
            var buoyancyFps = 1 / 1000f;

            var turbulence = 0f;
            var finalText = "";
            for (int idx = 0; idx <= KWS_Settings.FFT.MaxWindSpeed; idx++)
            {
                waterInstance.Settings.GlobalWindSpeed        = idx;
                waterInstance.Settings.GlobalWindTurbulence = turbulence;
                waterInstance.ForceUpdateWaterSettings();

                float lastMaxHeight = 0;
                int   maxFrames     = (int)Mathf.Lerp(200, 2000, (1f * idx / KWS_Settings.FFT.MaxWindSpeed));
                for (int frame = 0; frame < maxFrames; frame++)
                {
                    UpdateHeight(waterInstance, ref lastMaxHeight);
                    Debug.Log($"idx {idx}  frame {frame}");
                    yield return new WaitForSeconds(buoyancyFps);
                }

                if (idx == 0) lastMaxHeight = 0.001f;
                var maxHeightText           = lastMaxHeight.ToString().Replace(',', '.');

                finalText += $"{idx}, {maxHeightText}" + Environment.NewLine;
            }

            Debug.Log("Finished " + turbulence);
            File.WriteAllText($@"D:\DebugWaterAmplitude_{waterInstance.Settings.CurrentFftWavesCascades}_{turbulence}.txt", finalText);



            //turbulence = 0.5f;
            //finalText  = "";
            //for (int idx = 0; idx <= KWS_Settings.FFT.MaxWindSpeed; idx++)
            //{
            //    waterInstance.Settings.GlobalWindSpeed        = idx;
            //    waterInstance.Settings.GlobalWindTurbulence = turbulence;
            //    waterInstance.ForceUpdateWaterSettings();

            //    float lastMaxHeight = 0;
            //    int   maxFrames     = (int)Mathf.Lerp(200, 2000, (1f * idx / KWS_Settings.FFT.MaxWindSpeed));
            //    for (int frame = 0; frame < maxFrames; frame++)
            //    {
            //        UpdateHeight(waterInstance, ref lastMaxHeight);
            //        Debug.Log($"idx {idx}  frame {frame}");
            //        yield return new WaitForSeconds(buoyancyFps);
            //    }

            //    if (idx == 0) lastMaxHeight = 0.001f;
            //    var maxHeightText           = lastMaxHeight.ToString().Replace(',', '.');

            //    finalText += $"{idx}, {maxHeightText}" + Environment.NewLine;
            //}

            Debug.Log("Finished " + turbulence);
            File.WriteAllText($@"D:\DebugWaterAmplitude_{waterInstance.Settings.CurrentFftWavesCascades}_{turbulence}.txt", finalText);

            turbulence = 1.0f;
            finalText  = "";
            for (int idx = 0; idx <= KWS_Settings.FFT.MaxWindSpeed; idx++)
            {
                waterInstance.Settings.GlobalWindSpeed        = idx;
                waterInstance.Settings.GlobalWindTurbulence = turbulence;
                waterInstance.ForceUpdateWaterSettings();

                float lastMaxHeight = 0;
                int   maxFrames     = (int)Mathf.Lerp(200, 2000, (1f * idx / KWS_Settings.FFT.MaxWindSpeed));
                for (int frame = 0; frame < maxFrames; frame++)
                {
                    UpdateHeight(waterInstance, ref lastMaxHeight);
                    Debug.Log($"idx {idx}  frame {frame}");
                    yield return new WaitForSeconds(buoyancyFps);
                }

                if (idx == 0) lastMaxHeight = 0.001f;
                var maxHeightText           = lastMaxHeight.ToString().Replace(',', '.');

                finalText += $"{idx}, {maxHeightText}" + Environment.NewLine;
            }

            Debug.Log("Finished " + turbulence);
            File.WriteAllText($@"D:\DebugWaterAmplitude_{waterInstance.Settings.CurrentFftWavesCascades}_{turbulence}.txt", finalText);
        }
        */
        //public static void UpdateHeight(WaterSystem waterInstance, ref float lastMaxHeight)
        //{
        //    WaterSystem.GetWaterSurfaceData(Vector3.zero);
        //    if (WaterSharedResources.FftGpuHeightAsyncData == null) return;

        //    var asyncData = WaterSharedResources.FftGpuHeightAsyncData;
        //    var buffer    = asyncData.CurrentBuffer();
        //    var maxValue  = buffer.Max(n => n.height);

        //    lastMaxHeight = Mathf.Max(lastMaxHeight, maxValue);
        //}


        public static void Release()
        {
            KW_Extensions.SafeDestroy(_debugTextureSliceMaterial);
        }

    }
}
