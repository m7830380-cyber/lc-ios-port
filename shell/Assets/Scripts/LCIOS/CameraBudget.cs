using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Frame rate. Measured on the phone (bench 2026-10-18, ship, GPU 100% busy): every HDRP camera costs the GPU a lot
    // whatever its size: the 34x24 head cam 38 ms, the security cam 24 ms, the map 16 ms, the HUD camera 16 ms for
    // nothing but the canvas, the player view 38 ms. So:
    //  - the monitor cameras (map, security, head cam) render a few times a second, never two in the same frame;
    //  - the HUD camera skips the HDRP pipeline and draws only its canvas (checked against the HDRP picture, else undone).
    public class CameraBudget : MonoBehaviour
    {
        // Seconds between two renders of each monitor camera.
        static readonly Dictionary<string, float> Interval = new Dictionary<string, float>
        {
            { "MapCamera", 1f / 3 }, { "SecurityCamera", 1f }, { "HeadMountedCamera", 1f },
        };
        const float ReportEvery = 30f;

        readonly Dictionary<Camera, float> m_Last = new Dictionary<Camera, float>();
        readonly List<Camera> m_Held = new List<Camera>();
        Camera[] m_Cameras = new Camera[16];
        readonly Dictionary<string, int> m_Renders = new Dictionary<string, int>();
        float m_Since;

        static CameraBudget s_Instance;
        static readonly ShaderTagId[] UiPasses = { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("ForwardOnly"), new ShaderTagId("Forward") };
        HDAdditionalCameraData m_UiCamera;

        public static void Install()
        {
            var go = new GameObject("LCIOS.CameraBudget");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<CameraBudget>();
            // Put back the cameras held off for this frame once it is rendered, so the game finds them as it left them.
            RenderPipelineManager.endContextRendering += (ctx, cams) => s_Instance.Release();
            SceneManager.sceneLoaded += (s, mode) => s_Instance.StartCoroutine(s_Instance.CheapUi(s.name));
        }

        void Release()
        {
            foreach (var c in m_Held) if (c != null) c.enabled = true;
            m_Held.Clear();
        }

        // After the game's Updates (which switch the monitor cameras on) and before rendering.
        void LateUpdate()
        {
            Release();
            float now = Time.unscaledTime;
            if (Camera.allCamerasCount > m_Cameras.Length) m_Cameras = new Camera[Camera.allCamerasCount * 2];
            int n = Camera.GetAllCameras(m_Cameras);
            Camera pick = null;
            float pickWait = 0;
            for (int i = 0; i < n; i++)
            {
                var cam = m_Cameras[i];
                if (cam.targetTexture == null || !Interval.TryGetValue(cam.name, out float interval)) continue;
                if (!m_Last.TryGetValue(cam, out float last))
                {
                    last = float.NegativeInfinity;
                    // Keep its HDRP history while it skips frames instead of rebuilding it every render.
                    var hd = cam.GetComponent<HDAdditionalCameraData>();
                    if (hd != null) hd.hasPersistentHistory = true;
                }
                float wait = (now - last) / interval;   // >= 1: due
                if (wait >= 1 && (pick == null || wait > pickWait)) { pick = cam; pickWait = wait; }
                m_Held.Add(cam);
            }
            if (pick != null)
            {
                m_Held.Remove(pick);
                m_Last[pick] = now;
                m_Renders.TryGetValue(pick.name, out int r); m_Renders[pick.name] = r + 1;
            }
            foreach (var c in m_Held) c.enabled = false;

            m_Since += Time.unscaledDeltaTime;
            if (m_Since >= ReportEvery)
            {
                var parts = new List<string>();
                foreach (var kv in m_Renders) parts.Add($"{kv.Key}={kv.Value / m_Since:0.0}/s");
                Log.Info($"[camerabudget] monitor renders: {(parts.Count > 0 ? string.Join(" ", parts) : "none")} hud={(m_UiCamera != null ? "cheap" : "hdrp")}");
                m_Renders.Clear(); m_Since = 0;
            }
        }

        // The HUD camera in the game scenes renders only the UI layer (the player view is a picture on its canvas).
        static Camera HudCamera()
        {
            int ui = 1 << LayerMask.NameToLayer("UI");
            foreach (var c in Camera.allCameras)
                if (c.targetTexture == null && c.cullingMask == ui) return c;
            return null;
        }

        IEnumerator CheapUi(string scene)
        {
            yield return new WaitForSecondsRealtime(6f);
            var cam = HudCamera();
            if (cam == null) yield break;
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null || hd.hasCustomRender) yield break;

            yield return new WaitForEndOfFrame();
            var before = Capture();
            hd.customRender += RenderUi;
            for (int i = 0; i < 10; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var after = Capture();
            if (before == null || after == null || cam == null)
            {
                if (hd != null) hd.customRender -= RenderUi;
                Log.Warn($"[camerabudget] '{scene}' hud: could not compare pictures, left on HDRP");
                yield break;
            }
            int diff = 0, litBefore = 0, litAfter = 0;
            for (int i = 0; i < before.Length; i++)
            {
                Color32 a = before[i], b = after[i];
                diff += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                if (Math.Max(a.r, Math.Max(a.g, a.b)) > 8) litBefore++;
                if (Math.Max(b.r, Math.Max(b.g, b.b)) > 8) litAfter++;
            }
            float avg = diff / (3f * before.Length);
            bool ok = avg < 40 && litAfter * 2 >= litBefore;
            Log.Info($"[camerabudget] '{scene}' hud without HDRP: difference={avg:0.0}/255 lit {litBefore} -> {litAfter} of {before.Length} -> {(ok ? "kept" : "undone")}");
            if (ok) m_UiCamera = hd;
            else hd.customRender -= RenderUi;
        }

        // Clear, then draw the camera's UI-layer renderers (its canvas) straight to the screen.
        static void RenderUi(ScriptableRenderContext ctx, HDCamera hdCamera)
        {
            var cam = hdCamera.camera;
            ctx.SetupCameraProperties(cam);
            var cmd = new CommandBuffer { name = "LCIOS HUD" };
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.ClearRenderTarget(true, true, Color.black);
            ctx.ExecuteCommandBuffer(cmd);
            cmd.Release();
            if (!cam.TryGetCullingParameters(out var p)) return;
            var culled = ctx.Cull(ref p);
            var draw = new DrawingSettings(UiPasses[0], new SortingSettings(cam) { criteria = SortingCriteria.CommonTransparent });
            for (int i = 1; i < UiPasses.Length; i++) draw.SetShaderPassName(i, UiPasses[i]);
            var filter = new FilteringSettings(RenderQueueRange.all, cam.cullingMask);
            ctx.DrawRenderers(culled, ref draw, ref filter);
        }

        // A grid of screen pixels from the finished frame.
        static Color32[] Capture()
        {
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                var px = tex.GetPixels32();
                var grid = new Color32[px.Length / 97];
                for (int i = 0; i < grid.Length; i++) grid[i] = px[i * 97];
                return grid;
            }
            catch (Exception e)
            {
                Log.Warn("[camerabudget] screen capture failed: " + e.Message);
                return null;
            }
            finally
            {
                if (tex) Destroy(tex);
            }
        }
    }
}
