using System;
using System.Globalization;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    /// <summary>
    /// Deterministic natural replies for greetings, small talk, and empathy fallbacks.
    /// Prevents "Bunu bilmiyorum" on social exchanges.
    /// </summary>
    public static class GreetingEngine
    {
        private static readonly string[] PureGreetingPhrases =
        {
            "selam",
            "merhaba",
            "günaydın",
            "gunaydin",
            "iyi günler",
            "iyi gunler",
            "iyi akşamlar",
            "iyi aksamlar",
            "iyi geceler",
            "hey",
            "sa"
        };

        private static readonly string[] SmallTalkPhrases =
        {
            "nasılsın",
            "nasilsin",
            "n'aber",
            "naber",
            "ne haber",
            "nasıl gidiyor",
            "nasil gidiyor",
            "nasılsınız",
            "nasilsiniz"
        };

        public static bool IsGreetingMessage(string message)
        {
            var normalized = Normalize(message);
            if (normalized.Length == 0)
                return false;

            if (ContainsAny(normalized, SmallTalkPhrases))
                return false;

            if (normalized.Contains("?"))
                return false;

            return ContainsAny(normalized, PureGreetingPhrases) ||
                   IsOnlyTokens(normalized, PureGreetingPhrases);
        }

        public static bool IsSmallTalkMessage(string message)
        {
            var normalized = Normalize(message);
            if (normalized.Length == 0)
                return false;

            return ContainsAny(normalized, SmallTalkPhrases);
        }

        public static bool IsSocialExchange(string message, MessageIntent intent)
        {
            if (intent == MessageIntent.Greeting || intent == MessageIntent.Joke)
                return true;

            return IsGreetingMessage(message) || IsSmallTalkMessage(message);
        }

        public static bool IsHumbleFallbackText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lower = text.Trim().ToLowerInvariant();
            return lower.StartsWith("bunu bilmiyorum", StringComparison.Ordinal) ||
                   lower == "bilmiyorum baba." ||
                   lower == "bilmiyorum.";
        }

        public static string TryBuildDirectReply(
            string userMessage,
            MessageIntent intent,
            EmpathyAnalysis empathy,
            string ownerName,
            string memoryContext = null)
        {
            if (CreativeTaskEngine.IsCreativeTask(userMessage))
                return null;

            if (MessageCategoryClassifier.IsEmotionalStatement(userMessage))
                return null;

            if (MessageCategoryClassifier.IsImplicitEmotionalStatement(userMessage))
                return null;

            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);

            if (IsSmallTalkMessage(userMessage))
                return BuildSmallTalkReply(hitap);

            if (intent == MessageIntent.Greeting || IsGreetingMessage(userMessage))
                return BuildGreetingReply(userMessage, hitap);

            return null;
        }

        public static string BuildIntentAwareFallback(ResponseQualityContext context)
        {
            var memoryContext = context?.MemoryContext;
            var hitap = UserPreferenceResolver.ResolveHitap(context?.OwnerName, memoryContext);
            var user = context?.UserMessage ?? string.Empty;
            var intent = context?.Intent?.Intent ?? MessageIntent.Unknown;

            if (IsSmallTalkMessage(user))
                return BuildSmallTalkReply(hitap);

            if (intent == MessageIntent.Greeting || IsGreetingMessage(user))
                return BuildGreetingReply(user, hitap);

            if (MessageCategoryClassifier.IsEmotionalStatement(user))
            {
                var empathyReply = EmpathyResponseEngine.TryBuildDirectReply(
                    user,
                    context?.OwnerName,
                    context?.Empathy,
                    memoryContext);
                if (!string.IsNullOrWhiteSpace(empathyReply))
                    return empathyReply;
            }

            if (MessageCategoryClassifier.IsImplicitEmotionalStatement(user) &&
                ImplicitEmotionDetector.ShouldTriggerEmpathy(user))
            {
                var implicitReply = EmpathyResponseEngine.TryBuildImplicitReply(
                    user,
                    context?.OwnerName,
                    memoryContext);
                if (!string.IsNullOrWhiteSpace(implicitReply))
                    return implicitReply;
            }

            if (intent == MessageIntent.Emotion || context?.Empathy?.RequiresEmpathyFirst == true)
                return BuildEmpathyFallback(context?.Empathy, hitap);

            if (intent == MessageIntent.Joke)
                return "Haha, güldürdün " + hitap + ".";

            if (context?.Plan?.RequiredMemory == true && context.HadMemoryResults)
            {
                var recall = MemoryRecallHelper.TryBuildDirectRecallReply(
                    user,
                    context.MemoryContext,
                    context.OwnerName);
                if (!string.IsNullOrWhiteSpace(recall))
                    return recall;
            }

            if (context?.KnowledgeKind != KnowledgeQuestionKind.None ||
                KnowledgeQuestionClassifier.IsKnowledgeIntent(user))
            {
                var knowledge = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = context?.KnowledgeKind ?? KnowledgeQuestionClassifier.Classify(user),
                    UserMessage = user,
                    OwnerName = context?.OwnerName,
                    MemoryContext = context?.MemoryContext,
                    HadMemoryResults = context?.HadMemoryResults ?? false,
                    HadRagResults = context?.HadRagResults ?? false,
                    MemoryCount = context?.HadMemoryResults == true ? 1 : 0
                });
                if (!string.IsNullOrWhiteSpace(knowledge))
                    return knowledge;

                return "Bu konuda net bir bilgim yok " + hitap + ", ama bana biraz daha detay verirsen yardımcı olmaya çalışırım.";
            }

            return ClarificationResponseEngine.BuildClarification(user, context?.OwnerName, memoryContext);
        }

        public static string BuildGreetingReply(string userMessage, string hitap)
        {
            var lower = Normalize(userMessage);
            var suffix = string.IsNullOrWhiteSpace(hitap) ? "!" : " " + hitap + "!";

            if (lower.Contains("günaydın") || lower.Contains("gunaydin"))
                return "Günaydın" + suffix + (string.IsNullOrWhiteSpace(hitap) ? " Umarım güzel bir gün olur." : " Umarım güzel bir gün olur.");

            if (lower.Contains("iyi günler") || lower.Contains("iyi gunler"))
                return "İyi günler" + suffix;

            if (lower.Contains("merhaba"))
                return "Merhaba" + suffix;

            return string.IsNullOrWhiteSpace(hitap) ? "Selam 👋" : "Selam " + hitap + " 👋";
        }

        public static string BuildSmallTalkReply(string hitap) =>
            string.IsNullOrWhiteSpace(hitap)
                ? "İyiyim, teşekkürler. Sen nasılsın?"
                : "İyiyim " + hitap + ", teşekkürler. Sen nasılsın?";

        public static string BuildEmpathyFallback(EmpathyAnalysis empathy, string hitap)
        {
            var opener = (empathy?.SampleOpener ?? string.Empty).Trim();
            if (opener.Length > 0)
            {
                var followUp = (empathy?.FollowUpQuestion ?? string.Empty).Trim();
                if (followUp.Length > 0 && !string.Equals(followUp, "Bir şey mi oldu?", StringComparison.OrdinalIgnoreCase))
                    return opener + " " + followUp;

                if (followUp.Length > 0)
                    return opener + " İstersen anlatabilirsin.";

                return opener;
            }

            return "Anlıyorum " + hitap + ". İstersen anlatabilirsin.";
        }

        public static string BuildHumbleFallback(string hitap) =>
            ClarificationResponseEngine.BuildUnknownClarification(hitap, null);

        private static string ResolveHitap(string ownerName) =>
            string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();

        private static string Normalize(string message)
        {
            var text = (message ?? string.Empty).Trim();
            if (text.Length == 0)
                return string.Empty;

            try
            {
                return text.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            }
            catch
            {
                return text.ToLowerInvariant();
            }
        }

        private static bool ContainsAny(string text, string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsOnlyTokens(string normalized, string[] tokens)
        {
            var stripped = normalized;
            foreach (var token in tokens.OrderByDescending(t => t.Length))
            {
                var index = stripped.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    stripped = stripped.Remove(index, token.Length);
                    index = stripped.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                }
            }

            stripped = stripped
                .Replace("!", string.Empty)
                .Replace(".", string.Empty)
                .Replace(",", string.Empty)
                .Replace("?", string.Empty)
                .Trim();

            return stripped.Length == 0;
        }
    }
}
