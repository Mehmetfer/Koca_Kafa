using System;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class MessageCategoryClassifier
    {
        private static readonly string[] MemorySignals =
        {
            "hatırlıyor musun", "hatirliyor musun", "hatırla", "hatirla",
            "beni hatırla", "daha önce", "daha once", "adım ne", "adim ne",
            "ismim ne", "neydi", "profilimde", "kaydettiğin", "kaydettigin",
            "söylediğim", "soyledigim", "ne biliyorsun benden", "benim hakkımda"
        };

        private static readonly string[] GoalSignals =
        {
            "hedef", "plan yap", "öğrenmek istiyorum", "ogrenmek istiyorum",
            "vermek istiyorum", "başarmak istiyorum", "basarmak istiyorum", "yol haritası", "yol haritasi",
            "roadmap", "strateji", "hedefim", "amaç", "amac"
        };

        private static readonly string[] ProblemSignals =
        {
            "sorun", "problem", "çalışmıyor", "calismiyor", "hata", "bozuk",
            "yapamıyorum", "yapamiyorum", "çözemiyorum", "cozemiyorum",
            "takıldım", "takildim", "bıktım", "biktım", "biktim"
        };

        private static readonly string[] QuestionSignals =
        {
            "nedir", "kimdir", "nasıl", "nasil", "neden", "niçin", "nicin",
            "hangi", "kaç", "kac", "nerede", "ne zaman", "misin", "mısın", "musun"
        };

        public static MessageCategory Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return MessageCategory.CasualChat;

            var lower = message.Trim().ToLowerInvariant();

            if (ExplicitEmotionDetector.IsExplicitEmotionalStatement(lower))
                return MessageCategory.EmotionalStatement;

            if (ImplicitEmotionDetector.IsImplicitEmotionalStatement(message))
                return MessageCategory.ImplicitEmotionalStatement;

            if (ContainsAny(lower, MemorySignals))
                return MessageCategory.MemoryReference;

            if (GreetingEngine.IsGreetingMessage(message))
                return MessageCategory.Greeting;

            if (GreetingEngine.IsSmallTalkMessage(message))
                return MessageCategory.CasualChat;

            if (ContainsAny(lower, GoalSignals))
                return MessageCategory.Goal;

            if (ContainsAny(lower, ProblemSignals))
                return MessageCategory.Problem;

            if (message.Contains("?") || ContainsAny(lower, QuestionSignals))
                return MessageCategory.Question;

            return MessageCategory.CasualChat;
        }

        public static bool IsEmotionalStatement(string message) =>
            ExplicitEmotionDetector.IsExplicitEmotionalStatement(message);

        public static bool IsImplicitEmotionalStatement(string message) =>
            ImplicitEmotionDetector.IsImplicitEmotionalStatement(message);

        public static bool ShouldDisableHumbleFallback(string message) =>
            IsEmotionalStatement(message) || ImplicitEmotionDetector.ShouldTriggerEmpathy(message);

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
