using System;
using System.IO;
using System.Text;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    /// <summary>
    /// Internal-only trace writer. Never surfaces debug output to the user.
    /// </summary>
    public static class SelfDebugLogger
    {
        public static void Write(string rootPath, string userMessage, SelfDebugOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || outcome == null)
                return;

            try
            {
                var dir = Path.Combine(rootPath, "Logs", "SelfDebug");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "self_debug_" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log");

                var sb = new StringBuilder();
                sb.AppendLine("--- " + DateTime.UtcNow.ToString("o") + " ---");
                sb.AppendLine("UserMessage: " + Truncate(userMessage, 120));
                sb.AppendLine("Passed: " + outcome.Passed);
                sb.AppendLine("UsedSafeMode: " + outcome.UsedSafeMode);
                sb.AppendLine("Iterations: " + outcome.IterationCount);

                foreach (var iteration in outcome.Iterations)
                {
                    sb.AppendLine("  Iteration " + iteration.Number + " | Fix: " + (iteration.FixStrategy ?? "(none)"));
                    sb.AppendLine("  RootModule: " + (iteration.RootModule ?? "(none)"));
                    foreach (var issue in iteration.Issues)
                        sb.AppendLine("    - " + issue.Type +
                                      (string.IsNullOrWhiteSpace(issue.FailureCode) ? "" : " [" + issue.FailureCode + "]") +
                                      " @ " + issue.RootModule + ": " + issue.Description);
                }

                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Internal logging must never break chat.
            }
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrWhiteSpace(value) || value.Length <= max
                ? value
                : value.Substring(0, max) + "...";
    }
}
