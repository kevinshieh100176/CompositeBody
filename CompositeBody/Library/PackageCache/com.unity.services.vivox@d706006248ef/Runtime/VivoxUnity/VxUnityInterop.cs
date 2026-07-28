#if UNITY_5_3_OR_NEWER
using UnityEngine;
using System;
using System.Collections;

namespace Unity.Services.Vivox
{
    internal class VxUnityInterop : MonoBehaviour
    {
        private static object m_Lock = new object();
#if !UNITY_INCLUDE_TESTS
        private bool quitting = false;
#endif
        private static VxUnityInterop m_Instance;

        /// <summary>
        /// Access singleton instance through this propriety.
        /// </summary>
        public static VxUnityInterop Instance
        {
            get
            {
                lock (m_Lock)
                {
                    if (m_Instance == null)
                    {
                        // Search for existing instance.
                        m_Instance = FindObjectOfType<VxUnityInterop>();

                        // Create new instance if one doesn't already exist.
                        if (m_Instance == null)
                        {
                            // Need to create a new GameObject to attach the singleton to.
                            var singletonObject = new GameObject();
                            m_Instance = singletonObject.AddComponent<VxUnityInterop>();
                            singletonObject.name = typeof(VxUnityInterop).ToString() + " (Singleton)";
                        }
                    }
                    // Make instance persistent even if its already in the scene
                    DontDestroyOnLoad(m_Instance.gameObject);
                    return m_Instance;
                }
            }
        }

        void ApplicationQuitting()
        {
#if !UNITY_INCLUDE_TESTS
            quitting = true;
#endif
            VxClient.Instance.IsQuitting = true;
            Client.Instance.Uninitialize();
            Application.quitting -= ApplicationQuitting;
        }

        // Setting up Unity Coroutine to run on the main thread
        public virtual void StartVivoxUnity()
        {
            StartCoroutine(VivoxUnityRun());
            Application.quitting += ApplicationQuitting;
        }

        private IEnumerator VivoxUnityRun()
        {
            while (VxClient.Instance.Started)
            {
                try
                {
                    Client.RunOnce();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
        }

        void OnDestroy()
        {
#if !UNITY_INCLUDE_TESTS
            if (!quitting)
            {
                var classType = GetType();
                Debug.LogWarning(classType.Namespace + " requrires " + classType.Name + " to communicate messages to and from "
                    + classType.Namespace + " Core. Deleting this object will prevent the " + classType.Namespace + " SDK from working.  " +
                    "If you would like to change it's implementation please override StartVivoxUnity method in "
                    + classType.Name);
            }
#endif
        }
    }
}
#endif
