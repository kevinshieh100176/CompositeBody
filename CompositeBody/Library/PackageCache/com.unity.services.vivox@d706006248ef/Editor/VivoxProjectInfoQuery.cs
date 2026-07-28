using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Vivox.Editor
{
    class VivoxProjectInfoQueryResult
    {
        public string server { get; set; }
        public string domain { get; set; }
        public string tokenIssuer { get; set; }
        public string tokenKey { get; set; }
    }

    static class VivoxProjectInfoQuery
    {
    }
}
