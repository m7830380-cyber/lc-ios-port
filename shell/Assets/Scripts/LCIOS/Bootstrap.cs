using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Entry point of the iOS additions. Runs before the first scene so a failed boot still leaves a log.
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Log.Init(Application.persistentDataPath);
            Log.Info($"Lethal Company iOS starting. documents={Application.persistentDataPath}");
            Log.Info($"device={SystemInfo.deviceModel} os={SystemInfo.operatingSystem} gpu={SystemInfo.graphicsDeviceName} " +
                     $"api={SystemInfo.graphicsDeviceType} mem={SystemInfo.systemMemorySize}MB unity={Application.unityVersion} " +
                     $"screen={Screen.width}x{Screen.height} safe={Screen.safeArea}");
            try
            {
                Input.simulateMouseWithTouches = false;
                Input.multiTouchEnabled = true;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                Application.lowMemory += () =>
                {
                    Log.Warn("low memory warning");
                    // Unity already unloads unused assets on its own here; doing it again just stalls the game twice.
                };
                SceneManager.sceneLoaded += (s, mode) => Log.Info($"scene loaded: '{s.name}' ({mode}) roots={s.rootCount}");
                HalfResolution();
                HalfScreen();
                // Skip each texture's largest mip level: about a quarter of the texture memory, slightly softer textures.
                QualitySettings.globalTextureMipmapLimit = 1;
                Log.Info("texture mip limit set to 1 (half-size textures)");
                TouchProbe.Install();
                RenderProbe.Install();
                RenderFix.Install();
                TouchControls.Install();
                UiScale.Install();
                Perf.Install();
                Bench.Install();
            }
            catch (Exception e)
            {
                Log.Error("bootstrap failed: " + e);
            }
        }

        // The HUD/menu camera renders the whole HDRP pipeline at the panel's 2796x1290. Half of that is still sharp on a
        // 460 ppi screen and a quarter of the pixels; the 3D view has its own fixed 860x520 texture either way.
        static void HalfScreen()
        {
            int w = Screen.width, h = Screen.height;
            if (Mathf.Max(w, h) <= 1600) return;
            Screen.SetResolution(w / 2, h / 2, true);
            Log.Info($"screen resolution {w}x{h} -> {w / 2}x{h / 2}");
        }

        // Render at half resolution so the HDRP buffers fit under the iOS memory limit.
        static void HalfResolution()
        {
            var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (asset == null)
            {
                Log.Warn("render scale: no HDRP asset, left at full resolution");
                return;
            }
            var settings = asset.currentPlatformRenderPipelineSettings;
            settings.dynamicResolutionSettings.enabled = true;
            settings.dynamicResolutionSettings.forceResolution = true;
            settings.dynamicResolutionSettings.forcedPercentage = 50f;
            asset.currentPlatformRenderPipelineSettings = settings;
            Log.Info("render scale forced to 50%");
        }
    }
}
