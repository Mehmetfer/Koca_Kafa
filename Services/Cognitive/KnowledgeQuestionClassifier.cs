using System;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class KnowledgeQuestionClassifier
    {
        private static readonly Regex TopicKnowledgePattern = new Regex(
            @"^(.+?)\s+hakkında\s+ne\s+biliyorsun",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static KnowledgeQuestionKind Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return KnowledgeQuestionKind.None;

            if (CreativeTaskEngine.IsCreativeTask(message))
                return KnowledgeQuestionKind.None;

            var lower = message.Trim().ToLowerInvariant();

            if (IsSelfDiagnosticQuestionCore(lower))
                return KnowledgeQuestionKind.SelfQuestion;

            if (IsSelfQuestion(lower))
                return KnowledgeQuestionKind.SelfQuestion;

            if (IsTopicSpecificKnowledgeQueryCore(lower))
                return KnowledgeQuestionKind.ExplanationQuestion;

            if (IsExplanationQuestion(lower))
                return KnowledgeQuestionKind.ExplanationQuestion;

            if (IsKnowledgeInventoryQuestion(lower))
                return KnowledgeQuestionKind.KnowledgeQuestion;

            return KnowledgeQuestionKind.None;
        }

        public static bool IsKnowledgeIntent(string message) =>
            Classify(message) != KnowledgeQuestionKind.None;

        public static bool ShouldDisableEmpathy(string message) =>
            IsKnowledgeIntent(message);

        private static bool IsSelfQuestion(string lower)
        {
            if (ContainsAny(lower,
                "sen kimsin", "kimsin sen", "sen nesin", "sen neysin",
                "koca kafa kimsin", "koca kafa nesin",
                "hangi seviyedesin", "hangi seviyesin", "kaçıncı seviye",
                "seviyen ne", "seviyede misin", "kaç seviye",
                "sen hangi seviyedesin", "sen hangi seviyesin"))
                return true;

            return (ContainsAny(lower, "sen ", "kimsin", "seviye") || lower.StartsWith("kimsin"))
                   && !ContainsAny(lower, "nedir", "ne demek", "açıkla", "acikla");
        }

        private static bool IsExplanationQuestion(string lower)
        {
            if (ContainsAny(lower, "nedir", "ne demek", "ne demektir", "nelerdir"))
                return true;

            if (ContainsAny(lower, "açıkla", "acikla", "anlatır mısın", "anlatir misin", "tanımla", "tanimla"))
                return !IsSelfQuestion(lower);

            return false;
        }

        private static bool IsKnowledgeInventoryQuestion(string lower)
        {
            if (IsTopicSpecificKnowledgeQueryCore(lower))
                return false;

            return ContainsAny(lower,
                "ne biliyorsun",
                "neler bilirsin",
                "neler biliyorsun",
                "bilirsin",
                "biliyor musun",
                "ne biliyorsunuz",
                "ne öğrendin",
                "ne ogrendin",
                "neler biliyorsun",
                "bana dair ne biliyorsun",
                "benim hakkımda ne biliyorsun");
        }

        public static bool IsTopicSpecificKnowledgeQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return IsTopicSpecificKnowledgeQueryCore(message.Trim().ToLowerInvariant());
        }

        public static bool IsSelfDiagnosticQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return IsSelfDiagnosticQuestionCore(message.Trim().ToLowerInvariant());
        }

        private static bool IsTopicSpecificKnowledgeQueryCore(string lower)
        {
            var match = TopicKnowledgePattern.Match(lower);
            if (!match.Success)
                return false;

            var topic = match.Groups[1].Value.Trim();
            if (topic.Length < 2)
                return false;

            return !ContainsWholeWord(topic, "sen", "siz", "benim")
                   && topic.IndexOf("koca kafa", StringComparison.OrdinalIgnoreCase) < 0
                   && topic.IndexOf("bana dair", StringComparison.OrdinalIgnoreCase) < 0
                   && topic.IndexOf("hakkımda", StringComparison.OrdinalIgnoreCase) < 0
                   && topic.IndexOf("hakkinda", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool ContainsWholeWord(string text, params string[] words)
        {
            foreach (var word in words)
            {
                var pattern = @"\b" + Regex.Escape(word) + @"\b";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }

            return false;
        }

        private static bool IsSelfDiagnosticQuestionCore(string lower) =>
            ContainsAny(lower,
                "hata var", "hata varmı", "hata var mi", "bug var",
                "kontrol edebilir", "kontrol et", "programında hata", "programinda hata",
                "çalışıyor mu", "calisiyor mu", "test edebilir", "kendini kontrol");

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
