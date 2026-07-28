using UnityEditor.SettingsManagement;

namespace Unity.Services.Vivox.Editor
{
    class VivoxSettings
    {
        const string k_PackageName = "com.unity.services.vivox";
        const string k_Server = "server";
        const string k_Domain = "domain";
        const string k_TokenIssuer = "tokenIssuer";
        const string k_TokenKey = "tokenKey";
        const string k_IsServiceEnabledKey = "isServiceEnabled";
        const string k_IsTestModeKey = "isTestMode";
        const string k_IsEnvironmentCustom = "isEnvironmentCustom";

        static VivoxSettings s_Instance;

        bool m_SettingsAreDirty;

        public static VivoxSettings Instance
        {
            get
            {
                if (s_Instance is null)
                {
                    s_Instance = new VivoxSettings();
                }

                return s_Instance;
            }
        }

        readonly Settings m_Settings;

        VivoxSettings()
        {
            m_Settings = new Settings(k_PackageName);
        }

        /// <summary>
        /// Server that the Vivox service will be connecting to.
        /// Typically set automatically from the Project Settings > Services > Vivox tab
        /// </summary>
        public string Server
        {
            get => m_Settings.Get<string>(k_Server);
            set
            {
                if (Server != value)
                {
                    m_Settings.Set(k_Server, value);
                    m_SettingsAreDirty = true;
                };
            }
        }

        /// <summary>
        /// Domain that the Vivox service will be connecting to.
        /// Typically set automatically from the Project Settings > Services > Vivox tab
        /// </summary>
        public string Domain
        {
            get => m_Settings.Get<string>(k_Domain);
            set
            {
                if (Domain != value)
                {
                    m_Settings.Set(k_Domain, value);
                    m_SettingsAreDirty = true;
                };
            }
        }

        /// <summary>
        /// Token Issuer that Vivox will be using to connect to the service.
        /// Typically set automatically from the Project Settings > Services > Vivox tab
        /// </summary>
        public string TokenIssuer
        {
            get => m_Settings.Get<string>(k_TokenIssuer);
            set
            {
                if (TokenIssuer != value)
                {
                    m_Settings.Set(k_TokenIssuer, value);
                    m_SettingsAreDirty = true;
                };
            }
        }

        /// <summary>
        /// Token Key that Vivox will be using to connect to the service.
        /// Typically set automatically from the Project Settings > Services > Vivox tab
        /// </summary>
        public string TokenKey
        {
            get => m_Settings.Get<string>(k_TokenKey);
            set
            {
                if (TokenKey != value)
                {
                    m_Settings.Set(k_TokenKey, value);
                    m_SettingsAreDirty = true;
                };
            }
        }

        /// <summary>
        /// Whether or not the credentials were pulled directly from the Unity Dashboard
        /// </summary>
        public bool IsEnvironmentCustom
        {
            get => m_Settings.Get<bool>(k_IsEnvironmentCustom);
            set => m_Settings.Set(k_IsEnvironmentCustom, value);
        }

        /// <summary>
        /// Whether or not the Vivox Service is enabled
        /// </summary>
        public bool IsServiceEnabled
        {
            get => m_Settings.Get<bool>(k_IsServiceEnabledKey);
            set => m_Settings.Set(k_IsServiceEnabledKey, value);
        }

        /// <summary>
        /// Sets "Test Mode" for Token Generation, which results in easier to setup but less secure token generation
        /// Projects built with Test Mode should not be release for general consumption
        /// </summary>
        public bool IsTestMode
        {
            get => m_Settings.Get(k_IsTestModeKey, fallback: false);
            set => m_Settings.Set(k_IsTestModeKey, value);
        }

        /// <summary>
        /// Saves the VivoxSettings
        /// </summary>
        public void Save()
        {
            if (m_SettingsAreDirty)
            {
                m_Settings.Save();
                m_SettingsAreDirty = false;
            }
        }
    }
}
