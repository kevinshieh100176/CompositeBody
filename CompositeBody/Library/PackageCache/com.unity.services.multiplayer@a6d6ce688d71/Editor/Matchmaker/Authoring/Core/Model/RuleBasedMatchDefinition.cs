using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    class RuleBasedMatchDefinition
    {
        [DataMember(IsRequired = true)] public List<RuleBasedTeamDefinition> teams = new();
        [DataMember(IsRequired = false)] public List<Rule> matchRules = new();
    }

    class RuleBasedTeamDefinition
    {
        [DataMember(IsRequired = true)] public string name;
        [DataMember(IsRequired = true)] public Range teamCount;
        [DataMember(IsRequired = true)] public Range playerCount;
        [DataMember(IsRequired = false)] public List<Rule> teamRules = new();
    }

    class Range
    {
        [DataMember(IsRequired = true)] public int min;

        [DataMember(IsRequired = true)] public int max;

        [DataMember(IsRequired = false)] public List<RangeRelaxation> relaxations = new();
    }

    class RangeRelaxation
    {
        [DataMember(IsRequired = true)] public RangeRelaxationType type;
        [DataMember(IsRequired = true)] public AgeType ageType;
        [DataMember(IsRequired = true)] public double atSeconds;
        [DataMember(IsRequired = true)] public double value;
    }

    enum RangeRelaxationType
    {
        [DataMember(Name = "RangeControl.ReplaceMin")]
        RangeControlReplaceMin
    }

    enum AgeType
    {
        Youngest,
        Oldest,
        Average
    }

    [Serializable]
    class Rule
    {
        [DataMember(IsRequired = true)] public string source;
        [DataMember(IsRequired = true)] public string name;
        [DataMember(IsRequired = true)] public RuleType type;
        [DataMember(IsRequired = false)] public JsonObject reference;
        [DataMember(IsRequired = false)] public double overlap;
        [DataMember(IsRequired = false)] public bool enableRule;
        [DataMember(IsRequired = false)] public bool not;
        [DataMember(IsRequired = false)] public List<RuleRelaxation> relaxations = new();
        [DataMember(IsRequired = false)] public RuleExternalData externalData;
    }

    [Serializable]
    class RuleRelaxation
    {
        [DataMember(IsRequired = true)] public RuleRelaxationType type;
        [DataMember(IsRequired = true)] public AgeType ageType;
        [DataMember(IsRequired = true)] public double atSeconds;
        [DataMember(IsRequired = false)] public JsonObject value;
    }

    enum RuleRelaxationType
    {
        [DataMember(Name = "RuleControl.Enable")]
        RuleControlEnable,
        [DataMember(Name = "RuleControl.Disable")]
        RuleControlDisable,
        [DataMember(Name = "ReferenceControl.Replace")]
        ReferenceControlReplace,
    }

    enum RuleType
    {
        LessThan,
        LessThanEqual,
        GreaterThan,
        GreaterThanEqual,
        Difference,
        Equality,
        DoubleDifference,
        InList,
        Intersection
    }

    [Serializable]
    class RuleExternalData
    {
        [Serializable]
        internal class Leaderboard
        {
            [DataMember(IsRequired = true)] public string id;
        }

        [Serializable]
        internal class CloudSave
        {
            [DataMember(IsRequired = true)] public AccessClass accessClass;
            [DataMember(IsRequired = false, Name = "default")] public JsonObject _default;

            internal enum AccessClass
            {
                Default,
                Public,
                Protected,
                Private
            }
        }

        [DataMember(IsRequired = false)] public Leaderboard leaderboard;
        [DataMember(IsRequired = false)] public CloudSave cloudSave;
    }
}
