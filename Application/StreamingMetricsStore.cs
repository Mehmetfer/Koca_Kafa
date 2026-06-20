using System;
using System.Globalization;
using System.IO;
using System.Text;
using Koca_Kafa.Data.Abstractions;

namespace Koca_Kafa.Application
{
    internal static class StreamingMetricsStore
    {
        private static readonly object Gate = new object();

        public static void Record(IDataPathProvider paths, long ttftMs)
        {
            if (paths == null || ttftMs < 0)
                return;

            lock (Gate)
            {
                try
                {
                    var logsDir = Path.Combine(paths.RootPath, "Logs");
                    Directory.CreateDirectory(logsDir);
                    var path = Path.Combine(logsDir, "streaming_metrics.txt");

                    long count = 0;
                    long sum = 0;
                    if (File.Exists(path))
                    {
                        foreach (var line in File.ReadAllLines(path))
                        {
                            if (!line.StartsWith("count=", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var parts = line.Split('|');
                            foreach (var part in parts)
                            {
                                var kv = part.Split('=');
                                if (kv.Length != 2)
                                    continue;

                                if (string.Equals(kv[0].Trim(), "count", StringComparison.OrdinalIgnoreCase))
                                    long.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                                else if (string.Equals(kv[0].Trim(), "sum_ttft_ms", StringComparison.OrdinalIgnoreCase))
                                    long.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sum);
                            }
                            break;
                        }
                    }

                    count++;
                    sum += ttftMs;
                    var average = count == 0 ? 0 : (double)sum / count;

                    var snapshot = string.Format(
                        CultureInfo.InvariantCulture,
                        "count={0}|sum_ttft_ms={1}|avg_ttft_ms={2:0.0}|updated_utc={3:o}{4}",
                        count,
                        sum,
                        average,
                        DateTime.UtcNow,
                        Environment.NewLine);

                    File.WriteAllText(path, snapshot, Encoding.UTF8);
                }
                catch
                {
                    // metrics must not break chat
                }
            }
        }

        public static double GetAverageTtftMs(IDataPathProvider paths)
        {
            if (paths == null)
                return 0;

            lock (Gate)
            {
                try
                {
                    var path = Path.Combine(paths.RootPath, "Logs", "streaming_metrics.txt");
                    if (!File.Exists(path))
                        return 0;

                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (!line.StartsWith("count=", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var parts = line.Split('|');
                        foreach (var part in parts)
                        {
                            var kv = part.Split('=');
                            if (kv.Length != 2)
                                continue;

                            if (string.Equals(kv[0].Trim(), "avg_ttft_ms", StringComparison.OrdinalIgnoreCase) &&
                                double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var avg))
                                return avg;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return 0;
        }
    }
}
