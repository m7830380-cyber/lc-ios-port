using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // One-off measurement: once per launch, after the ship scene has loaded, switches one costly piece off at a time for
    // a few seconds and logs the frame rate of each, then puts everything back. Tells which piece the GPU spends the
    // frame on, so the real fix can be chosen from numbers instead of guesses.
    public class Bench : MonoBehaviour
    {
        const string Scene = "SampleSceneRelay";
        const float StartAfter = 25f, Hold = 8f, Measure = 5f;

        class Config
        {
            public string Name;
            public string[] Off = new string[0];       // FrameSettingsField names off on every camera
            public string[] CamerasOff = new string[0]; // cameras disabled by name ("screen" = the one drawing to the screen)
        }

        static readonly string[] Extras = { "MapCamera", "SecurityCamera", "HeadMountedCamera" };
        static readonly string[] ShadowsFog = { "ShadowMaps", "Shadowmask", "Volumetrics", "ReprojectionForVolumetrics" };

        // The first bench (2026-10-18) showed the player camera's own features cost little (8.2 -> 9.8 fps with all off),
        // so this one looks at the other cameras and the per-camera cost.
        static readonly Config[] Configs =
        {
            new Config { Name = "baseline" },
            new Config { Name = "no map camera", CamerasOff = new[] { "MapCamera" } },
            new Config { Name = "no security camera", CamerasOff = new[] { "SecurityCamera" } },
            new Config { Name = "no head camera", CamerasOff = new[] { "HeadMountedCamera" } },
            new Config { Name = "no extra cameras", CamerasOff = Extras },
            new Config { Name = "shadows+fog off on every camera", Off = ShadowsFog },
            new Config { Name = "no player camera", CamerasOff = new[] { "MainCamera" } },
            new Config { Name = "only screen camera", CamerasOff = new[] { "MainCamera", "MapCamera", "SecurityCamera", "HeadMountedCamera" } },
            new Config { Name = "no cameras at all", CamerasOff = new[] { "MainCamera", "MapCamera", "SecurityCamera", "HeadMountedCamera", "screen" } },
            new Config { Name = "baseline again" },
        };

        static bool s_Done;

        [DllImport("__Internal")] static extern double LCGpuBusyMs();

        public static void Install()
        {
            var go = new GameObject("LCIOS.Bench");
            DontDestroyOnLoad(go);
            var b = go.AddComponent<Bench>();
            SceneManager.sceneLoaded += (s, mode) =>
            {
                if (s.name == Scene && !s_Done) { s_Done = true; b.StartCoroutine(b.Run()); }
            };
        }

        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(StartAfter);
            Log.Info("[bench] start: " + Describe());
            foreach (var c in Configs)
            {
                var disabled = new List<Camera>();
                var changed = new List<HDAdditionalCameraData>();
                foreach (var cam in Camera.allCameras)
                {
                    string name = cam.targetTexture == null ? "screen" : cam.name;
                    if (Array.IndexOf(c.CamerasOff, name) >= 0) { cam.enabled = false; disabled.Add(cam); continue; }
                    var hd = cam.GetComponent<HDAdditionalCameraData>();
                    if (hd != null && c.Off.Length > 0) { Set(hd, c.Off, false, true); changed.Add(hd); }
                }

                yield return new WaitForSecondsRealtime(Hold - Measure);
                int frames = 0; float t = 0, worst = 0;
                double gpu0 = LCGpuBusyMs();
                while (t < Measure)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    t += dt; frames++; if (dt > worst) worst = dt;
                }
                double gpu = (LCGpuBusyMs() - gpu0) / frames;
                Log.Info($"[bench] {c.Name}: fps={frames / t:0.0} worst={worst * 1000:0}ms gpu={gpu:0.0}ms/frame");

                foreach (var cam in disabled) if (cam != null) cam.enabled = true;
                foreach (var hd in changed) if (hd != null) Set(hd, c.Off, true, false);
            }
            Log.Info("[bench] done");
        }

        static void Set(HDAdditionalCameraData hd, string[] fields, bool on, bool overridden)
        {
            foreach (var name in fields)
            {
                if (!Enum.TryParse(name, out FrameSettingsField f)) continue;
                hd.customRenderingSettings = true;
                hd.renderingPathCustomFrameSettings.SetEnabled(f, on);
                hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = overridden;
            }
        }

        // Cameras and lights in the scene, to read the numbers against.
        static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var c in Camera.allCameras)
                sb.Append($"camera '{c.name}' depth={c.depth} far={c.farClipPlane:0} target={(c.targetTexture ? $"{c.targetTexture.width}x{c.targetTexture.height} {c.targetTexture.format}" : "screen")}; ");
            int lights = 0, shadowed = 0, point = 0;
            var res = new Dictionary<string, int>();
            foreach (var l in FindObjectsOfType<HDAdditionalLightData>())
            {
                var light = l.GetComponent<Light>();
                if (light == null || !light.enabled) continue;
                lights++;
                if (light.shadows == LightShadows.None) continue;
                shadowed++;
                if (l.type == HDLightType.Point) point++;
                string key = l.shadowResolution.useOverride ? "custom" + l.shadowResolution.@override : "tier" + l.shadowResolution.level;
                res.TryGetValue(key, out int n); res[key] = n + 1;
            }
            sb.Append($"lights={lights} shadowed={shadowed} (point={point}) shadow res:");
            foreach (var kv in res) sb.Append($" {kv.Key}x{kv.Value}");
            return sb.ToString();
        }
    }
}
