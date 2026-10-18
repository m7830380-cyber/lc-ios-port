using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine.LowLevel;

namespace LCIOS
{
    // Poor man's profiler for a release build: a timestamp before every engine step of the frame (scripts, physics,
    // rendering, ...) so the log can say which step eats the frame. The gap after the last step until the next frame
    // starts is reported as "wait" (idle under the frame cap, or waiting for the display).
    public static class LoopTimer
    {
        struct Marker { }

        const string Wait = "wait";
        static readonly Stopwatch s_Clock = Stopwatch.StartNew();
        static readonly Dictionary<string, long> s_Ticks = new Dictionary<string, long>();
        static long s_Last;
        static string s_Current;

        public static void Install()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (Installed(root)) return;
            var phases = root.subSystemList;
            for (int i = 0; i < phases.Length; i++)
            {
                var phase = phases[i];
                if (phase.subSystemList == null) continue;
                var list = new List<PlayerLoopSystem>();
                foreach (var s in phase.subSystemList)
                {
                    list.Add(MarkerFor(phase.type.Name + "." + (s.type != null ? s.type.Name : "?")));
                    list.Add(s);
                }
                list.Add(MarkerFor(i == phases.Length - 1 ? Wait : phase.type.Name + ".gap"));
                phase.subSystemList = list.ToArray();
                phases[i] = phase;
            }
            root.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(root);
            Log.Info($"[perf] frame step timer installed ({phases.Sum(p => p.subSystemList?.Length ?? 0) / 2} steps)");
        }

        static bool Installed(PlayerLoopSystem root) =>
            root.subSystemList != null && root.subSystemList.Any(p => p.subSystemList != null && p.subSystemList.Any(s => s.type == typeof(Marker)));

        static PlayerLoopSystem MarkerFor(string next) => new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Mark(next) };

        static void Mark(string next)
        {
            long now = s_Clock.ElapsedTicks;
            if (s_Current != null)
            {
                s_Ticks.TryGetValue(s_Current, out long t);
                s_Ticks[s_Current] = t + now - s_Last;
            }
            s_Last = now;
            s_Current = next;
        }

        // "top steps in ms per frame", then resets. Reinstalls itself if something replaced the player loop.
        public static string Report(int frames)
        {
            if (!Installed(PlayerLoop.GetCurrentPlayerLoop())) Install();
            if (frames <= 0 || s_Ticks.Count == 0) return "";
            double perMs = 1000.0 / Stopwatch.Frequency / frames;
            var sb = new StringBuilder(" steps:");
            foreach (var kv in s_Ticks.OrderByDescending(k => k.Value).Take(10))
                sb.Append($" {kv.Key.Replace("PlayerLoop", "")}={kv.Value * perMs:0.0}");
            s_Ticks.Clear();
            return sb.ToString();
        }
    }
}
