using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace UnityEditor.XR.OpenXR.Features.Android
{
    internal static class XRFeatureInfoProvider
    {
        internal struct FeatureInfo : IEquatable<FeatureInfo>
        {
            internal string FeatureName { get; }
            internal string ExtensionsString { get; }

            public FeatureInfo(string featureName, string extensionsString)
            {
                FeatureName = featureName;
                ExtensionsString = extensionsString;
            }

            public bool Equals(FeatureInfo other)
            {
                return FeatureName == other.FeatureName && ExtensionsString == other.ExtensionsString;
            }

            public override bool Equals(object obj)
            {
                return obj is FeatureInfo other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(FeatureName, ExtensionsString);
            }
        }

        internal static Dictionary<string, FeatureInfo> GetFeatureInfos<TFeatureType>(
            Func<TFeatureType, OpenXRFeatureAttribute, bool> filter = null,
            BuildTargetGroup buildTargetGroup = BuildTargetGroup.Android)
            where TFeatureType : OpenXRFeature
        {
            var openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            var features = new List<TFeatureType>();
            openXRSettings.GetFeatures(features);
            var featureInfos = new Dictionary<string, FeatureInfo>();

            foreach (var feature in features)
            {
                if (feature == null || !feature.enabled)
                    continue;

                var attribute = feature.GetType().GetCustomAttribute<OpenXRFeatureAttribute>();

                if (attribute == null ||
                    string.IsNullOrEmpty(attribute.OpenxrExtensionStrings) ||
                    string.IsNullOrEmpty(attribute.FeatureId))
                {
                    continue;
                }

                if (filter != null && !filter(feature, attribute))
                {
                    continue;
                }

                featureInfos[attribute.FeatureId] = new FeatureInfo(attribute.UiName, attribute.OpenxrExtensionStrings);
            }

            return featureInfos;
        }
    }
}
