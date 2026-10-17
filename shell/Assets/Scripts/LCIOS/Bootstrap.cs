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
                    // Free unreferenced assets and managed heap before iOS kills the app for memory.
                    Resources.UnloadUnusedAssets();
                    GC.Collect();
                };
                SceneManager.sceneLoaded += (s, mode) => Log.Info($"scene loaded: '{s.name}' ({mode}) roots={s.rootCount}");
                HalfResolution();
                // Skip each texture's largest mip level: about a quarter of the texture memory, slightly softer textures.
                QualitySettings.globalTextureMipmapLimit = 1;
                Log.Info("texture mip limit set to 1 (half-size textures)");
                TouchProbe.Install();
                RenderProbe.Install();
                RenderFix.Install();
                TouchControls.Install();
                UiScale.Install();
            }
            catch (Exception e)
            {
                Log.Error("bootstrap failed: " + e);
            }
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
