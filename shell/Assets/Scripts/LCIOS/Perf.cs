using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LCIOS
{
    // Frame rate: lifts the iOS 30 fps cap, trims costly-but-subtle graphics (like the PC mod HDLethalCompany on low
    // fog/shadow settings) and logs the frame rate so the next step is based on numbers.
    public class Perf : MonoBehaviour
    {
        const int TargetFps = 60;
        const float ReportEvery = 10f;

        float m_Since, m_Worst;
        int m_Frames;
        readonly FrameTiming[] m_Timing = new FrameTiming[1];

        public static void Install()
        {
            // Stack traces for every Debug.Log are expensive under IL2CPP and the game logs a lot.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            var go = new GameObject("LCIOS.Perf");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<Perf>();
            SceneManager.sceneLoaded += (s, mode) => p.Invoke(nameof(Trim), 2f);
        }

        void Start() { InvokeRepeating(nameof(Trim), 5f, 5f); }

        void Update()
        {
            // The game's default "VSync" option sets targetFrameRate=-1, which iOS treats as 30 fps; a frame that misses
            // 30 then waits for the next display refresh and the game drops to 20.
            int t = Application.targetFrameRate;
            if (t <= 0 || t > TargetFps)
            {
                Application.targetFrameRate = TargetFps;
                Log.Info($"[perf] target frame rate {t} -> {TargetFps}");
            }

            float dt = Time.unscaledDeltaTime;
            m_Frames++;
            m_Since += dt;
            if (dt > m_Worst) m_Worst = dt;
            FrameTimingManager.CaptureFrameTimings();
            if (m_Since < ReportEvery) return;
            var sb = new StringBuilder();
            sb.Append($"[perf] '{SceneManager.GetActiveScene().name}' fps={m_Frames / m_Since:0.0} worst={m_Worst * 1000:0}ms");
            if (FrameTimingManager.GetLatestTimings(1, m_Timing) > 0)
                sb.Append($" cpu={m_Timing[0].cpuMainThreadFrameTime:0.0}ms render={m_Timing[0].cpuRenderThreadFrameTime:0.0}ms gpu={m_Timing[0].gpuFrameTime:0.0}ms");
            Log.Info(sb.ToString());
            m_Frames = 0; m_Since = 0; m_Worst = 0;
        }

        // Volumetric fog at the low quality tier, shadows from lamps at the low resolution tier.
        void Trim()
        {
            int fogs = 0, lights = 0;
            foreach (var v in FindObjectsOfType<Volume>())
            {
                var profile = v.sharedProfile;
                if (profile == null || !profile.TryGet(out Fog fog)) continue;
                var (level, custom) = fog.quality.levelAndOverride;
                if (fog.quality.overrideState && level == 0 && !custom) continue;
                fog.quality.overrideState = true;
                fog.quality.levelAndOverride = (0, false);
                fogs++;
                Log.Info($"[perf] fog '{v.name}' quality level={level} custom={custom} budget={fog.volumetricFogBudget} -> low");
            }
            foreach (var l in FindObjectsOfType<HDAdditionalLightData>())
            {
                if (l.type == HDLightType.Directional) continue;
                var (level, custom) = (l.shadowResolution.level, l.shadowResolution.useOverride);
                if (level == 0 && !custom) continue;
                l.SetShadowResolutionLevel(0);
                l.SetShadowResolutionOverride(false);
                lights++;
            }
            if (fogs + lights > 0) Log.Info($"[perf] trimmed: {fogs} fog volume(s), {lights} shadowed light(s) to low tier");
        }
    }
}
