using System;
using UnityEngine;
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
                Application.lowMemory += () => Log.Warn("low memory warning");
                SceneManager.sceneLoaded += (s, mode) => Log.Info($"scene loaded: '{s.name}' ({mode}) roots={s.rootCount}");
                TouchProbe.Install();
                RenderProbe.Install();
            }
            catch (Exception e)
            {
                Log.Error("bootstrap failed: " + e);
            }
        }
    }
}
