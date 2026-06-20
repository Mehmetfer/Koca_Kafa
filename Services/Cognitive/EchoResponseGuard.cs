using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public static class EchoResponseGuard
    {
        public const double MinClarificationConfidence = DecisionLockGate.IntentResolutionThreshold;

        private static readonly Regex EchoNotPattern = new Regex(
            @"anlad[ıi]m\s*,?\s*.+?\s*['']?(yi|yı|yu|yi)\s+not\s+ettim",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuotedEchoPattern = new Regex(
            @"anlad[ıi]m\s*,?\s*.+?\s*['']([^'']{1,40})['']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SuffixEchoPattern = new Regex(
            @"^['']?(.{1,30})['']?\s*(yi|yı|yu)\s*\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsEchoResponse(string userMessage, string reply)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(reply))
                return false;

            if (EchoNotPattern.IsMatch(reply))
                return true;

            if (QuotedEchoPattern.IsMatch(reply))
                return true;

            var userNorm = Normalize(userMessage);
            var replyNorm = Normalize(reply);
            if (userNorm.Length >= 2 && userNorm.Length <= 24)
            {
                if (string.Equals(replyNorm, userNorm, StringComparison.Ordinal))
                    return true;

                if (SuffixEchoPattern.IsMatch(reply.Trim()))
                    return true;
            }

            var lowerReply = reply.ToLowerInvariant();
            if (lowerReply.Contains("anladım") || lowerReply.Contains("anladim"))
            {
                foreach (var token in ExtractMeaningfulTokens(userMessage))
                {
                    if (token.Length < 2)
                        continue;

                    if (lowerReply.Contains("'" + token) || lowerReply.Contains("’" + token) ||
                        lowerReply.Contains(token + "'yi") || lowerReply.Contains(token + "'yı"))
                        return true;
                }
            }

            return false;
        }

        public static bool ContainsForbiddenFallback(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.Trim().ToLowerInvariant();

            if (lower.StartsWith("anladım", StringComparison.Ordinal) ||
                lower.StartsWith("anladim", StringComparison.Ordinal))
                return true;

            if (lower.Contains("bunu bilmiyorum") || lower == "bilmiyorum baba." || lower == "bilmiyorum.")
                return true;

            if (lower.Contains("'yi not ettim") || lower.Contains("'yı not ettim") ||
                lower.Contains("'yu not ettim"))
                return true;

            if (lower.Contains("bunu not ediyorum"))
                return true;

            if (Regex.IsMatch(lower, @"anlad[ıi]m\s*,.+not\s+ettim"))
                return true;

            return false;
        }

        public static string SanitizeReply(string userMessage, string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return reply;

            if (!IsEchoResponse(userMessage, reply) && !ContainsForbiddenFallback(reply))
                return reply.Trim();

            return ClarificationResponseEngine.BuildClarification(userMessage, null, null);
        }

        private static IList<string> ExtractMeaningfulTokens(string message)
        {
            var lower = Normalize(message);
            return lower
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string Normalize(string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();
            text = Regex.Replace(text, @"[^\p{L}\p{N}\s]", string.Empty);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}
