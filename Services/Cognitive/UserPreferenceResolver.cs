using System;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class UserPreferenceResolver
    {
        public static string ResolveHitap(string ownerName, string memoryContext)
        {
            if (ShouldAvoidBaba(memoryContext))
            {
                var owner = (ownerName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(owner) ||
                    string.Equals(owner, "baba", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return owner;
            }

            return string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();
        }

        public static string AppendHitap(string text, string hitap)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(hitap))
                return (text ?? string.Empty).Trim();

            var trimmed = text.Trim();
            if (trimmed.EndsWith(hitap, StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return trimmed + " " + hitap + ".";
        }

        public static string StripForbiddenNickname(string text, string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(text) || !ShouldAvoidBaba(memoryContext))
                return text ?? string.Empty;

            var result = text;
            result = result.Replace(" baba.", ".");
            result = result.Replace(" baba,", ",");
            result = result.Replace(" baba ", " ");
            result = result.Replace(" baba?", "?");
            result = result.Replace(" baba!", "!");
            if (result.EndsWith(" baba", StringComparison.OrdinalIgnoreCase))
                result = result.Substring(0, result.Length - 5).TrimEnd();

            return result.Trim();
        }

        public static bool ShouldAvoidBaba(string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(memoryContext))
                return false;

            return memoryContext.IndexOf("[Entity:avoid_nickname_baba]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memoryContext.IndexOf("avoid_nickname_baba", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
