using System;
using System.Text;
using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// A persistent per-headset identity token, independent of NGO's <c>OwnerClientId</c>
    /// (which changes every reconnect). Stored locally so a dropped headset can rejoin an
    /// in-progress session and reclaim its role and story progress.
    /// </summary>
    public static class PlayerIdentity
    {
        const string k_PrefsKey = "compositebody_player_guid";

        public static string localGuid
        {
            get
            {
                string guid = PlayerPrefs.GetString(k_PrefsKey, string.Empty);
                if (string.IsNullOrEmpty(guid))
                {
                    guid = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(k_PrefsKey, guid);
                    PlayerPrefs.Save();
                }
                return guid;
            }
        }

        /// <summary>Builds the byte payload sent as part of NGO's connection approval request.</summary>
        public static byte[] BuildConnectionPayload() => Encoding.UTF8.GetBytes(localGuid);

        public static string ReadFromConnectionPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>Wipes the stored identity. Useful for staff resetting a headset to "new player".</summary>
        public static void ResetLocalIdentity()
        {
            PlayerPrefs.DeleteKey(k_PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
