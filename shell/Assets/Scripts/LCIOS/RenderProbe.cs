using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Diagnostics for the black screen: per scene, logs the render pipeline, every camera and canvas, and how many
    // frames the pipeline actually rendered.
    public class RenderProbe : MonoBehaviour
    {
        static int s_Frames, s_Cameras;
        string m_Scene = "";

        public static void Install()
        {
            var go = new GameObject("LCIOS.RenderProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<RenderProbe>();
            RenderPipelineManager.endFrameRendering += (ctx, cams) => { s_Frames++; s_Cameras += cams.Length; };
            SceneManager.sceneLoaded += (s, mode) =>
            {
                p.m_Scene = s.name; p.Invoke(nameof(Describe), 1f);
                p.StartCoroutine(p.SamplePixels(s.name));
            };
        }

        // What actually reaches the screen: average / brightest pixel of the final frame, 3 s and 10 s into the scene.
        System.Collections.IEnumerator SamplePixels(string scene)
        {
            foreach (float wait in new[] { 3f, 7f })
            {
                yield return new WaitForSecondsRealtime(wait);
                yield return new WaitForEndOfFrame();
                Texture2D tex = null;
                try
                {
                    tex = ScreenCapture.CaptureScreenshotAsTexture();
                    var px = tex.GetPixels32();
                    long r = 0, g = 0, b = 0; int max = 0, lit = 0, n = 0;
                    for (int i = 0; i < px.Length; i += 97, n++)
                    {
                        var c = px[i];
                        r += c.r; g += c.g; b += c.b;
                        int m = Math.Max(c.r, Math.Max(c.g, c.b));
                        if (m > max) max = m;
                        if (m > 8) lit++;
                    }
                    Log.Info($"[render] pixels '{scene}' {tex.width}x{tex.height}: avg=({r / n},{g / n},{b / n}) max={max} lit={lit}/{n} pipelineFrames={s_Frames}");
                }
                catch (Exception e)
                {
                    Log.Warn("[render] pixel sample failed: " + e.Message);
                }
                if (tex) Destroy(tex);
            }
        }

        void Describe()
        {
            try
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                Log.Info($"[render] scene '{m_Scene}': pipeline={(rp ? rp.GetType().Name + " '" + rp.name + "'" : "built-in")} " +
                         $"pipelineFrames={s_Frames} camerasRendered={s_Cameras} screen={Screen.width}x{Screen.height}");
                foreach (var c in Camera.allCameras)
                    Log.Info($"[render]   camera '{c.name}' depth={c.depth} target={(c.targetTexture ? c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height : "screen")} " +
                             $"display={c.targetDisplay} rect={c.rect} clear={c.clearFlags} mask={c.cullingMask}");
                foreach (var cv in FindObjectsOfType<Canvas>())
                    if (cv.isRootCanvas)
                        Log.Info($"[render]   canvas '{cv.name}' mode={cv.renderMode} camera={(cv.worldCamera ? cv.worldCamera.name : "-")} active={cv.isActiveAndEnabled}");
            }
            catch (Exception e)
            {
                Log.Warn("[render] describe failed: " + e.Message);
            }
        }
    }
}
