// WARNING: Auto generated code. Modifications will be lost!
#if UNITY_2023_2_OR_NEWER
using Unity.Services.Multiplayer.Authoring.Editor.Shared.Analytics;
using UnityEngine.Analytics;

namespace Unity.Services.Multiplayer.Editor.Shared.Analytics
{
    class CommonAnalyticProvider : ICommonAnalyticProvider
    {
        public IAnalytic GetAnalytic(ICommonAnalytics.CommonEventPayload payload)
        {
            return new CommonAnalyticEvent(payload);
        }
    }
}
#endif
