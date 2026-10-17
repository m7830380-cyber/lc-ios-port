using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LCIOS
{
    // The game's canvases scale with the screen width. On a 19.5:9 phone that makes the HUD and chat about a third
    // larger than on a 16:9 monitor, so scale by height instead: the layout then matches 16:9 with extra room at the sides.
    public static class UiScale
    {
        public static void Install()
        {
            SceneManager.sceneLoaded += (s, mode) => Fix(s.name);
        }

        static void Fix(string scene)
        {
            foreach (var scaler in Object.FindObjectsOfType<CanvasScaler>(true))
            {
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;
                Log.Info($"[ui] '{scene}' canvas '{scaler.name}' ref={scaler.referenceResolution} match={scaler.matchWidthOrHeight} -> 1 (height)");
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
            }
        }
    }
}
