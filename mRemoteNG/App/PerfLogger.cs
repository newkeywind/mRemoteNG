using System.Diagnostics;

namespace mRemoteNG.App
{
    internal static class PerfLogger
    {
        public static Stopwatch Start()
        {
            return Stopwatch.StartNew();
        }

        public static void Log(string label, Stopwatch stopwatch)
        {
            Logger.Instance.Log.Info($"[PERF] {label}: {stopwatch.ElapsedMilliseconds} ms");
        }

        public static void Log(string message)
        {
            Logger.Instance.Log.Info($"[PERF] {message}");
        }
    }
}