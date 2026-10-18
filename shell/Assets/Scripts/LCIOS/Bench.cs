using System;
using System.Collections;
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
            public string[] Off = new string[0];   // FrameSettingsField names off on the player camera
            public bool NoScreenCamera, HalfView;
        }

        static readonly Config[] Configs =
        {
            new Config { Name = "baseline" },
            new Config { Name = "no shadows", Off = new[] { "ShadowMaps", "Shadowmask" } },
            new Config { Name = "no fog", Off = new[] { "Volumetrics", "ReprojectionForVolumetrics" } },
            new Config { Name = "no post/custom pass", Off = new[] { "Postprocess", "CustomPostProcess", "CustomPass" } },
            new Config { Name = "no screen camera", NoScreenCamera = true },
            new Config { Name = "half view resolution", HalfView = true },
            new Config { Name = "no shadows+fog", Off = new[] { "ShadowMaps", "Shadowmask", "Volumetrics", "ReprojectionForVolumetrics" } },
            new Config { Name = "all of the above", Off = new[] { "ShadowMaps", "Shadowmask", "Volumetrics", "ReprojectionForVolumetrics", "Postprocess", "CustomPostProcess", "CustomPass" }, NoScreenCamera = true, HalfView = true },
            new Config { Name = "baseline again" },
        };

        static bool s_Done;

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
                Camera player = PlayerCamera();
                if (player == null) { Log.Warn("[bench] no player camera"); yield break; }
                var hd = player.GetComponent<HDAdditionalCameraData>();
                Camera screen = null;
                foreach (var cam in Camera.allCameras) if (cam.targetTexture == null) { screen = cam; break; }
                var rt = player.targetTexture;
                int w = rt.width, h = rt.height;

                if (hd != null) Set(hd, c.Off, false, true);
                if (c.NoScreenCamera && screen != null) screen.enabled = false;
                if (c.HalfView) Resize(rt, w / 2, h / 2);

                yield return new WaitForSecondsRealtime(Hold - Measure);
                int frames = 0; float t = 0, worst = 0;
                while (t < Measure)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    t += dt; frames++; if (dt > worst) worst = dt;
                }
                Log.Info($"[bench] {c.Name}: fps={frames / t:0.0} worst={worst * 1000:0}ms");

                if (hd != null) Set(hd, c.Off, true, false);
                if (screen != null) screen.enabled = true;
                if (c.HalfView) Resize(rt, w, h);
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

        static void Resize(RenderTexture rt, int w, int h)
        {
            rt.Release();
            rt.width = w;
            rt.height = h;
            rt.Create();
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

        // Cameras and lights in the scene, to read the numbers against.
        static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var c in Camera.allCameras)
                sb.Append($"camera '{c.name}' depth={c.depth} far={c.farClipPlane:0} target={(c.targetTexture ? $"{c.targetTexture.width}x{c.targetTexture.height} {c.targetTexture.format}" : "screen")}; ");
            int lights = 0, shadowed = 0, point = 0;
            var res = new System.Collections.Generic.Dictionary<string, int>();
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
