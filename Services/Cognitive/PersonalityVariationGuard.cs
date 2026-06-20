using System;
using System.Linq;

namespace Koca_Kafa.Services.Cognitive
{
    public static class PersonalityVariationGuard
    {
        public static bool ShouldSkipRepeatedGreeting(string signature, string lastSignature)
        {
            if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(lastSignature))
                return false;

            return string.Equals(signature, lastSignature, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldSkipRepeatedEmpathy(string signature, string lastSignature)
        {
            if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(lastSignature))
                return false;

            return string.Equals(signature, lastSignature, StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildGreetingSignature(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return string.Empty;

            var first = reply.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? reply;
            return first.Trim().ToLowerInvariant();
        }

        public static string BuildEmpathySignature(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return string.Empty;

            return string.Join("|", reply.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Take(2));
        }

        public static string LimitEmpathySentences(string reply, int maxSentences = 2)
        {
            if (string.IsNullOrWhiteSpace(reply) || maxSentences <= 0)
                return reply ?? string.Empty;

            var parts = reply.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Take(maxSentences)
                .ToList();

            if (parts.Count == 0)
                return reply.Trim();

            var ending = reply.TrimEnd().EndsWith("?") ? "?" : ".";
            return string.Join(". ", parts) + ending;
        }
    }
}
