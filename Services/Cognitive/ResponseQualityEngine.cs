using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Core;
using Koca_Kafa.Core.Persona;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class ResponseQualityEngine : IResponseQualityEngine
    {
        private const double LowIntentConfidenceThreshold = 0.45;
        private const double LowRagConfidenceThreshold = 0.55;

        private static readonly string[] AssistantCliches =
        {
            "size nasıl yardımcı olabilirim",
            "size nasil yardimci olabilirim",
            "bir yapay zeka olarak",
            "yapay zeka olarak",
            "ben bir dil modeliyim",
            "dil modeli olarak",
            "bir ai olarak",
            "as an ai",
            "language model",
            "başka sorunuz varsa",
            "baska sorunuz varsa",
            "yardım etmekten memnuniyet duyarım",
            "yardim etmekten memnuniyet duyarim",
            "herhangi bir sorunuz olursa",
            "herhangi bir sorunuz",
            "lütfen bana bildirin",
            "lutfen bana bildirin",
            "başka bir konuda yardımcı",
            "baska bir konuda yardimci"
        };

        private static readonly string[] BrokenPhrases =
        {
            "konuşuccu",
            "konusuccu",
            "eğinsonu",
            "eginsonu",
            "sorularınızı açabiliyor olurum",
            "sorularinizi acabiliyor olurum",
            "açabiliyor olurum",
            "acabiliyor olurum",
            "konuşuccu olarak",
            "konusuccu olarak"
        };

        public string Polish(string reply, ResponseQualityContext context)
        {
            if (TryBuildMemoryRecallOverride(context, out var recallOverride))
                return recallOverride;

            if (string.IsNullOrWhiteSpace(reply))
                return GreetingEngine.BuildIntentAwareFallback(context);

            var text = ReplySanitizer.Sanitize(reply.Trim());
            text = TextDeduplicator.DeduplicateSentences(text);
            text = RemoveAssistantCliches(text);
            text = RemovePersonaForbidden(text);
            text = ApplyCharacterConsistency(text, context);

            var sentences = SplitSentences(text)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            sentences = sentences
                .Where(s => !IsBrokenSentence(s))
                .Where(s => !IsIncompleteFragment(s))
                .ToList();

            sentences = DeduplicateSentences(sentences);
            text = string.Join(" ", sentences).Trim();
            text = TextDeduplicator.DeduplicateSentences(text);

            text = LimitParagraphs(text, context);
            text = CollapseWhitespace(text);

            if (!string.IsNullOrWhiteSpace(context?.MemoryContext))
                text = UserPreferenceResolver.StripForbiddenNickname(text, context.MemoryContext);

            if (GreetingEngine.IsHumbleFallbackText(text) && ShouldNeverUseHumbleFallback(context))
                return GreetingEngine.BuildIntentAwareFallback(context);

            if (GreetingEngine.IsHumbleFallbackText(text) && context.HadMemoryResults)
            {
                var recall = MemoryRecallHelper.TryBuildDirectRecallReply(
                    context.UserMessage,
                    context.MemoryContext,
                    context.OwnerName);
                if (!string.IsNullOrWhiteSpace(recall))
                    return recall;
            }

            if (ShouldUseHumbleFallback(context, text, sentences.Count))
                return ResolveFallback(context);

            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(context?.UserMessage) &&
                KnowledgeResponseEngine.ContainsEmpathyOpener(text))
                return GreetingEngine.BuildIntentAwareFallback(context);

            if ((context?.CreativeTaskKind != CreativeTaskKind.None ||
                 CreativeTaskEngine.IsCreativeTask(context?.UserMessage)) &&
                (CreativeTaskEngine.ContainsEmpathyOpener(text) ||
                 CreativeTaskEngine.ContainsMixedScript(text)))
                return GreetingEngine.BuildIntentAwareFallback(context);

            if ((context?.MessageCategory == MessageCategory.EmotionalStatement ||
                 MessageCategoryClassifier.IsEmotionalStatement(context?.UserMessage)) &&
                EmpathyResponseEngine.ContainsForbiddenFallback(text))
            {
                var empathyReply = EmpathyResponseEngine.TryBuildDirectReply(
                    context.UserMessage,
                    context.OwnerName,
                    context.Empathy,
                    context.MemoryContext);
                if (!string.IsNullOrWhiteSpace(empathyReply))
                    return empathyReply;
            }

            if ((context?.MessageCategory == MessageCategory.ImplicitEmotionalStatement ||
                 MessageCategoryClassifier.IsImplicitEmotionalStatement(context?.UserMessage)) &&
                (context?.ImplicitEmotionConfidence > ImplicitEmotionDetector.EmpathyThreshold ||
                 ImplicitEmotionDetector.ShouldTriggerEmpathy(context?.UserMessage)) &&
                (EmpathyResponseEngine.ContainsForbiddenFallback(text) ||
                 EmpathyResponseEngine.ContainsDiagnosisLanguage(text)))
            {
                var implicitReply = EmpathyResponseEngine.TryBuildImplicitReply(
                    context.UserMessage,
                    context.OwnerName,
                    context.MemoryContext);
                if (!string.IsNullOrWhiteSpace(implicitReply))
                    return implicitReply;
            }

            return string.IsNullOrWhiteSpace(text)
                ? GreetingEngine.BuildIntentAwareFallback(context)
                : text;
        }

        private static bool ShouldNeverUseHumbleFallback(ResponseQualityContext context)
        {
            if (context == null)
                return false;

            var intent = context.Intent?.Intent ?? MessageIntent.Unknown;
            if (intent == MessageIntent.Greeting || intent == MessageIntent.Emotion || intent == MessageIntent.Joke)
                return true;

            if (context.Empathy?.RequiresEmpathyFirst == true)
                return true;

            if (context?.Plan?.RequiredMemory == true && context.HadMemoryResults)
                return true;

            if (MemoryRecallHelper.IsRecallQuery(context?.UserMessage) && context.HadMemoryResults)
                return true;

            if (context?.KnowledgeKind != KnowledgeQuestionKind.None ||
                KnowledgeQuestionClassifier.IsKnowledgeIntent(context?.UserMessage))
                return true;

            if (context?.IsDateTimeQuestion == true ||
                DateTimeAwarenessEngine.IsDateTimeQuestion(context?.UserMessage))
                return true;

            if (context?.CreativeTaskKind != CreativeTaskKind.None ||
                CreativeTaskEngine.IsCreativeTask(context?.UserMessage))
                return true;

            if (context?.MessageCategory == MessageCategory.EmotionalStatement ||
                MessageCategoryClassifier.IsEmotionalStatement(context?.UserMessage))
                return true;

            if ((context?.MessageCategory == MessageCategory.ImplicitEmotionalStatement ||
                 MessageCategoryClassifier.IsImplicitEmotionalStatement(context?.UserMessage)) &&
                (context?.ImplicitEmotionConfidence ?? 0) > ImplicitEmotionDetector.EmpathyThreshold)
                return true;

            return GreetingEngine.IsSocialExchange(context.UserMessage, intent);
        }

        private static string ResolveFallback(ResponseQualityContext context)
        {
            if (ShouldNeverUseHumbleFallback(context))
                return GreetingEngine.BuildIntentAwareFallback(context);

            return GreetingEngine.BuildHumbleFallback(GetHitap(context));
        }

        private static string RemovePersonaForbidden(string text)
        {
            var result = text ?? string.Empty;
            foreach (var phrase in PersonaDefaults.ForbiddenPhrases)
            {
                var index = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    result = result.Remove(index, phrase.Length);
                    index = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                }
            }

            return CollapseWhitespace(result);
        }

        private static string RemoveAssistantCliches(string text)
        {
            var result = text ?? string.Empty;
            foreach (var cliche in AssistantCliches)
            {
                var index = result.IndexOf(cliche, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    result = result.Remove(index, cliche.Length);
                    index = result.IndexOf(cliche, StringComparison.OrdinalIgnoreCase);
                }
            }

            return CollapseWhitespace(result);
        }

        private static string ApplyCharacterConsistency(string text, ResponseQualityContext context)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var hitap = GetHitap(context);
            var result = text;

            result = Regex.Replace(result, @"\bSize\b", "Sana", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bSiz\b", "Sen", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bsize\b", "sana");
            result = Regex.Replace(result, @"\bsiz\b", "sen");
            result = Regex.Replace(result, @"\bKullanıcı\b", hitap, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bkullanıcı\b", hitap, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bKullanici\b", hitap, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bkullanici\b", hitap, RegexOptions.IgnoreCase);

            return result;
        }

        private static List<string> DeduplicateSentences(IList<string> sentences)
        {
            var kept = new List<string>();
            var normalized = new List<string>();

            foreach (var sentence in sentences)
            {
                var norm = NormalizeForComparison(sentence);
                if (norm.Length == 0)
                    continue;

                if (normalized.Any(existing => IsDuplicate(existing, norm)))
                    continue;

                kept.Add(sentence);
                normalized.Add(norm);
            }

            return kept;
        }

        private static bool IsDuplicate(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            if (left.Length >= 6 && right.Length >= 6)
            {
                if (left.Contains(right) || right.Contains(left))
                    return true;

                var leftWords = left.Split(' ').Where(w => w.Length > 2).ToList();
                var rightWords = right.Split(' ').Where(w => w.Length > 2).ToList();
                if (leftWords.Count > 0 && rightWords.Count > 0)
                {
                    var overlap = leftWords.Intersect(rightWords, StringComparer.OrdinalIgnoreCase).Count();
                    var union = leftWords.Union(rightWords, StringComparer.OrdinalIgnoreCase).Count();
                    if (union > 0 && (double)overlap / union >= 0.75)
                        return true;
                }
            }

            return false;
        }

        private static bool IsBrokenSentence(string sentence)
        {
            var lower = (sentence ?? string.Empty).ToLowerInvariant();
            if (lower.Length == 0)
                return true;

            if (BrokenPhrases.Any(p => lower.Contains(p)))
                return true;

            if (Regex.IsMatch(lower, @"[bcdfghjklmnpqrstvwxyzçğıöşü]{6,}", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(lower, @"\b\w{25,}\b"))
                return true;

            return false;
        }

        private static bool IsIncompleteFragment(string sentence)
        {
            var trimmed = (sentence ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return true;

            if (trimmed == "?")
                return true;

            if (Regex.IsMatch(trimmed, @",\s*baba\.\s*$", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(trimmed, @"\.\.\.\s*(ve)?\s*$", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(trimmed, @"\sve\s*$", RegexOptions.IgnoreCase))
                return true;

            if (trimmed.Length <= 3 && trimmed.EndsWith("?", StringComparison.Ordinal))
                return true;

            return false;
        }

        private static string LimitParagraphs(string text, ResponseQualityContext context)
        {
            var paragraphs = text
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (paragraphs.Count == 0)
                return text;

            var max = GetMaxParagraphs(context);
            if (paragraphs.Count <= max)
                return string.Join("\n\n", paragraphs);

            return string.Join("\n\n", paragraphs.Take(max));
        }

        private static int GetMaxParagraphs(ResponseQualityContext context)
        {
            var intent = context?.Intent?.Intent ?? MessageIntent.Unknown;

            if (context?.Empathy?.RequiresEmpathyFirst == true ||
                intent == MessageIntent.Emotion)
                return 2;

            if (intent == MessageIntent.Greeting || intent == MessageIntent.Joke)
                return 1;

            if (intent == MessageIntent.Question)
                return 3;

            return 4;
        }

        private static bool ShouldUseHumbleFallback(
            ResponseQualityContext context,
            string text,
            int sentenceCount)
        {
            if (string.IsNullOrWhiteSpace(text) || sentenceCount == 0)
                return true;

            if (ReplySanitizer.ContainsInternalLeakage(text))
                return true;

            var intent = context?.Intent?.Intent ?? MessageIntent.Unknown;
            if (intent != MessageIntent.Question && intent != MessageIntent.Teaching && intent != MessageIntent.Task)
                return false;

            if (context?.HadRagResults == true &&
                context.RagConfidence.GetValueOrDefault(1.0) >= LowRagConfidenceThreshold)
                return false;

            var intentConfidence = context?.Intent?.Confidence ?? 1.0;
            if (intent == MessageIntent.Unknown && intentConfidence < LowIntentConfidenceThreshold)
                return true;

            if (context?.HadRagResults != true &&
                context?.Plan?.RequiredRag == true &&
                intentConfidence < 0.55)
                return true;

            if (sentenceCount == 0)
                return true;

            return false;
        }

        private static string GetHitap(ResponseQualityContext context)
        {
            return UserPreferenceResolver.ResolveHitap(context?.OwnerName, context?.MemoryContext);
        }

        private static List<string> SplitSentences(string text)
        {
            return Regex.Split(text ?? string.Empty, @"(?<=[.!?…])\s+")
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string NormalizeForComparison(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                return string.Empty;

            var lower = sentence.ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^\p{L}\p{N}\s]", string.Empty);
            lower = Regex.Replace(lower, @"\s+", " ").Trim();
            return lower;
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => Regex.Replace(l.Trim(), @"\s+", " "))
                .Where(l => l.Length > 0);

            return string.Join("\n\n", lines).Trim();
        }

        private static bool TryBuildMemoryRecallOverride(ResponseQualityContext context, out string reply)
        {
            reply = null;
            if (context == null || string.IsNullOrWhiteSpace(context.MemoryContext))
                return false;

            if (!MemoryRecallHelper.IsRecallQuery(context.UserMessage) &&
                context.Plan?.RequiredMemory != true)
                return false;

            reply = MemoryRecallHelper.TryBuildDirectRecallReply(
                context.UserMessage,
                context.MemoryContext,
                context.OwnerName);

            return !string.IsNullOrWhiteSpace(reply);
        }
    }
}
