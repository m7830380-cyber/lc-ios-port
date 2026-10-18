using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // The player's view (MainCamera -> PlayerScreen render texture) came out black on iOS while the UI drew fine.
    // Some HDRP pass breaks after the DX11 -> Metal shader conversion. This measures the camera's own output and,
    // while it is black, turns HDRP features off group by group until the picture appears, then turns back on every
    // group that was not needed. The result is logged and applied to every game camera from then on.
    public class RenderFix : MonoBehaviour
    {
        class Group
        {
            public string Name;
            public string[] Off;   // FrameSettingsField names to disable
            public string[] On;    // FrameSettingsField names to enable
            public bool NoDynRes;
        }

        static readonly Group[] Groups =
        {
            new Group { Name = "dynamic resolution", NoDynRes = true },
            new Group { Name = "stop NaN", On = new[] { "StopNaN" } },
            new Group { Name = "volumetric fog", Off = new[] { "Volumetrics", "ReprojectionForVolumetrics", "VolumetricClouds", "AtmosphericScattering" } },
            new Group { Name = "exposure", Off = new[] { "ExposureControl" } },
            new Group { Name = "compute light loop", Off = new[] { "DeferredTile", "ComputeLightEvaluation", "ComputeLightVariants", "ComputeMaterialVariants", "BigTilePrepass", "FPTLForForwardOpaque" } },
            new Group { Name = "bloom", Off = new[] { "Bloom" } },
            new Group { Name = "post processing", Off = new[] { "Postprocess", "CustomPostProcess" } },
            new Group { Name = "custom passes", Off = new[] { "CustomPass" } },
            new Group { Name = "screen space effects", Off = new[] { "SSAO", "SSR", "TransparentSSR", "SSGI", "ContactShadows", "ScreenSpaceShadows" } },
            new Group { Name = "shadows", Off = new[] { "ShadowMaps", "Shadowmask" } },
            new Group { Name = "reflections", Off = new[] { "SkyReflection", "ReflectionProbe", "PlanarProbe", "ProbeVolume" } },
            new Group { Name = "sss/decals/layers", Off = new[] { "SubsurfaceScattering", "Transmission", "Decals", "DecalLayers", "LightLayers" } },
            new Group { Name = "async compute", Off = new[] { "AsyncCompute", "LightListAsync", "SSRAsync", "SSAOAsync", "ContactShadowsAsync", "VolumeVoxelizationsAsync" } },
            new Group { Name = "forward rendering", Off = new[] { "LitShaderMode" } },
            // Not a fix: costly effects that barely show in this game, kept off for frame rate.
            // Only for the full-screen camera that draws the HUD/menus (it renders no 3D world, just the canvas).
            new Group { Name = "screen camera extras", Off = new[] { "Volumetrics", "ReprojectionForVolumetrics", "AtmosphericScattering", "ShadowMaps", "Shadowmask", "SSAO", "Decals", "DecalLayers", "LightLayers", "ReflectionProbe", "PlanarProbe", "SkyReflection", "MotionVectors", "ObjectMotionVectors", "TransparentsWriteMotionVector", "Refraction", "Distortion", "LowResTransparent", "TransparentPrepass", "TransparentPostpass" } },
            new Group { Name = "subtle effects", Off = new[] { "SSR", "TransparentSSR", "SSGI", "ContactShadows", "ScreenSpaceShadows", "SubsurfaceScattering", "Transmission", "MSAA" } },
        };

        const float LitNeeded = 0.25f;   // share of pixels that must be visibly lit for the view to count as working

        // Found on the phone (2026-10-18): the compute light loop renders black. Start with it off, and keep the
        // player view out of dynamic resolution (its render texture is only 860x520; scaling it showed a small square).
        static readonly List<Group> s_Applied = new List<Group> { Groups[0], Groups[4], Groups[15] };
        static readonly List<Group> s_ScreenGroups = new List<Group> { Groups[4], Groups[14], Groups[15] };
        // The ship monitors (map, security, head cam) are tiny pictures: shadows, fog and the like don't show on them.
        static readonly List<Group> s_MonitorGroups = new List<Group> { Groups[0], Groups[4], Groups[14], Groups[15] };
        // Measured on the phone (shader bench 2026-10-18, ship): the player view costs the GPU 39 ms per frame with
        // deferred lighting (its tile/compute path is off, see above, so every pixel goes through every light) and 11 ms
        // forward. Forward is used on every 3D camera unless it changes the picture of the scene (checked on each load).
        static readonly List<Group> s_Forward = new List<Group> { Groups[13] };
        static bool s_UseForward = true;
        static bool s_Searched;
        Texture2D m_Tex;
        float m_Avg;   // average brightness of the last Sample

        public static void Install()
        {
            var go = new GameObject("LCIOS.RenderFix");
            DontDestroyOnLoad(go);
            var f = go.AddComponent<RenderFix>();
            SceneManager.sceneLoaded += (s, mode) => f.StartCoroutine(f.Check(s.name));
        }

        void Start() { InvokeRepeating(nameof(ApplyToAll), 0.5f, 1f); }

        // Keep the chosen settings on cameras that appear later (spectating, new scenes).
        void ApplyToAll()
        {
            if (s_Applied.Count == 0) return;
            foreach (var c in Camera.allCameras)
            {
                Apply(c, c.targetTexture == null ? s_ScreenGroups : c.name == "MainCamera" ? s_Applied : s_MonitorGroups);
                if (s_UseForward && c.targetTexture != null) Apply(c, s_Forward);
            }
        }

        static Camera PlayerCamera()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (c.targetTexture == null) continue;
                if (c.name == "MainCamera") return c;
                if (best == null || c.targetTexture.width > best.targetTexture.width) best = c;
            }
            return best;
        }

        IEnumerator Check(string scene)
        {
            yield return new WaitForSecondsRealtime(4f);
            var cam = PlayerCamera();
            if (cam == null) yield break;
            ApplyToAll();
            yield return Settle();
            float lit = Sample(cam, out string stats);
            if (s_UseForward && lit >= LitNeeded)
            {
                // Same view with deferred lighting, to compare.
                float fwdLit = lit, fwdAvg = m_Avg;
                string fwdStats = stats;
                SetForward(false);
                yield return Settle();
                if (cam == null) yield break;
                float defLit = Sample(cam, out string defStats), defAvg = m_Avg;
                bool same = fwdLit >= defLit * 0.7f && fwdAvg >= defAvg * 0.6f && fwdAvg <= defAvg * 1.6f + 10;
                Log.Info($"[renderfix] '{scene}' forward {fwdStats} | deferred {defStats} -> {(same ? "forward" : "deferred (forward looked different)")}");
                if (same) { SetForward(true); yield return Settle(); if (cam == null) yield break; lit = Sample(cam, out stats); }
            }
            Log.Info($"[renderfix] '{scene}' camera '{cam.name}' output {stats}");
            if (lit >= LitNeeded || s_Searched) yield break;
            s_Searched = true;
            yield return Search(cam);
        }

        IEnumerator Search(Camera cam)
        {
            var tried = new List<Group>(s_Applied);
            Group fixedBy = null;
            foreach (var g in Groups)
            {
                if (tried.Contains(g)) continue;
                tried.Add(g);
                Apply(cam, tried);
                yield return Settle();
                if (cam == null) yield break;
                float lit = Sample(cam, out string stats);
                Log.Info($"[renderfix] + {g.Name} off: {stats}");
                if (lit >= LitNeeded) { fixedBy = g; break; }
            }
            if (fixedBy == null)
            {
                Log.Warn("[renderfix] no feature group brought the picture back; restoring defaults");
                Reset(cam);
                s_Applied.Clear();
                yield break;
            }
            // Turn back on every earlier group the picture does not need.
            for (int i = tried.Count - 2; i >= 0; i--)
            {
                var without = new List<Group>(tried);
                without.RemoveAt(i);
                Reset(cam);
                Apply(cam, without);
                yield return Settle();
                if (cam == null) yield break;
                float lit = Sample(cam, out string stats);
                bool needed = lit < LitNeeded;
                Log.Info($"[renderfix] - {tried[i].Name} back on: {stats} -> {(needed ? "needed off" : "not needed")}");
                if (!needed) tried = without;
            }
            Reset(cam);
            Apply(cam, tried);
            s_Applied.Clear();
            s_Applied.AddRange(tried);
            var names = new List<string>();
            foreach (var g in tried) names.Add(g.Name);
            Log.Info("[renderfix] fixed with: " + string.Join(", ", names));
        }

        void SetForward(bool on)
        {
            s_UseForward = on;
            foreach (var c in Camera.allCameras) if (c.targetTexture != null) Reset(c);
            ApplyToAll();
        }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForEndOfFrame();
        }

        static void Apply(Camera cam, List<Group> groups)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;
            hd.customRenderingSettings = true;
            foreach (var g in groups)
            {
                if (g.NoDynRes) hd.allowDynamicResolution = false;
                Set(hd, g.Off, false);
                Set(hd, g.On, true);
            }
        }

        static void Set(HDAdditionalCameraData hd, string[] fields, bool on)
        {
            if (fields == null) return;
            foreach (var name in fields)
            {
                if (!Enum.TryParse(name, out FrameSettingsField f)) continue;
                hd.renderingPathCustomFrameSettings.SetEnabled(f, on);
                hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            }
        }

        static void Reset(Camera cam)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;
            foreach (FrameSettingsField f in Enum.GetValues(typeof(FrameSettingsField)))
                if ((int)f >= 0 && (int)f < 128) hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = false;
            hd.allowDynamicResolution = true;
        }

        // Share of visibly lit pixels in the camera's render texture.
        float Sample(Camera cam, out string stats)
        {
            stats = "?";
            const int w = 64, h = 40;
            var rt = cam.targetTexture;
            if (rt == null) return 1f;
            var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(rt, small);
                RenderTexture.active = small;
                if (m_Tex == null) m_Tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                m_Tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                m_Tex.Apply(false);
                var px = m_Tex.GetPixels32();
                long r = 0, g = 0, b = 0; int lit = 0;
                var quad = new int[4];   // lit pixels per quarter: bottom-left, bottom-right, top-left, top-right
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    r += c.r; g += c.g; b += c.b;
                    if (Math.Max(c.r, Math.Max(c.g, c.b)) > 8)
                    {
                        lit++;
                        quad[(i % w >= w / 2 ? 1 : 0) + (i / w >= h / 2 ? 2 : 0)]++;
                    }
                }
                int n = px.Length, q = n / 4;
                m_Avg = (r + g + b) / (3f * n);
                stats = $"avg=({r / n},{g / n},{b / n}) lit={lit * 100 / n}% quarters BL/BR/TL/TR={quad[0] * 100 / q}/{quad[1] * 100 / q}/{quad[2] * 100 / q}/{quad[3] * 100 / q}% ({rt.width}x{rt.height} {rt.format})";
                return (float)lit / n;
            }
            catch (Exception e)
            {
                stats = "sample failed: " + e.Message;
                return 1f;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(small);
            }
        }
    }
}
