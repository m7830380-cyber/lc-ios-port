using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace LCIOS
{
    // The ship terminal on a phone. The game types into a text field that holds the whole terminal screen, so the
    // iOS keyboard showed all of it, and leaving needs TAB/ESC, which a phone doesn't have (2026-10-19).
    // This hides that field's own keyboard and opens one holding only what the player types, copied into the terminal
    // as they type; Return sends it. Buttons on the right: type, scroll, exit.
    public class TerminalTouch : MonoBehaviour
    {
        Terminal m_Terminal;
        float m_NextFind;
        bool m_Active;
        TouchScreenKeyboard m_Keyboard;
        string m_Typed = "";
        readonly List<(Rect Rect, Action Tap)> m_Taps = new List<(Rect, Action)>();
        GUIStyle m_Button;

        public static void Install()
        {
            var go = new GameObject("LCIOS.TerminalTouch");
            DontDestroyOnLoad(go);
            go.AddComponent<TerminalTouch>();
        }

        void Update()
        {
            try
            {
                if (m_Terminal == null && Time.unscaledTime > m_NextFind)
                {
                    m_NextFind = Time.unscaledTime + 2f;
                    m_Terminal = FindObjectOfType<Terminal>();
                    // Keep the game's field from opening its own keyboard with the whole screen text in it.
                    if (m_Terminal != null && m_Terminal.screenText != null) m_Terminal.screenText.shouldHideSoftKeyboard = true;
                }
                var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
                bool active = m_Terminal != null && m_Terminal.terminalInUse && p != null && p.inTerminalMenu;
                if (active != m_Active)
                {
                    m_Active = active;
                    Log.Info("[terminal] " + (active ? "entered" : "left"));
                    if (active) OpenKeyboard();
                    else CloseKeyboard();
                }
                if (!m_Active) return;

                PollKeyboard();
                foreach (var t in Touch.activeTouches)
                {
                    if (t.phase != TouchPhase.Began) continue;
                    var pos = new Vector2(t.screenPosition.x, Screen.height - t.screenPosition.y);
                    foreach (var (rect, tap) in m_Taps)
                        if (rect.Contains(pos)) { tap(); break; }
                }
            }
            catch (Exception e)
            {
                Log.Error("[terminal] " + e);
                m_Terminal = null;
                CloseKeyboard();
            }
        }

        void OpenKeyboard()
        {
            CloseKeyboard();
            m_Typed = "";
            TouchScreenKeyboard.hideInput = false;
            m_Keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.ASCIICapable, false, false, false, false, "Type a command");
        }

        void CloseKeyboard()
        {
            if (m_Keyboard != null) m_Keyboard.active = false;
            m_Keyboard = null;
        }

        void PollKeyboard()
        {
            if (m_Keyboard == null) return;
            var st = m_Keyboard.status;
            Write(m_Keyboard.text ?? "");
            if (st == TouchScreenKeyboard.Status.Visible) return;
            m_Keyboard = null;
            if (st == TouchScreenKeyboard.Status.Done)
            {
                Log.Info($"[terminal] command '{m_Typed}'");
                m_Typed = "";
                m_Terminal.OnSubmit();
            }
        }

        // Puts the typed text at the end of the terminal screen in place of what was typed before.
        void Write(string typed)
        {
            var node = m_Terminal.currentNode;
            if (node != null && typed.Length > node.maxCharactersToType) typed = typed.Substring(0, node.maxCharactersToType);
            if (typed == m_Typed) return;
            var field = m_Terminal.screenText;
            string text = field.text;
            int added = Mathf.Clamp(m_Terminal.textAdded, 0, text.Length);
            // The game's TextChanged (on the field's change event) counts textAdded from this.
            field.text = text.Substring(0, text.Length - added) + typed;
            m_Typed = typed;
        }

        void Scroll(float by)
        {
            var bar = m_Terminal.scrollBarVertical;
            if (bar != null) bar.value = Mathf.Clamp01(bar.value + by);
        }

        void OnGUI()
        {
            if (!m_Active) return;
            float u = Screen.height / 20f;
            if (m_Button == null) m_Button = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            m_Button.fontSize = Mathf.RoundToInt(u * 0.6f);
            bool collect = Event.current.type == EventType.Repaint;
            if (collect) m_Taps.Clear();

            var safe = Screen.safeArea;
            float w = u * 3.2f, h = u * 1.6f, gap = u * 0.4f;
            float x = safe.xMax - w - u * 0.5f, y = Screen.height - safe.yMax + u * 0.5f;
            Button(new Rect(x, y, w, h), "EXIT", collect, () => m_Terminal.QuitTerminal());
            y += h + gap;
            Button(new Rect(x, y, w, h), "TYPE", collect, OpenKeyboard);
            y += h + gap;
            Button(new Rect(x, y, w, h), "UP", collect, () => Scroll(0.25f));
            y += h + gap;
            Button(new Rect(x, y, w, h), "DOWN", collect, () => Scroll(-0.25f));
        }

        void Button(Rect r, string label, bool collect, Action tap)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, label, m_Button);
            if (collect) m_Taps.Add((r, tap));
        }
    }
}
