using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Core;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class CoreChatOutputContract
    {
        private static readonly string[] ForbiddenFooterTokens =
        {
            "teşekkürler", "tesekkurler", "sağ ol", "sag ol", "saol",
            "merhaba tekrar", "selam tekrar", "görüşürüz", "gorusuruz"
        };

        private static readonly string[] ForbiddenStandaloneGreetings =
        {
            "merhaba", "selam", "günaydın", "gunaydin", "iyi günler", "iyi gunler"
        };

        private static readonly string[] ForbiddenEmpathyPhrases =
        {
            "üzgün hisset", "uzgun hisset", "üzüldüm", "uzuldum",
            "duyduğuma üzüldüm", "duyduguma uzuldum", "yanındayım", "yanindayim",
            "moralin bozuk", "zor olabilir", "anlıyorum seni", "anliyorum seni",
            "harika bir gün", "muhteşem", "muhtesem", "süpersin", "supersin"
        };

        private static readonly string[] MetaPhrases =
        {
            "bir yapay zeka", "bir dil modeli", "hafızama", "hafizama",
            "kaydettim", "not ettim", "başka bir konuda", "baska bir konuda",
            "system", "assistant", "language model", "dil modeli"
        };

        private static readonly string[] FillerPhrases =
        {
            "aslında", "aslında şöyle", "şunu söyleyeyim", "sunu soyleyeyim",
            "tabii ki", "elbette", "kesinlikle"
        };

        public const int MaxSentences = 3;

        public static string Enforce(string reply, CoreChatIntentKind intent)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return FallbackClarify();

            var text = ReplySanitizer.Sanitize(reply.Trim());
            text = StripEmojis(text);
            text = TextDeduplicator.DeduplicateSentences(text);
            text = StripMetaPhrases(text);
            text = StripFillerPhrases(text);
            text = StripForbiddenEmpathy(text);
            if (intent != CoreChatIntentKind.Greeting)
                text = StripLeadingGreeting(text);
            text = StripTrailingFooterGreetings(text, intent);
            text = LimitSentences(text, MaxSentences);
            text = CollapseWhitespace(text);

            if (string.IsNullOrWhiteSpace(text))
                return FallbackClarify();

            if (!PassesFinalCheck(text, intent))
                return FallbackClarify();

            return text.Trim();
        }

        public static bool PassesFinalCheck(string reply, CoreChatIntentKind intent = CoreChatIntentKind.CasualChat)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            if (ReplySanitizer.ContainsInternalLeakage(reply))
                return false;

            if (CountSentences(reply) > MaxSentences)
                return false;

            if (ContainsForbiddenEmpathy(reply))
                return false;

            if (intent != CoreChatIntentKind.Greeting && EndsWithForbiddenFooter(reply))
                return false;

            if (intent != CoreChatIntentKind.Greeting && IsStandaloneGreetingOnly(reply))
                return false;

            return true;
        }

        public static string FallbackClarify() => "Bunu netleştirebilir misin?";

        public static string FallbackUnknown() => "Bunu bilmiyorum.";

        public static string ResolveUnknown(string userMessage)
        {
            if (IsUserExpressingUnknown(userMessage))
                return FallbackClarify();

            return FallbackUnknown();
        }

        public static bool IsUserExpressingUnknown(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return lower.Contains("bilmiyorum") || lower.Contains("bunu bilmiyorum");
        }

        private static string StripEmojis(string text) =>
            Regex.Replace(text ?? string.Empty, @"[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2600-\u27BF]", string.Empty);

        private static string StripMetaPhrases(string text)
        {
            var result = text;
            foreach (var phrase in MetaPhrases)
            {
                result = Regex.Replace(
                    result,
                    Regex.Escape(phrase),
                    string.Empty,
                    RegexOptions.IgnoreCase);
            }

            return result.Trim();
        }

        private static string StripFillerPhrases(string text)
        {
            var result = text ?? string.Empty;
            foreach (var phrase in FillerPhrases)
            {
                result = Regex.Replace(result, @"\b" + Regex.Escape(phrase) + @"\b", string.Empty, RegexOptions.IgnoreCase);
            }

            return result.Trim();
        }

        private static string StripForbiddenEmpathy(string text)
        {
            var lower = text.ToLowerInvariant();
            foreach (var phrase in ForbiddenEmpathyPhrases)
            {
                if (lower.Contains(phrase))
                    return FallbackClarify();
            }

            return text;
        }

        private static bool ContainsForbiddenEmpathy(string text)
        {
            var lower = (text ?? string.Empty).ToLowerInvariant();
            return ForbiddenEmpathyPhrases.Any(p => lower.Contains(p));
        }

        private static string StripLeadingGreeting(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            foreach (var token in ForbiddenStandaloneGreetings)
            {
                if (trimmed.StartsWith(token + ".", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(token.Length + 1).Trim();
                if (trimmed.StartsWith(token + " ", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(token.Length).Trim();
                if (trimmed.Equals(token, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
            }

            return trimmed;
        }

        private static string StripTrailingFooterGreetings(string text, CoreChatIntentKind intent)
        {
            if (intent == CoreChatIntentKind.Greeting)
                return text;

            var trimmed = text.Trim();
            foreach (var token in ForbiddenFooterTokens)
            {
                if (trimmed.EndsWith(token + ".", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(0, trimmed.Length - token.Length - 1).TrimEnd('.', ' ');
                if (trimmed.EndsWith(token, StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(0, trimmed.Length - token.Length).TrimEnd('.', ' ', ',');
            }

            return trimmed;
        }

        private static bool EndsWithForbiddenFooter(string text)
        {
            var trimmed = (text ?? string.Empty).Trim().TrimEnd('.', '!', '?');
            return ForbiddenFooterTokens.Any(t =>
                trimmed.EndsWith(t, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsStandaloneGreetingOnly(string text)
        {
            var trimmed = (text ?? string.Empty).Trim().TrimEnd('.', '!', '?');
            return ForbiddenStandaloneGreetings.Any(t =>
                string.Equals(trimmed, t, StringComparison.OrdinalIgnoreCase));
        }

        private static string LimitSentences(string text, int maxSentences)
        {
            var parts = SplitSentences(text);
            if (parts.Count <= maxSentences)
                return text;

            return string.Join(". ", parts.Take(maxSentences)).Trim() + ".";
        }

        private static int CountSentences(string text) => SplitSentences(text).Count;

        private static IList<string> SplitSentences(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string CollapseWhitespace(string text) =>
            Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
    }
}
