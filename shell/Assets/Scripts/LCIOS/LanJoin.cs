using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using NetworkManager = Unity.Netcode.NetworkManager;
using TransportEvent = Unity.Networking.Transport.NetworkEvent;
using UnityEngine;
using UnityEngine.SceneManagement;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace LCIOS
{
    // The game's LAN "Join" always connects to 127.0.0.1 (the same computer), so on the phone it waits forever.
    // In LAN mode on the main menu this shows a small panel: it finds games on the Wi-Fi by trying a Unity Transport
    // connection to every address of the phone's /24 network on the game port (a PC or another phone hosting with
    // "allow remote connections" answers), lets the player pick one or type an address, and points the game's
    // transport at it, so the game's own Join button connects there.
    public class LanJoin : MonoBehaviour
    {
        const string PrefKey = "lcios.lan.ip";
        const float ScanTime = 3f, Rescan = 6f;

        string m_Selected;
        readonly List<string> m_Found = new List<string>();
        string m_Status = "";
        bool m_Visible;

        NetworkDriver m_Driver;
        readonly List<(NetworkConnection Conn, string Ip)> m_Probes = new List<(NetworkConnection, string)>();
        float m_ScanStarted = -100f, m_ScanEnded = -100f;
        TouchScreenKeyboard m_Keyboard;

        Rect m_Panel;
        readonly List<(Rect Rect, Action Tap)> m_Taps = new List<(Rect, Action)>();
        GUIStyle m_Text, m_Button;

        public static void Install()
        {
            var go = new GameObject("LCIOS.LanJoin");
            DontDestroyOnLoad(go);
            go.AddComponent<LanJoin>();
        }

        void Start()
        {
            m_Selected = PlayerPrefs.GetString(PrefKey, "");
        }

        static bool LanMenu(out UnityTransport transport)
        {
            transport = null;
            if (SceneManager.GetActiveScene().name != "MainMenu") return false;
            var gnm = GameNetworkManager.Instance;
            var nm = NetworkManager.Singleton;
            if (gnm == null || !gnm.disableSteam || nm == null || nm.IsListening) return false;
            transport = nm.GetComponent<UnityTransport>();
            return transport != null;
        }

        void Update()
        {
            try
            {
                bool visible = LanMenu(out var transport);
                if (visible != m_Visible)
                {
                    m_Visible = visible;
                    if (!visible) StopScan();
                    else Log.Info($"[lan] join panel shown, saved host '{m_Selected}'");
                }
                if (!visible) return;

                // The game's Join button connects to ConnectionData.Address.
                if (!string.IsNullOrEmpty(m_Selected) && transport.ConnectionData.Address != m_Selected)
                {
                    var cd = transport.ConnectionData;
                    cd.Address = m_Selected;
                    transport.ConnectionData = cd;
                    Log.Info($"[lan] join address set to {m_Selected}:{cd.Port}");
                }

                PollKeyboard();
                if (m_Driver.IsCreated) PollScan();
                else if (m_Found.Count == 0 && Time.unscaledTime - m_ScanEnded > Rescan) StartScan(transport.ConnectionData.Port);

                if (m_Keyboard == null)
                    foreach (var t in Touch.activeTouches)
                    {
                        if (t.phase != TouchPhase.Began) continue;
                        var p = new Vector2(t.screenPosition.x, Screen.height - t.screenPosition.y);
                        foreach (var (rect, tap) in m_Taps)
                            if (rect.Contains(p)) { tap(); break; }
                    }
            }
            catch (Exception e)
            {
                m_Status = "error: " + e.Message;
                Log.Error("[lan] " + e);
                StopScan();
                m_ScanEnded = Time.unscaledTime + 60f;   // don't retry a failing scan every few seconds
            }
        }

        void Select(string ip)
        {
            m_Selected = ip;
            PlayerPrefs.SetString(PrefKey, ip);
            PlayerPrefs.Save();
            Log.Info($"[lan] host selected: {ip}");
        }

        // ---- finding hosts ----

        void StartScan(ushort port)
        {
            var self = LocalAddress();
            if (self == null)
            {
                m_Status = "Not on Wi-Fi?";
                m_ScanEnded = Time.unscaledTime;
                Log.Warn("[lan] no local IPv4 address");
                return;
            }
            var b = self.GetAddressBytes();
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithNetworkConfigParameters(connectTimeoutMS: 400, maxConnectAttempts: 6, disconnectTimeoutMS: 2000,
                heartbeatTimeoutMS: 500, maxFrameTimeMS: 0, fixedFrameTimeMS: 0, maxMessageSize: 1400);
            m_Driver = NetworkDriver.Create(settings);
            for (int i = 1; i < 255; i++)
            {
                if (i == b[3]) continue;
                string ip = $"{b[0]}.{b[1]}.{b[2]}.{i}";
                m_Probes.Add((m_Driver.Connect(NetworkEndPoint.Parse(ip, port)), ip));
            }
            m_ScanStarted = Time.unscaledTime;
            m_Status = "Searching...";
            Log.Info($"[lan] scanning {b[0]}.{b[1]}.{b[2]}.1-254 port {port} (phone is {self})");
        }

        void PollScan()
        {
            m_Driver.ScheduleUpdate().Complete();
            foreach (var (conn, ip) in m_Probes)
            {
                TransportEvent.Type ev;
                while ((ev = m_Driver.PopEventForConnection(conn, out _)) != TransportEvent.Type.Empty)
                {
                    if (ev != TransportEvent.Type.Connect) continue;
                    if (!m_Found.Contains(ip))
                    {
                        m_Found.Add(ip);
                        Log.Info($"[lan] found a game at {ip}");
                        if (m_Found.Count == 1 && m_Selected != ip && (string.IsNullOrEmpty(m_Selected) || !m_Found.Contains(m_Selected))) Select(ip);
                    }
                    m_Driver.Disconnect(conn);
                }
            }
            if (Time.unscaledTime - m_ScanStarted < ScanTime) return;
            m_Status = m_Found.Count == 0 ? "No game found. Host must pick \"allow remote connections\"." : "";
            Log.Info($"[lan] scan done: {(m_Found.Count == 0 ? "nothing" : string.Join(", ", m_Found))}");
            StopScan();
        }

        void StopScan()
        {
            if (m_Driver.IsCreated)
            {
                foreach (var (conn, _) in m_Probes)
                    if (m_Driver.GetConnectionState(conn) != NetworkConnection.State.Disconnected) m_Driver.Disconnect(conn);
                m_Driver.ScheduleUpdate().Complete();
                m_Driver.Dispose();
            }
            m_Driver = default;
            m_Probes.Clear();
            m_ScanEnded = Time.unscaledTime;
        }

        static IPAddress LocalAddress()
        {
            IPAddress best = null;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address)) continue;
                        var b = a.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        if (ni.Name == "en0") return a.Address;   // Wi-Fi on iOS
                        if (best == null) best = a.Address;
                    }
                }
            }
            catch (Exception e) { Log.Warn("[lan] interfaces: " + e.Message); }
            if (best != null) return best;
            try
            {
                // Picks the interface the default route uses; nothing is sent.
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("10.255.255.255", 1);
                    return ((IPEndPoint)s.LocalEndPoint).Address;
                }
            }
            catch (Exception e) { Log.Warn("[lan] route: " + e.Message); }
            return null;
        }

        // ---- typing an address ----

        void PollKeyboard()
        {
            if (m_Keyboard == null) return;
            var st = m_Keyboard.status;
            if (st == TouchScreenKeyboard.Status.Visible) return;
            if (st == TouchScreenKeyboard.Status.Done)
            {
                string text = m_Keyboard.text.Trim().Replace(',', '.');
                if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (!m_Found.Contains(text)) m_Found.Add(text);
                    Select(text);
                    m_Status = "";
                }
                else m_Status = $"\"{text}\" is not an address like 192.168.1.20";
            }
            m_Keyboard = null;
        }

        // ---- drawing ----

        void OnGUI()
        {
            if (!m_Visible) return;
            float u = Screen.height / 20f;
            if (m_Text == null)
            {
                m_Text = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
                m_Button = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            m_Text.fontSize = Mathf.RoundToInt(u * 0.5f);
            m_Button.fontSize = Mathf.RoundToInt(u * 0.5f);

            var safe = Screen.safeArea;
            float w = u * 9f, row = u * 1.1f;
            int rows = 2 + Mathf.Max(1, m_Found.Count) + 1 + (m_Status.Length > 0 ? 1 : 0);
            m_Panel = new Rect(safe.xMax - w - u * 0.5f, Screen.height - safe.yMax + u * 0.5f, w, rows * row + u * 0.4f);
            bool collect = Event.current.type == EventType.Repaint;
            if (collect) m_Taps.Clear();

            Fill(m_Panel, new Color(0f, 0f, 0f, 0.7f));
            float x = m_Panel.x + u * 0.3f, y = m_Panel.y + u * 0.2f, iw = w - u * 0.6f;
            GUI.Label(new Rect(x, y, iw, row), "LAN game to join:", m_Text); y += row;
            if (m_Found.Count == 0)
            {
                GUI.Label(new Rect(x, y, iw, row), string.IsNullOrEmpty(m_Selected) ? "(none yet)" : m_Selected + "  (saved)", m_Text);
                y += row;
            }
            foreach (var ip in m_Found)
            {
                string addr = ip;
                bool sel = addr == m_Selected;
                var r = new Rect(x, y, iw, row * 0.9f);
                Fill(r, sel ? new Color(0.9f, 0.35f, 0.1f, 0.85f) : new Color(1f, 1f, 1f, 0.15f));
                GUI.Label(r, (sel ? "> " : "") + addr, m_Button);
                if (collect) m_Taps.Add((r, () => Select(addr)));
                y += row;
            }
            float bw = (iw - u * 0.3f) / 2;
            var scan = new Rect(x, y, bw, row * 0.9f);
            var type = new Rect(x + bw + u * 0.3f, y, bw, row * 0.9f);
            Fill(scan, new Color(1f, 1f, 1f, m_Driver.IsCreated ? 0.08f : 0.2f));
            Fill(type, new Color(1f, 1f, 1f, 0.2f));
            GUI.Label(scan, m_Driver.IsCreated ? "Searching..." : "Search again", m_Button);
            GUI.Label(type, "Type address", m_Button);
            if (collect)
            {
                m_Taps.Add((scan, () =>
                {
                    if (m_Driver.IsCreated || !LanMenu(out var t)) return;
                    m_Found.Clear();
                    StartScan(t.ConnectionData.Port);
                }));
                m_Taps.Add((type, () =>
                {
                    m_Keyboard = TouchScreenKeyboard.Open(m_Selected ?? "", TouchScreenKeyboardType.NumbersAndPunctuation, false, false, false, false, "Host address, e.g. 192.168.1.20");
                }));
            }
            y += row;
            if (m_Status.Length > 0) GUI.Label(new Rect(x, y, iw, row), m_Status, m_Text);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
