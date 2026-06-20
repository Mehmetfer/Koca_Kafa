using System;
using System.Globalization;
using System.IO;
using System.Text;
using Koca_Kafa.Data.Abstractions;

namespace Koca_Kafa.Application
{
    internal static class GenerationLog
    {
        public static void Cancelled(IDataPathProvider paths, string input, string reason)
        {
            try
            {
                var logsDir = Path.Combine(paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var line = string.Format(
                    "{0:o} [GenerationCancelled] input=\"{1}\" reason={2}{3}",
                    DateTime.UtcNow,
                    Sanitize(input),
                    reason ?? "unknown",
                    Environment.NewLine);
                File.AppendAllText(Path.Combine(logsDir, "generation.log"), line, Encoding.UTF8);
            }
            catch
            {
                // logging must not break chat
            }
        }

        public static void Timeout(IDataPathProvider paths, string input, double elapsedSeconds)
        {
            try
            {
                var logsDir = Path.Combine(paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:o} [GenerationTimeout] input=\"{1}\" elapsed={2:0.0}s{3}",
                    DateTime.UtcNow,
                    Sanitize(input),
                    elapsedSeconds,
                    Environment.NewLine);
                File.AppendAllText(Path.Combine(logsDir, "generation.log"), line, Encoding.UTF8);
            }
            catch
            {
                // logging must not break chat
            }
        }

        public static void Ttft(IDataPathProvider paths, string input, long ttftMs)
        {
            try
            {
                var logsDir = Path.Combine(paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:o} [TTFT] input=\"{1}\" ms={2}{3}",
                    DateTime.UtcNow,
                    Sanitize(input),
                    ttftMs,
                    Environment.NewLine);
                File.AppendAllText(Path.Combine(logsDir, "generation.log"), line, Encoding.UTF8);
            }
            catch
            {
                // logging must not break chat
            }
        }

        public static void Completed(
            IDataPathProvider paths,
            string input,
            long ttftMs,
            long totalMs,
            int responseChars,
            double averageTtftMs)
        {
            try
            {
                var logsDir = Path.Combine(paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:o} [GenerationCompleted] input=\"{1}\" ttft_ms={2} total_ms={3} chars={4} avg_ttft_ms={5:0.0}{6}",
                    DateTime.UtcNow,
                    Sanitize(input),
                    ttftMs,
                    totalMs,
                    responseChars,
                    averageTtftMs,
                    Environment.NewLine);
                File.AppendAllText(Path.Combine(logsDir, "generation.log"), line, Encoding.UTF8);
            }
            catch
            {
                // logging must not break chat
            }
        }

        private static string Sanitize(string value) =>
            (value ?? string.Empty).Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
    }
}
