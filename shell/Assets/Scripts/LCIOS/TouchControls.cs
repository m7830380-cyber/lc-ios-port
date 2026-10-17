using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LCIOS
{
    // On-screen controls for the ship and the moons. The game already has gamepad bindings for every action
    // (PlayerActions: Move = left stick, Jump = south, Interact = west, ...), so the controls feed a virtual gamepad;
    // looking feeds a virtual mouse's delta, which the game reads like a real mouse.
    // Left part of the screen: floating move stick. Right part: drag to look, buttons on the right edge.
    public class TouchControls : MonoBehaviour
    {
        class Button
        {
            public string Label;
            public GamepadButton Pad;
            public bool Toggle;
            public float X, Y;   // centre, in units from the right/bottom edge of the safe area (negative X = from the left)
            public float R = 0.55f;
            public bool Down;
            public Rect Screen;  // computed each frame, GUI coordinates
        }

        const float LookScale = 0.6f;   // mouse-delta pixels per touch pixel

        readonly Button[] m_Buttons =
        {
            new Button { Label = "JUMP", Pad = GamepadButton.South, X = 1.2f, Y = 1.3f, R = 0.7f },
            new Button { Label = "CROUCH", Pad = GamepadButton.RightStick, X = 2.7f, Y = 0.8f },
            new Button { Label = "E", Pad = GamepadButton.West, X = 2.7f, Y = 2.2f, R = 0.65f },
            new Button { Label = "USE", Pad = GamepadButton.RightTrigger, X = 1.2f, Y = 3.0f, R = 0.65f },
            new Button { Label = "RUN", Pad = GamepadButton.LeftTrigger, X = 4.0f, Y = 1.0f, Toggle = true },
            new Button { Label = "DROP", Pad = GamepadButton.East, X = 4.0f, Y = 2.4f },
            new Button { Label = "SCAN", Pad = GamepadButton.RightShoulder, X = 2.7f, Y = 3.6f },
            new Button { Label = "INSPECT", Pad = GamepadButton.LeftShoulder, X = 4.0f, Y = 3.8f, R = 0.45f },
            new Button { Label = "<", Pad = GamepadButton.DpadLeft, X = 1.9f, Y = 4.6f, R = 0.45f },
            new Button { Label = ">", Pad = GamepadButton.DpadRight, X = 0.8f, Y = 4.6f, R = 0.45f },
            new Button { Label = "MENU", Pad = GamepadButton.Start, X = -0.8f, Y = 6.4f, R = 0.42f },
        };

        enum Role { Stick, Look, Button }
        struct Finger { public Role Role; public int Button; public Vector2 Start, Last; }
        readonly Dictionary<int, Finger> m_Fingers = new Dictionary<int, Finger>();

        Gamepad m_Pad;
        Mouse m_Mouse;
        Texture2D m_Circle;
        GUIStyle m_Label;
        bool m_Visible, m_Failed;
        Vector2 m_StickCentre, m_Stick;
        float m_Unit;
        Rect m_Safe;

        public static void Install()
        {
            var go = new GameObject("LCIOS.TouchControls");
            DontDestroyOnLoad(go);
            go.AddComponent<TouchControls>();
        }

        void Start()
        {
            m_Pad = InputSystem.AddDevice<Gamepad>("LCIOS Touch Gamepad");
            m_Mouse = InputSystem.AddDevice<Mouse>("LCIOS Touch Look");
            m_Circle = MakeCircle(128);
            Log.Info("[controls] virtual gamepad and mouse added");
        }

        static bool InGame()
        {
            var gnm = GameNetworkManager.Instance;
            var p = gnm != null ? gnm.localPlayerController : null;
            if (p == null || !p.isPlayerControlled && !p.isPlayerDead) return false;
            if (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen) return false;
            return !p.inTerminalMenu && !p.isTypingChat;
        }

        void Update()
        {
            if (m_Failed || m_Pad == null) return;
            try
            {
                bool visible = InGame();
                if (visible != m_Visible)
                {
                    m_Visible = visible;
                    Log.Info("[controls] " + (visible ? "shown" : "hidden"));
                    if (!visible) ReleaseAll();
                }
                if (!m_Visible) return;
                Layout();
                Vector2 look = Vector2.zero;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    switch (t.phase)
                    {
                        case TouchPhase.Began: Begin(t); break;
                        case TouchPhase.Moved:
                        case TouchPhase.Stationary: look += Move(t); break;
                        default: End(t.fingerId); break;
                    }
                }
                Send(look);
            }
            catch (System.Exception e)
            {
                m_Failed = true;
                Log.Error("[controls] disabled: " + e);
            }
        }

        void Layout()
        {
            m_Unit = UnityEngine.Screen.height / 10f;
            m_Safe = UnityEngine.Screen.safeArea;
            foreach (var b in m_Buttons)
            {
                float cx = b.X >= 0 ? m_Safe.xMax - b.X * m_Unit : m_Safe.xMin - b.X * m_Unit;
                float cy = m_Safe.yMin + b.Y * m_Unit;   // touch space: origin bottom-left
                float r = b.R * m_Unit;
                b.Screen = new Rect(cx - r, UnityEngine.Screen.height - cy - r, 2 * r, 2 * r);
            }
        }

        int ButtonAt(Vector2 touch)
        {
            var gui = new Vector2(touch.x, UnityEngine.Screen.height - touch.y);
            for (int i = 0; i < m_Buttons.Length; i++)
            {
                var b = m_Buttons[i];
                // A little larger than drawn, so fingers do not slip off.
                if (Vector2.Distance(gui, b.Screen.center) <= b.Screen.width * 0.6f) return i;
            }
            return -1;
        }

        void Begin(Touch t)
        {
            int bi = ButtonAt(t.position);
            var f = new Finger { Start = t.position, Last = t.position, Button = bi };
            if (bi >= 0)
            {
                f.Role = Role.Button;
                var b = m_Buttons[bi];
                b.Down = b.Toggle ? !b.Down : true;
            }
            else if (t.position.x < UnityEngine.Screen.width * 0.42f)
            {
                f.Role = Role.Stick;
                m_StickCentre = t.position;
                m_Stick = Vector2.zero;
            }
            else f.Role = Role.Look;
            m_Fingers[t.fingerId] = f;
        }

        Vector2 Move(Touch t)
        {
            if (!m_Fingers.TryGetValue(t.fingerId, out var f)) { Begin(t); return Vector2.zero; }
            Vector2 look = Vector2.zero;
            if (f.Role == Role.Stick)
                m_Stick = Vector2.ClampMagnitude((t.position - m_StickCentre) / (1.2f * m_Unit), 1f);
            else if (f.Role == Role.Look)
                look = (t.position - f.Last) * LookScale;
            f.Last = t.position;
            m_Fingers[t.fingerId] = f;
            return look;
        }

        void End(int finger)
        {
            if (!m_Fingers.TryGetValue(finger, out var f)) return;
            m_Fingers.Remove(finger);
            if (f.Role == Role.Stick) m_Stick = Vector2.zero;
            else if (f.Role == Role.Button && !m_Buttons[f.Button].Toggle) m_Buttons[f.Button].Down = false;
        }

        void ReleaseAll()
        {
            m_Fingers.Clear();
            m_Stick = Vector2.zero;
            foreach (var b in m_Buttons) b.Down = false;
            Send(Vector2.zero);
        }

        void Send(Vector2 look)
        {
            var state = new GamepadState { leftStick = m_Stick };
            foreach (var b in m_Buttons)   // triggers are axes, set below; WithButton only takes the 32 button bits
                if (b.Down && b.Pad != GamepadButton.LeftTrigger && b.Pad != GamepadButton.RightTrigger) state = state.WithButton(b.Pad);
            state.leftTrigger = m_Buttons[4].Down ? 1f : 0f;
            state.rightTrigger = m_Buttons[3].Down ? 1f : 0f;
            InputSystem.QueueStateEvent(m_Pad, state);
            if (look != Vector2.zero)
                InputSystem.QueueStateEvent(m_Mouse, new MouseState { delta = look });
        }

        void OnGUI()
        {
            if (!m_Visible || m_Circle == null || Event.current.type != EventType.Repaint) return;
            if (m_Label == null)
                m_Label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            m_Label.fontSize = Mathf.RoundToInt(m_Unit * 0.24f);
            var old = GUI.color;
            foreach (var b in m_Buttons)
            {
                GUI.color = new Color(1f, 1f, 1f, b.Down ? 0.55f : 0.22f);
                GUI.DrawTexture(b.Screen, m_Circle);
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.Label(b.Screen, b.Label, m_Label);
            }
            bool stickActive = false;
            foreach (var f in m_Fingers.Values) if (f.Role == Role.Stick) stickActive = true;
            Vector2 centre = stickActive ? m_StickCentre : new Vector2(m_Safe.xMin + 2.2f * m_Unit, m_Safe.yMin + 2.2f * m_Unit);
            float br = 1.2f * m_Unit, kr = 0.5f * m_Unit;
            var c = new Vector2(centre.x, UnityEngine.Screen.height - centre.y);
            GUI.color = new Color(1f, 1f, 1f, stickActive ? 0.25f : 0.12f);
            GUI.DrawTexture(new Rect(c.x - br, c.y - br, 2 * br, 2 * br), m_Circle);
            var k = c + new Vector2(m_Stick.x, -m_Stick.y) * br;
            GUI.color = new Color(1f, 1f, 1f, stickActive ? 0.5f : 0.2f);
            GUI.DrawTexture(new Rect(k.x - kr, k.y - kr, 2 * kr, 2 * kr), m_Circle);
            GUI.color = old;
        }

        static Texture2D MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    // Filled disc with a brighter rim, soft edge.
                    float a = Mathf.Clamp01(r - d) * (d > r - 6 ? 1f : 0.45f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
