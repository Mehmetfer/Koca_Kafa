using System;
using System.Globalization;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class CoreChatIntentClassifier
    {
        public static CoreChatIntentKind Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return CoreChatIntentKind.CasualChat;

            var lower = message.Trim().ToLowerInvariant();

            if (ContainsAny(lower, "nasılsın", "nasilsin", "naber", "ne haber", "n'aber", "nasıl gidiyor", "nasil gidiyor"))
                return CoreChatIntentKind.CasualChat;

            if (IsGreeting(lower))
                return CoreChatIntentKind.Greeting;

            if (IsRequest(lower))
                return CoreChatIntentKind.Request;

            if (IsExplanationRequest(lower))
                return CoreChatIntentKind.Explanation;

            if (IsQuestion(lower))
                return CoreChatIntentKind.Question;

            return CoreChatIntentKind.CasualChat;
        }

        private static bool IsGreeting(string lower)
        {
            if (ContainsAny(lower, "nasılsın", "nasilsin", "naber", "ne haber"))
                return false;

            return ContainsAny(lower,
                "selam", "merhaba", "günaydın", "gunaydin", "iyi günler", "iyi gunler",
                "iyi akşamlar", "iyi aksamlar", "hey", "sa", "hello", "hi") &&
                   !lower.Contains("?");
        }

        private static bool IsRequest(string lower) =>
            ContainsAny(lower,
                "yapabilir misin", "yapar mısın", "yapar misin", "oluştur", "olustur",
                "hazırla", "hazirla", "yazabilir misin", "çevir", "cevir",
                "please create", "please write", "help me", "can you write", "can you create");

        private static bool IsExplanationRequest(string lower) =>
            ContainsAny(lower,
                "açıkla", "acikla", "explain", "anlatır mısın", "anlatir misin",
                "nedir", "ne demek", "nelerdir", "how does", "what is", "what are") ||
            (lower.Contains("?") && ContainsAny(lower, "nedir", "ne demek", "nasıl çalış", "nasil calis"));

        private static bool IsQuestion(string lower) =>
            lower.Contains("?") ||
            ContainsAny(lower,
                "kaç", "kac", "kim", "nerede", "ne zaman", "hangi", "mi ", "mı ",
                "how many", "how much", "who ", "where ", "when ");

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
