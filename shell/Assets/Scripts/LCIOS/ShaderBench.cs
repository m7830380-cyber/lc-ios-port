using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // One-off measurement of the player view (MainCamera), once in the ship and once on the first moon: with the ship
    // monitors held off, switches one thing off at a time (an HDRP feature, the lights, or every object drawn with one
    // shader) for a few seconds and logs the GPU time per frame, then puts exactly that thing back. Says which
    // shaders and effects the GPU spends the frame on, so they can be cut for frame rate.
    public class ShaderBench : MonoBehaviour
    {
        const float StartAfter = 25f, Settle = 2f, Measure = 3f;
        const int TopShaders = 10;

        class Test
        {
            public string Name;
            public string[] Off = new string[0];   // FrameSettingsField names off on the player camera
            public Action Apply, Undo;
        }

        static bool s_Ship, s_Moon;
        [DllImport("__Internal")] static extern double LCGpuBusyMs();

        public static void Install()
        {
            var go = new GameObject("LCIOS.ShaderBench");
            DontDestroyOnLoad(go);
            var b = go.AddComponent<ShaderBench>();
            SceneManager.sceneLoaded += (s, mode) =>
            {
                if (s.name == "SampleSceneRelay" && !s_Ship) { s_Ship = true; b.StartCoroutine(b.Run(s.name)); }
                else if (s.name.StartsWith("Level") && !s_Moon) { s_Moon = true; b.StartCoroutine(b.Run(s.name)); }
            };
        }

        static Camera MainCamera()
        {
            foreach (var c in Camera.allCameras) if (c.name == "MainCamera" && c.targetTexture != null) return c;
            return null;
        }

        IEnumerator Run(string scene)
        {
            yield return new WaitForSecondsRealtime(StartAfter);
            var cam = MainCamera();
            var hd = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
            if (hd == null) { Log.Warn($"[shaderbench] '{scene}': no player camera"); yield break; }
            CameraBudget.HoldAll = true;
            try
            {
                Log.Info($"[shaderbench] '{scene}' start (ship monitors held off). Stand still for about 2 minutes.");
                foreach (var t in Tests(cam, hd))
                {
                    if (cam == null) break;
                    var saved = Save(hd, t.Off);
                    Set(hd, t.Off);
                    try { t.Apply?.Invoke(); }
                    catch (Exception e) { Log.Warn($"[shaderbench] {t.Name}: {e.Message}"); }
                    yield return new WaitForSecondsRealtime(Settle);
                    int frames = 0; float time = 0, worst = 0;
                    double gpu0 = LCGpuBusyMs();
                    while (time < Measure)
                    {
                        yield return null;
                        float dt = Time.unscaledDeltaTime;
                        time += dt; frames++; if (dt > worst) worst = dt;
                    }
                    double gpu = (LCGpuBusyMs() - gpu0) / frames;
                    Log.Info($"[shaderbench] {t.Name}: fps={frames / time:0.0} gpu={gpu:0.0}ms/frame worst={worst * 1000:0}ms");
                    try { t.Undo?.Invoke(); }
                    catch (Exception e) { Log.Warn($"[shaderbench] undo {t.Name}: {e.Message}"); }
                    if (hd != null) Restore(hd, saved);
                }
                Log.Info($"[shaderbench] '{scene}' done");
            }
            finally
            {
                CameraBudget.HoldAll = false;
            }
        }

        IEnumerable<Test> Tests(Camera cam, HDAdditionalCameraData hd)
        {
            var list = new List<Test>
            {
                new Test { Name = "baseline" },
                new Test { Name = "no opaque objects", Off = new[] { "OpaqueObjects" } },
                new Test { Name = "no transparent objects", Off = new[] { "TransparentObjects" } },
                new Test { Name = "no opaque+transparent (fixed cost left)", Off = new[] { "OpaqueObjects", "TransparentObjects" } },
                new Test { Name = "forward instead of deferred", Off = new[] { "LitShaderMode" } },
                new Test { Name = "no shadows", Off = new[] { "ShadowMaps", "Shadowmask", "ContactShadows", "ScreenSpaceShadows" } },
                new Test { Name = "no fog", Off = new[] { "Volumetrics", "ReprojectionForVolumetrics", "AtmosphericScattering" } },
                new Test { Name = "no post processing", Off = new[] { "Postprocess", "CustomPostProcess" } },
                new Test { Name = "no custom passes", Off = new[] { "CustomPass" } },
                new Test { Name = "no SSAO", Off = new[] { "SSAO" } },
                new Test { Name = "no reflections", Off = new[] { "SkyReflection", "ReflectionProbe", "PlanarProbe" } },
                new Test { Name = "no decals", Off = new[] { "Decals", "DecalLayers" } },
                new Test { Name = "no motion vectors/refraction", Off = new[] { "MotionVectors", "ObjectMotionVectors", "TransparentsWriteMotionVector", "Refraction", "Distortion", "LowResTransparent", "TransparentPrepass", "TransparentPostpass" } },
            };

            var aa = hd.antialiasing;
            list.Add(new Test { Name = $"no antialiasing (was {aa})", Apply = () => hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None, Undo = () => { if (hd) hd.antialiasing = aa; } });

            var lightsOff = new List<Light>();
            list.Add(new Test
            {
                Name = "only directional lights",
                Apply = () =>
                {
                    foreach (var l in FindObjectsOfType<Light>())
                        if (l.enabled && l.type != LightType.Directional) { l.enabled = false; lightsOff.Add(l); }
                },
                Undo = () => { foreach (var l in lightsOff) if (l) l.enabled = true; lightsOff.Clear(); },
            });

            // Objects grouped by shader, the groups with the most visible objects first.
            var byShader = new Dictionary<string, List<Renderer>>();
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible || r is ParticleSystemRenderer) continue;
                var shaders = new HashSet<string>();
                foreach (var m in r.sharedMaterials) if (m != null && m.shader != null) shaders.Add(m.shader.name);
                foreach (var s in shaders)
                {
                    if (!byShader.TryGetValue(s, out var rs)) byShader[s] = rs = new List<Renderer>();
                    rs.Add(r);
                }
            }
            var order = new List<string>(byShader.Keys);
            order.Sort((a, b) => byShader[b].Count.CompareTo(byShader[a].Count));
            var summary = new List<string>();
            foreach (var s in order) summary.Add($"{s}x{byShader[s].Count}");
            Log.Info("[shaderbench] visible objects by shader: " + string.Join(", ", summary));

            for (int i = 0; i < order.Count && i < TopShaders; i++)
            {
                var rs = byShader[order[i]];
                var hidden = new List<Renderer>();
                list.Add(new Test
                {
                    Name = $"hide shader '{order[i]}' ({rs.Count} objects)",
                    Apply = () => { foreach (var r in rs) if (r && r.enabled) { r.enabled = false; hidden.Add(r); } },
                    Undo = () => { foreach (var r in hidden) if (r) r.enabled = true; hidden.Clear(); },
                });
            }
            var allHidden = new List<Renderer>();
            list.Add(new Test
            {
                Name = "hide particles",
                Apply = () => { foreach (var r in FindObjectsOfType<ParticleSystemRenderer>()) if (r.enabled) { r.enabled = false; allHidden.Add(r); } },
                Undo = () => { foreach (var r in allHidden) if (r) r.enabled = true; allHidden.Clear(); },
            });
            list.Add(new Test { Name = "baseline again" });
            return list;
        }

        struct Saved { public FrameSettingsField Field; public bool Overridden, Enabled; }

        static List<Saved> Save(HDAdditionalCameraData hd, string[] fields)
        {
            var saved = new List<Saved>();
            foreach (var name in fields)
            {
                if (!Enum.TryParse(name, out FrameSettingsField f)) continue;
                saved.Add(new Saved
                {
                    Field = f,
                    Overridden = hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f],
                    Enabled = hd.renderingPathCustomFrameSettings.IsEnabled(f),
                });
            }
            return saved;
        }

        static void Set(HDAdditionalCameraData hd, string[] fields)
        {
            if (fields.Length == 0) return;
            hd.customRenderingSettings = true;
            foreach (var name in fields)
            {
                if (!Enum.TryParse(name, out FrameSettingsField f)) continue;
                hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
                hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            }
        }

        static void Restore(HDAdditionalCameraData hd, List<Saved> saved)
        {
            foreach (var s in saved)
            {
                hd.renderingPathCustomFrameSettings.SetEnabled(s.Field, s.Enabled);
                hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)s.Field] = s.Overridden;
            }
        }
    }
}
