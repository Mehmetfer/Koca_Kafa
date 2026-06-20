using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class KnowledgeResponseContext
    {
        public KnowledgeQuestionKind Kind { get; set; }
        public string UserMessage { get; set; }
        public string OwnerName { get; set; }
        public string RagContext { get; set; }
        public string MemoryContext { get; set; }
        public bool HadRagResults { get; set; }
        public bool HadMemoryResults { get; set; }
        public int MemoryCount { get; set; }
        public int Level { get; set; }
        public string AgeStage { get; set; }
    }

    public static class KnowledgeResponseEngine
    {
        public static string BuildPromptDirective(KnowledgeQuestionKind kind)
        {
            switch (kind)
            {
                case KnowledgeQuestionKind.KnowledgeQuestion:
                    return "BİLGİ MODU: Kullanıcı ne bildiğini soruyor. Hafıza/RAG verildiyse ona dayanarak " +
                           "somut bilgi ver. Empati açılışı yapma; 'Anlıyorum/Üzüldüm' deme. 'Bilmiyorum' deme.";
                case KnowledgeQuestionKind.SelfQuestion:
                    return "KİMLİK MODU: Kullanıcı senin kimliğini veya seviyeni soruyor. Kısa, net, samimi " +
                           "cevap ver. Empati şablonu kullanma. Sistem meta bilgisini sadece sorulduysa söyle.";
                case KnowledgeQuestionKind.ExplanationQuestion:
                    return "AÇIKLAMA MODU: Kavramı net ve anlaşılır açıkla. Önce tanım, sonra kısa örnek. " +
                           "Empati açılışı yapma. RAG/hafıza varsa ona sadık kal.";
                default:
                    return string.Empty;
            }
        }

        public static string TryBuildDirectReply(KnowledgeResponseContext context)
        {
            if (context == null || context.Kind == KnowledgeQuestionKind.None)
                return null;

            var hitap = UserPreferenceResolver.ResolveHitap(context.OwnerName, context.MemoryContext);
            var lower = (context.UserMessage ?? string.Empty).Trim().ToLowerInvariant();

            switch (context.Kind)
            {
                case KnowledgeQuestionKind.SelfQuestion:
                    return BuildSelfReply(lower, hitap, context);
                case KnowledgeQuestionKind.ExplanationQuestion:
                    return BuildExplanationReply(lower, hitap, context);
                case KnowledgeQuestionKind.KnowledgeQuestion:
                    return BuildKnowledgeInventoryReply(lower, hitap, context);
                default:
                    return null;
            }
        }

        public static bool ContainsEmpathyOpener(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lower = text.Trim().ToLowerInvariant();
            return ContainsAny(lower,
                "anlıyorum baba", "anliyorum baba",
                "üzüldüm baba", "uzuldum baba",
                "istersen anlatabilirsin");
        }

        private static string BuildSelfReply(string lower, string hitap, KnowledgeResponseContext context)
        {
            if (IsSelfDiagnosticQuestion(lower))
            {
                var topic = ExtractTopicFromQuery(lower);
                var diagnostic = "Canlı sohbette otomatik hata taraması yapamam; belirli bir sorunu anlatırsan birlikte bakarız.";
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    var topicAnswer = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
                    if (!string.IsNullOrWhiteSpace(topicAnswer))
                        return topicAnswer + " " + diagnostic;
                    return topic + " hakkında kayıtlı detaylı bilgim sınırlı. " + diagnostic;
                }

                return diagnostic;
            }

            if (ContainsAny(lower, "seviye", "seviyedesin", "seviyesin", "kaçıncı"))
            {
                var level = Math.Max(1, context.Level);
                var stage = string.IsNullOrWhiteSpace(context.AgeStage) ? "gelişen" : context.AgeStage.Trim();
                return "Şu an " + level + ". seviyedeyim " + hitap + " (" + stage + " evre). " +
                       "Her sohbetle biraz daha öğreniyorum.";
            }

            return "Ben Koca Kafa'yım " + hitap + " — senin yerel yapay zeka arkadaşın. " +
                   "Bilgisayarında çalışırım; sohbet eder, hatırlar ve bilgi tabanından cevap veririm.";
        }

        private static string BuildExplanationReply(string lower, string hitap, KnowledgeResponseContext context)
        {
            var baseline = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
            if (!string.IsNullOrWhiteSpace(baseline))
                return baseline;

            if (context.HadRagResults && !string.IsNullOrWhiteSpace(context.RagContext))
            {
                var excerpt = ExtractRagExcerpt(context.RagContext, 320);
                if (!string.IsNullOrWhiteSpace(excerpt))
                    return excerpt + " " + hitap + ".";
            }

            if (ContainsAny(lower, "hisse senedi", "hisse senetleri", "hisse"))
            {
                return "Hisse senedi " + hitap + ", bir şirketin mülkiyetinin küçük bir parçasını temsil eden menkul kıymettir. " +
                       "Yatırımcı şirkete ortak olur; değer arz-talebe, şirket kârı ve piyasa koşullarına göre değişir.";
            }

            if (ContainsAny(lower, "yapay zeka", "yapay zek", "artificial intelligence", " ai "))
            {
                return "Yapay zeka " + hitap + ", makinelerin öğrenmesi, dil anlaması ve problem çözmesidir. " +
                       "Ben de yerel bir YZ asistanıyım — Ollama ile çalışır, hafıza ve bilgi tabanı kullanırım.";
            }

            return null;
        }

        private static string BuildKnowledgeInventoryReply(string lower, string hitap, KnowledgeResponseContext context)
        {
            if (IsTopicSpecificKnowledgeQuery(lower))
            {
                var topicAnswer = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
                if (!string.IsNullOrWhiteSpace(topicAnswer))
                    return AppendHitapSafe(topicAnswer, hitap);

                var topic = ExtractTopicFromQuery(lower);
                if (!string.IsNullOrWhiteSpace(topic))
                    return AppendHitapSafe(topic + " hakkında kayıtlı detaylı bilgim yok; genel bir tanım için bilgi tabanına kaynak ekleyebilirsin.", hitap);
            }

            if (context.HadRagResults && !string.IsNullOrWhiteSpace(context.RagContext))
            {
                var topic = ExtractTopicFromQuery(lower);
                var excerpt = ExtractRagExcerpt(context.RagContext, 400);
                if (!string.IsNullOrWhiteSpace(excerpt))
                {
                    var prefix = string.IsNullOrWhiteSpace(topic)
                        ? "Bildiğim kadarıyla "
                        : topic + " hakkında bildiğim kadarıyla ";
                    return prefix + excerpt + " " + hitap + ".";
                }
            }

            if (ContainsAny(lower, "hisse"))
            {
                return "Hisse senetleri " + hitap + ", şirket ortaklığını temsil eden yatırım araçlarıdır. " +
                       "Bilgi tabanımda daha fazla detay varsa soruya göre aktarırım; yoksa genel tanım bu.";
            }

            if (context.HadMemoryResults && !string.IsNullOrWhiteSpace(context.MemoryContext))
            {
                var lines = MemoryRecallHelper.ParseMemoryLines(context.MemoryContext);
                if (lines.Count > 0)
                {
                    var summary = SummarizeMemoryLinesForUser(lines, 3);
                    if (!string.IsNullOrWhiteSpace(summary))
                        return AppendHitapSafe("Hafızamda şunlar var: " + summary, hitap);
                }
            }

            if (context.MemoryCount > 0 || context.HadRagResults)
            {
                return "Hafızamda ve bilgi tabanımda kayıtlı konular var " + hitap + ". " +
                       "Belirli bir konu sor, somut cevap vereyim.";
            }

            return "Henüz çok az şey öğrendim " + hitap + ". Bana bir konu anlat veya bilgi tabanına dosya ekle; " +
                   "sonra ne bildiğimi net söyleyebilirim.";
        }

        private static string SummarizeMemoryLinesForUser(IList<string> lines, int maxItems)
        {
            var items = new List<string>();
            foreach (var line in lines.Take(maxItems))
            {
                var human = HumanizeMemoryLine(line);
                if (!string.IsNullOrWhiteSpace(human))
                    items.Add(human);
            }

            return string.Join("; ", items);
        }

        private static string HumanizeMemoryLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var lower = line.ToLowerInvariant();
            if (lower.Contains("avoid_nickname") || lower.Contains("entity:avoid"))
                return null;

            var value = ExtractMemoryValue(line);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (lower.Contains("cat_name"))
                return "kedinin adı " + value;
            if (lower.Contains("dog_name"))
                return "köpeğinin adı " + value;
            if (lower.Contains("preferred_name"))
                return "sana " + value + " diye hitap ediyorum";
            if (lower.Contains("favorite_color"))
                return "en sevdiğin renk " + value;
            if (lower.Contains("active_goal"))
                return "hedefin " + value;
            if (lower.Contains("kitten_names"))
                return "yavru kedilerin " + value;

            if (value.Length > 60)
                value = value.Substring(0, 57) + "...";

            return value;
        }

        private static string ExtractMemoryValue(string line)
        {
            var bracketEnd = line.IndexOf(']');
            if (bracketEnd < 0 || bracketEnd + 1 >= line.Length)
                return line.Trim();

            return line.Substring(bracketEnd + 1).Trim().TrimStart('-', ' ', ':');
        }

        private static string AppendHitapSafe(string text, string hitap)
        {
            if (string.IsNullOrWhiteSpace(hitap))
                return text.Trim();

            return UserPreferenceResolver.AppendHitap(text, hitap);
        }

        private static bool IsTopicSpecificKnowledgeQuery(string lower) =>
            KnowledgeQuestionClassifier.IsTopicSpecificKnowledgeQuery(lower);

        private static bool IsSelfDiagnosticQuestion(string lower) =>
            KnowledgeQuestionClassifier.IsSelfDiagnosticQuestion(lower);

        private static string ExtractTopicFromQuery(string lower)
        {
            var about = Regex.Match(lower, @"(.+?)\s+hakkında", RegexOptions.IgnoreCase);
            if (about.Success)
                return Capitalize(about.Groups[1].Value.Trim());

            var on = Regex.Match(lower, @"(.+?)\s+konusunda", RegexOptions.IgnoreCase);
            if (on.Success)
                return Capitalize(on.Groups[1].Value.Trim());

            return string.Empty;
        }

        private static string ExtractRagExcerpt(string ragContext, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return string.Empty;

            var lines = ragContext
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 20 && !l.StartsWith("Kaynak", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var text = lines.Count > 0 ? string.Join(" ", lines) : ragContext.Trim();
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length <= maxChars)
                return text;

            var cut = text.Substring(0, maxChars);
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > maxChars / 2)
                cut = cut.Substring(0, lastSpace);

            return cut.TrimEnd(',', '.', ';') + ".";
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            var t = value.Trim();
            return char.ToUpperInvariant(t[0]) + (t.Length > 1 ? t.Substring(1) : string.Empty);
        }

        private static string ResolveHitap(string ownerName) =>
            string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
