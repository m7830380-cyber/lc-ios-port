using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Diagnostics: logs each scene's UI input module and the first touches with the UI object under them,
    // so a phone log shows whether taps reach the game's menus.
    public class TouchProbe : MonoBehaviour
    {
        const int MaxTouchesPerScene = 20;
        int m_Logged;
        bool m_Failed;
        readonly List<RaycastResult> m_Hits = new List<RaycastResult>();

        public static void Install()
        {
            var go = new GameObject("LCIOS.TouchProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<TouchProbe>();
            SceneManager.sceneLoaded += (s, mode) => Describe(s.name);
        }

        static void Describe(string scene)
        {
            var es = EventSystem.current;
            string module = es == null ? "none" : (es.currentInputModule != null ? es.currentInputModule.GetType().FullName : "not yet active");
            Log.Info($"[touch] scene '{scene}': eventSystem={(es == null ? "none" : es.name)} module={module} cameras={Camera.allCamerasCount}");
            Instance?.ResetCount();
        }

        static TouchProbe Instance;
        void Awake() { Instance = this; }
        void ResetCount() { m_Logged = 0; }

        void Update()
        {
            if (m_Failed || m_Logged >= MaxTouchesPerScene) return;
            try
            {
                if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
                foreach (var t in Touch.activeTouches)
                {
                    if (t.phase != TouchPhase.Began) continue;
                    Log.Info($"[touch] began at {t.screenPosition} -> {UiUnder(t.screenPosition)}");
                    if (++m_Logged >= MaxTouchesPerScene) Log.Info("[touch] limit reached for this scene");
                }
            }
            catch (Exception e)
            {
                m_Failed = true;
                Log.Warn("[touch] probe disabled: " + e.Message);
            }
        }

        string UiUnder(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return "no EventSystem";
            m_Hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, m_Hits);
            if (m_Hits.Count == 0) return "no UI hit";
            var top = m_Hits[0].gameObject;
            return $"'{top.name}' (parent '{(top.transform.parent ? top.transform.parent.name : "-")}', {m_Hits.Count} hits)";
        }
    }
}
