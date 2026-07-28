using System.Collections.Generic;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    class MultiplayResources
    {
        public struct Fleet
        {
            public struct BuildConfig
            {
                public string Name;
                public string Id;

                public BuildConfig(string name, string id)
                {
                    Name = name;
                    Id = id;
                }
            }

            public struct QosRegion
            {
                public string Name;
                public string Id;

                public QosRegion(string name, string id)
                {
                    Name = name;
                    Id = id;
                }
            }

            public string Name;
            public string Id;
            public List<BuildConfig> BuildConfigs;
            public List<QosRegion> QosRegions;

            public Fleet(string name, string id, List<BuildConfig> buildConfigs, List<QosRegion> qosRegions)
            {
                Name = name;
                Id = id;
                BuildConfigs = buildConfigs;
                QosRegions = qosRegions;
            }
        }
        public List<Fleet> Fleets;

        public MultiplayResources(List<Fleet> fleets)
        {
            Fleets = fleets;
        }

        internal MultiplayResources() {}
    }
}
