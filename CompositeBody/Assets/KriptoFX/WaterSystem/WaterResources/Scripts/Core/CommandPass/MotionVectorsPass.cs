#pragma warning disable CS0168 // variable is declared but never used
#pragma warning disable CS0618 // member is obsolete (with message)
#pragma warning disable CS0649 // field is never assigned (but may be set in inspector)
#pragma warning disable CS0219 // variable is assigned but its value is never used
#pragma warning disable CS0414 // field is assigned but its value is never used


using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace KWS
{
    internal class MotionVectorsPass : WaterPass
    {
        internal override string PassName => "Water.MotionVectorsPass";

        public MotionVectorsPass()
        {
            WaterSharedResources.OnAnyWaterSettingsChanged += OnAnyWaterSettingsChanged;
           
        }

        void InitializeTextures()
        {
           
            //this.WaterLog(WaterSharedResources.CausticRTArray);
        }

        void ReleaseTextures()
        {
           
            this.WaterLog(string.Empty, KW_Extensions.WaterLogMessageType.ReleaseRT);
        }


        public override void Release()
        {
            WaterSharedResources.OnAnyWaterSettingsChanged -= OnAnyWaterSettingsChanged;
            ReleaseTextures();

            this.WaterLog(string.Empty, KW_Extensions.WaterLogMessageType.Release);
        }

        private void OnAnyWaterSettingsChanged(WaterSystem instance, WaterSystem.WaterTab changedTabs)
        {
            //if (changedTabs.HasFlag(WaterSystem.WaterTab.Caustic))
            {
                
            }
        }

        public override void ExecuteCommandBuffer(WaterPass.WaterPassContext waterContext)
        {
           
        }

    }
}