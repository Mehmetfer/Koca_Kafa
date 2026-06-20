using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.AI.Abstractions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class CoreChatResult
    {
        public string Reply { get; set; }
        public bool UseLlm { get; set; }
        public CoreChatIntentKind Intent { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseSource { get; set; }
    }

    public interface ICoreChatEngine
    {
        CoreChatResult Process(string userMessage, IReadOnlyList<ChatMessage> history = null);
        string BuildMinimalSystemPrompt(CoreChatIntentKind intent);
    }

    public sealed class CoreChatEngine : ICoreChatEngine
    {
        private static readonly Regex SimpleMathPattern = new Regex(
            @"^\s*(-?\d+(?:[.,]\d+)?)\s*([+\-*/x×])\s*(-?\d+(?:[.,]\d+)?)\s*(?:kaç|kac)?\s*\??\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CoreChatResult Process(string userMessage, IReadOnlyList<ChatMessage> history = null)
        {
            var message = (userMessage ?? string.Empty).Trim();
            var intent = CoreChatIntentClassifier.Classify(message);

            if (CoreChatOutputContract.IsUserExpressingUnknown(message))
            {
                return BuildResult(CoreChatOutputContract.FallbackClarify(), intent, "core:clarify");
            }

            if (LooksEmotional(message))
            {
                return BuildResult(CoreChatOutputContract.FallbackClarify(), intent, "core:neutral");
            }

            var deterministic = TryBuildDeterministic(message, intent);
            if (!string.IsNullOrWhiteSpace(deterministic))
            {
                return BuildResult(deterministic, intent, "core:deterministic");
            }

            if (intent == CoreChatIntentKind.Explanation || intent == CoreChatIntentKind.Question)
            {
                return BuildResult(CoreChatOutputContract.FallbackUnknown(), intent, "core:unknown");
            }

            return new CoreChatResult
            {
                UseLlm = true,
                Intent = intent,
                SystemPrompt = BuildMinimalSystemPrompt(intent),
                ResponseSource = "core:llm"
            };
        }

        public string BuildMinimalSystemPrompt(CoreChatIntentKind intent)
        {
            var task = "Answer briefly, accurately, and directly.";
            switch (intent)
            {
                case CoreChatIntentKind.Explanation:
                    task = "Give a short factual explanation in plain language.";
                    break;
                case CoreChatIntentKind.Question:
                    task = "Answer the question directly without guessing.";
                    break;
                case CoreChatIntentKind.Request:
                    task = "Acknowledge the request and ask one clarifying question if needed.";
                    break;
                case CoreChatIntentKind.Greeting:
                    task = "Reply with a short professional greeting.";
                    break;
                case CoreChatIntentKind.CasualChat:
                    task = "Reply naturally, briefly, and professionally.";
                    break;
            }

            return string.Join("\n", new[]
            {
                "PROFESSIONAL CORE CHAT — junior-mid assistant.",
                "Priority: accuracy, clarity, brevity, user fit.",
                "Rules:",
                "- 1 to 3 short sentences maximum.",
                "- Same language as the user.",
                "- Calm, clear, helpful, restrained tone.",
                "- No memory, empathy, personality simulation, or tools.",
                "- No repeated greetings, filler, emojis, or meta commentary.",
                "- If unsure, say you do not know or ask one clarifying question.",
                "- Never invent facts.",
                "- " + task
            });
        }

        private static CoreChatResult BuildResult(string reply, CoreChatIntentKind intent, string source) =>
            new CoreChatResult
            {
                Reply = CoreChatOutputContract.Enforce(reply, intent),
                UseLlm = false,
                Intent = intent,
                ResponseSource = source
            };

        private static string TryBuildDeterministic(string message, CoreChatIntentKind intent)
        {
            var languageSwitch = TryBuildLanguageSwitch(message);
            if (!string.IsNullOrWhiteSpace(languageSwitch))
                return languageSwitch;

            var math = TrySolveSimpleMath(message);
            if (!string.IsNullOrWhiteSpace(math))
                return math;

            var known = CoreChatKnowledgeBaseline.TryAnswer(message);
            if (!string.IsNullOrWhiteSpace(known))
                return known;

            switch (intent)
            {
                case CoreChatIntentKind.Greeting:
                    return TryBuildGreeting(message);
                case CoreChatIntentKind.CasualChat:
                    return TryBuildCasualChat(message);
                case CoreChatIntentKind.Request:
                    return TryBuildRequestAck(message);
                default:
                    return null;
            }
        }

        private static string TryBuildGreeting(string message)
        {
            var lower = Normalize(message);
            if (lower.Contains("günaydın") || lower.Contains("gunaydin"))
                return "Günaydın. Size nasıl yardımcı olabilirim?";
            if (lower.Contains("iyi günler") || lower.Contains("iyi gunler"))
                return "İyi günler. Size nasıl yardımcı olabilirim?";
            if (lower.Contains("hello") || lower.Contains("hi"))
                return "Hello. How can I help you?";
            if (lower.Contains("merhaba"))
                return "Merhaba. Size nasıl yardımcı olabilirim?";
            return "Merhaba. Size nasıl yardımcı olabilirim?";
        }

        private static string TryBuildCasualChat(string message)
        {
            var lower = Normalize(message);
            if (ContainsAny(lower, "nasılsın", "nasilsin", "naber", "ne haber", "n'aber"))
            {
                if (IsEnglish(message))
                    return "I'm doing well, thank you. How can I help you?";
                return "İyiyim, teşekkür ederim. Size nasıl yardımcı olabilirim?";
            }

            if (ContainsAny(lower, "nasıl gidiyor", "nasil gidiyor"))
                return "İyi gidiyor, teşekkür ederim. Size nasıl yardımcı olabilirim?";

            return null;
        }

        private static string TryBuildRequestAck(string message)
        {
            if (IsEnglish(message))
                return "Understood. What exactly would you like me to do?";

            return "Tam olarak ne yapmamı istersin?";
        }

        private static string TryBuildLanguageSwitch(string message)
        {
            var lower = Normalize(message);
            if (ContainsAny(lower,
                "ingilizce konuşalım", "ingilizce konusalim", "english please",
                "let's speak english", "lets speak english", "speak english",
                "ingilizce devam", "switch to english"))
                return "Tamam, İngilizce konuşabiliriz.";

            if (ContainsAny(lower,
                "türkçe konuşalım", "turkce konusalim", "türkçe devam", "turkce devam",
                "speak turkish", "let's speak turkish"))
                return "Tamam, Türkçe devam edebiliriz.";

            return null;
        }

        private static string TrySolveSimpleMath(string message)
        {
            var trimmed = (message ?? string.Empty).Trim();
            var match = SimpleMathPattern.Match(trimmed);
            if (!match.Success)
            {
                var embedded = Regex.Match(
                    trimmed,
                    @"(-?\d+(?:[.,]\d+)?)\s*([+\-*/x×])\s*(-?\d+(?:[.,]\d+)?)",
                    RegexOptions.IgnoreCase);
                if (!embedded.Success)
                    return null;

                match = embedded;
            }

            if (!TryParseNumber(match.Groups[1].Value, out var left) ||
                !TryParseNumber(match.Groups[3].Value, out var right))
                return null;

            var op = match.Groups[2].Value;
            double result;
            switch (op)
            {
                case "+":
                    result = left + right;
                    break;
                case "-":
                    result = left - right;
                    break;
                case "*":
                case "x":
                case "×":
                    result = left * right;
                    break;
                case "/":
                    if (Math.Abs(right) < double.Epsilon)
                        return null;
                    result = left / right;
                    break;
                default:
                    return null;
            }

            if (Math.Abs(result - Math.Round(result)) < 0.000001)
                return ((int)Math.Round(result)).ToString(CultureInfo.InvariantCulture);

            return result.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static bool TryParseNumber(string token, out double value)
        {
            var normalized = (token ?? string.Empty).Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool LooksEmotional(string message)
        {
            var lower = Normalize(message);
            return ContainsAny(lower,
                "moralim", "üzgün", "uzgun", "yalnız", "yalniz", "kötü hissed", "kotu hissed",
                "depresyon", "ağlıyorum", "agliyorum", "sad", "depressed", "lonely");
        }

        private static bool IsEnglish(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return ContainsAny(lower, "what", "how", "hello", "english", "speak", "help you");
        }

        private static string Normalize(string message)
        {
            try
            {
                return (message ?? string.Empty).Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            }
            catch
            {
                return (message ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
