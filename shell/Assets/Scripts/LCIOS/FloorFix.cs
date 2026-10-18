using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Floors inside the facility drew as nothing on the phone (2026-10-18). Two kinds of HDRP/Lit floor material
    // depend on a step the other floors don't:
    //  - grates with holes (alpha test: FloorGrating, metalholes, ...) only draw where the depth prepass wrote exactly
    //    the same depth (ZTest Equal). Drawing them with LEqual instead makes them show even if that step fails after
    //    the shader conversion; the holes then look solid.
    //  - metal plate floors with parallax (pixel displacement: MainRoomFloor, OilRigFloor) use the costliest shader
    //    variant there is. Without it they are flat-shaded like every other floor, and cheaper on the GPU.
    // Also logs which materials are under the player inside, so a floor still missing can be named from the log.
    public class FloorFix : MonoBehaviour
    {
        static readonly int ZTestGBuffer = Shader.PropertyToID("_ZTestGBuffer");
        const int Equal = 3, LessEqual = 4;
        readonly HashSet<int> m_Done = new HashSet<int>();
        string m_LastFloor;

        public static void Install()
        {
            var go = new GameObject("LCIOS.FloorFix");
            DontDestroyOnLoad(go);
            var f = go.AddComponent<FloorFix>();
            SceneManager.sceneLoaded += (s, mode) => f.Fix(s.name);
        }

        void Start()
        {
            // The interior is generated after landing from materials already loaded; catch any that come later.
            InvokeRepeating(nameof(FixLater), 10f, 10f);
            InvokeRepeating(nameof(LogFloor), 5f, 3f);
        }

        void FixLater() { Fix(null); }

        void Fix(string scene)
        {
            try
            {
                var grates = new List<string>();
                var flat = new List<string>();
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || !m_Done.Add(m.GetInstanceID())) continue;
                    if (m.shader == null || !m.shader.name.StartsWith("HDRP/")) continue;
                    if (m.IsKeywordEnabled("_ALPHATEST_ON") && m.HasProperty(ZTestGBuffer) && (int)m.GetFloat(ZTestGBuffer) == Equal)
                    {
                        m.SetFloat(ZTestGBuffer, LessEqual);
                        grates.Add(m.name);
                    }
                    if (m.IsKeywordEnabled("_PIXEL_DISPLACEMENT"))
                    {
                        m.DisableKeyword("_PIXEL_DISPLACEMENT");
                        m.DisableKeyword("_PIXEL_DISPLACEMENT_LOCK_OBJECT_SCALE");
                        m.DisableKeyword("_DEPTHOFFSET_ON");
                        flat.Add(m.name);
                    }
                }
                if (grates.Count > 0 || flat.Count > 0 || scene != null)
                    Log.Info($"[floorfix] {(scene != null ? $"'{scene}'" : "later")}: alpha-tested drawn with LEqual ({grates.Count}): {string.Join(", ", grates)}; parallax off ({flat.Count}): {string.Join(", ", flat)}");
            }
            catch (Exception e)
            {
                Log.Error("[floorfix] " + e);
            }
        }

        // What the local player stands on, when it changes (only while inside the facility).
        void LogFloor()
        {
            try
            {
                var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
                if (p == null || !p.isInsideFactory) { m_LastFloor = null; return; }
                if (!Physics.Raycast(p.transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return;
                var parts = new List<string>();
                foreach (var r in hit.collider.GetComponentsInParent<Renderer>(true))
                {
                    var mats = new List<string>();
                    foreach (var m in r.sharedMaterials)
                        if (m != null) mats.Add($"{m.name} [{m.shader.name} q{m.renderQueue} {string.Join(" ", m.shaderKeywords)}]");
                    parts.Add($"{r.name}{(r.enabled && r.gameObject.activeInHierarchy ? "" : " (hidden)")}: {string.Join("; ", mats)}");
                    if (parts.Count >= 2) break;
                }
                if (parts.Count == 0)
                {
                    foreach (var r in hit.collider.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = new List<string>();
                        foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m.name);
                        parts.Add($"{r.name}{(r.enabled && r.gameObject.activeInHierarchy ? "" : " (hidden)")}: {string.Join("; ", mats)}");
                        if (parts.Count >= 2) break;
                    }
                }
                string floor = $"collider '{hit.collider.name}' -> {(parts.Count > 0 ? string.Join(" | ", parts) : "no renderer")}";
                if (floor == m_LastFloor) return;
                m_LastFloor = floor;
                Log.Info("[floor] " + floor);
            }
            catch (Exception e)
            {
                Log.Warn("[floor] " + e.Message);
                CancelInvoke(nameof(LogFloor));
            }
        }
    }
}
