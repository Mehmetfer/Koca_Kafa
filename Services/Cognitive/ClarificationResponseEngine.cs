using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class ClarificationResponseEngine
    {
        public static string BuildClarification(string userMessage, string ownerName, string memoryContext)
        {
            var message = (userMessage ?? string.Empty).Trim();
            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);

            if (IsAmbiguousImperative(message))
                return AppendHitap("Ne vermemi istiyorsun?", hitap);

            if (GreetingEngine.IsSmallTalkMessage(message))
                return GreetingEngine.BuildSmallTalkReply(hitap);

            if (GreetingEngine.IsGreetingMessage(message))
                return GreetingEngine.BuildGreetingReply(message, hitap);

            if (MemoryRecallHelper.IsMemoryQuestion(message))
                return AppendHitap("Tam olarak neyi hatırlamamı istiyorsun?", hitap);

            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(message))
                return AppendHitap("Hangi konuda bilgi istediğini biraz açar mısın?", hitap);

            if (CreativeTaskEngine.IsCreativeTask(message) || LooksLikeCommand(message))
                return AppendHitap("Tam olarak ne yapmamı istersin?", hitap);

            if (MessageCategoryClassifier.IsEmotionalStatement(message) ||
                MessageCategoryClassifier.IsImplicitEmotionalStatement(message))
            {
                var empathy = EmpathyResponseEngine.TryBuildDirectReply(
                    message, ownerName, null, memoryContext);
                if (!string.IsNullOrWhiteSpace(empathy))
                    return empathy;
            }

            if (IsEnglish(message))
                return "I didn't quite catch that. Could you rephrase or add a bit more detail?";

            return AppendHitap("Tam olarak ne demek istediğini anlayamadım. Biraz daha açar mısın?", hitap);
        }

        public static string BuildUnknownClarification(string ownerName, string memoryContext)
        {
            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);
            return AppendHitap("Ne hakkında konuşmak istediğini biraz açar mısın?", hitap);
        }

        private static bool IsAmbiguousImperative(string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();
            if (text.Length == 0 || text.Length > 10 || text.Contains(" "))
                return false;

            return text == "ver" || text == "gönder" || text == "gonder" ||
                   text == "yap" || text == "getir" || text == "söyle" || text == "soyle";
        }

        private static bool LooksLikeCommand(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return lower.Contains("yap") || lower.Contains("oluştur") || lower.Contains("olustur") ||
                   lower.Contains("hazırla") || lower.Contains("hazirla") || lower.Contains("yaz") ||
                   lower.Contains("çevir") || lower.Contains("cevir") || lower.Contains("please");
        }

        private static string AppendHitap(string text, string hitap)
        {
            if (string.IsNullOrWhiteSpace(hitap))
                return text;

            return text.TrimEnd('?', '.', '!') + " " + hitap + "?";
        }

        private static bool IsEnglish(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return lower.Contains("what") || lower.Contains("how") || lower.Contains("please") ||
                   lower.Contains("hello") || lower.Contains("help me");
        }
    }
}
