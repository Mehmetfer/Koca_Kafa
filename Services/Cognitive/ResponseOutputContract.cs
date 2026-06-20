using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Core;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class ResponseOutputContract
    {
        private static readonly string[] ForbiddenFooterTokens =
        {
            "teşekkürler", "tesekkurler", "teşekkür ederim", "tesekkur ederim",
            "merhaba", "selam", "günaydın", "gunaydin", "iyi günler", "iyi gunler",
            "hoş geldin", "hos geldin", "görüşürüz", "gorusuruz"
        };

        private static readonly string[] MetaPhrases =
        {
            "hafızama kaydettim", "hafizama kaydettim", "not ettim", "kaydettim",
            "bildiğin kalıcı hafızalar", "bilgin kalici hafizalar",
            "bu bilgileri doğal şekilde", "bu bilgileri dogal sekilde",
            "size nasıl yardımcı", "size nasil yardimci",
            "başka bir konuda", "baska bir konuda"
        };

        public static string Enforce(string reply, ResponseQualityContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return reply ?? string.Empty;

            if (IsMemoryRecallOutput(context))
            {
                var strict = BuildStrictMemoryAnswer(context);
                if (!string.IsNullOrWhiteSpace(strict))
                    return strict;
            }

            var text = ReplySanitizer.Sanitize(reply.Trim());
            text = TextDeduplicator.DeduplicateSentences(text);
            text = StripMetaPhrases(text);
            text = StripForbiddenFooters(text);
            text = StripGreetingNoise(text, context);
            text = EnforceSingleIntentOutput(text, context);
            text = CollapseWhitespace(text);

            if (!string.IsNullOrWhiteSpace(context?.UserMessage))
                text = EchoResponseGuard.SanitizeReply(context.UserMessage, text);

            if (!PassesFinalCheck(text, context))
            {
                var redo = BuildRedo(context);
                if (!string.IsNullOrWhiteSpace(redo))
                    return redo;
            }

            return text.Trim();
        }

        public static bool PassesFinalCheck(string reply, ResponseQualityContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            if (ReplySanitizer.ContainsInternalLeakage(reply))
                return false;

            if (EndsWithForbiddenToken(reply))
                return false;

            if (IsMemoryRecallOutput(context) && ContainsMemoryLeak(reply))
                return false;

            if (IsMemoryRecallOutput(context) && CountSentences(reply) > 2)
                return false;

            if (HasDuplicatePhrases(reply))
                return false;

            if (EchoResponseGuard.IsEchoResponse(context?.UserMessage, reply))
                return false;

            if (EchoResponseGuard.ContainsForbiddenFallback(reply))
                return false;

            return true;
        }

        private static string BuildStrictMemoryAnswer(ResponseQualityContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.MemoryContext))
                return null;

            var recall = MemoryRecallHelper.TryBuildDirectRecallReply(
                context.UserMessage,
                context.MemoryContext,
                context.OwnerName);

            if (string.IsNullOrWhiteSpace(recall))
                return null;

            return CleanMemoryRecallAnswer(recall, context);
        }

        private static string CleanMemoryRecallAnswer(string recall, ResponseQualityContext context)
        {
            var text = (recall ?? string.Empty).Trim();
            text = StripForbiddenFooters(text);
            text = StripMetaPhrases(text);

            if (MemoryRecallHelper.IsKittenNameRecall(context.UserMessage))
            {
                var names = MemoryRecallHelper.ExtractKittenNamesFromReply(text);
                if (names.Count > 0)
                    return MemoryRecallHelper.FormatKittenNameList(names);
            }

            if (MemoryRecallHelper.IsCatNameRecall(context.UserMessage))
            {
                var cat = MemoryRecallHelper.ExtractSingleTokenAnswer(text);
                if (!string.IsNullOrWhiteSpace(cat))
                    return cat + ".";
            }

            if (MemoryRecallHelper.IsGoalRecall(context.UserMessage))
            {
                text = Regex.Replace(text, @"\s+baba\.?$", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (!text.EndsWith(".", StringComparison.Ordinal))
                    text += ".";
            }

            return text.Trim();
        }

        private static string BuildRedo(ResponseQualityContext context)
        {
            if (IsMemoryRecallOutput(context))
                return BuildStrictMemoryAnswer(context);

            if (GreetingEngine.IsSocialExchange(context?.UserMessage, context?.Intent?.Intent ?? MessageIntent.Unknown))
                return GreetingEngine.BuildIntentAwareFallback(context);

            if (MessageCategoryClassifier.IsEmotionalStatement(context?.UserMessage) ||
                MessageCategoryClassifier.IsImplicitEmotionalStatement(context?.UserMessage))
            {
                var empathy = EmpathyResponseEngine.TryBuildDirectReply(
                    context.UserMessage,
                    context.OwnerName,
                    context.Empathy,
                    context.MemoryContext);
                if (!string.IsNullOrWhiteSpace(empathy))
                    return empathy;
            }

            return GreetingEngine.BuildIntentAwareFallback(context);
        }

        private static bool IsMemoryRecallOutput(ResponseQualityContext context)
        {
            if (context == null)
                return false;

            if (context.KnowledgeKind == KnowledgeQuestionKind.ExplanationQuestion ||
                KnowledgeQuestionClassifier.IsTopicSpecificKnowledgeQuery(context.UserMessage))
                return false;

            return context.Plan?.RequiredMemory == true ||
                   MemoryRecallHelper.IsRecallQuery(context.UserMessage) ||
                   context.MessageCategory == MessageCategory.MemoryReference;
        }

        private static string EnforceSingleIntentOutput(string text, ResponseQualityContext context)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (IsMemoryRecallOutput(context))
            {
                var sentences = SplitSentences(text).Take(2).ToList();
                return string.Join(" ", sentences).Trim();
            }

            if (context?.MessageCategory == MessageCategory.Greeting ||
                GreetingEngine.IsGreetingMessage(context?.UserMessage))
                return SplitSentences(text).FirstOrDefault() ?? text;

            if (context?.MessageCategory == MessageCategory.EmotionalStatement ||
                context?.Empathy?.RequiresEmpathyFirst == true)
            {
                var sentences = SplitSentences(text);
                if (sentences.Count <= 2)
                    return text;
                return string.Join(" ", sentences.Take(2)).Trim();
            }

            return text;
        }

        private static string StripForbiddenFooters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text.Trim();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var token in ForbiddenFooterTokens)
                {
                    var pattern = @"[\s,;]*\b" + Regex.Escape(token) + @"\b[\s,;.!?]*$";
                    var updated = Regex.Replace(result, pattern, string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (!string.Equals(updated, result, StringComparison.Ordinal))
                    {
                        result = updated;
                        changed = true;
                    }
                }
            }

            result = Regex.Replace(result, @"\s+(ve|ile)\s+(Teşekkürler|Merhaba|Selam)\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return result;
        }

        private static string StripGreetingNoise(string text, ResponseQualityContext context)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var isGreeting = context?.MessageCategory == MessageCategory.Greeting ||
                               GreetingEngine.IsGreetingMessage(context?.UserMessage);

            if (isGreeting)
                return text;

            var result = text.Trim();
            result = Regex.Replace(result, @"^(Merhaba|Selam|Günaydın|Gunaydin|Hey)[!,.\s]+", string.Empty, RegexOptions.IgnoreCase).Trim();
            return result;
        }

        private static string StripMetaPhrases(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;
            foreach (var phrase in MetaPhrases)
            {
                if (string.Equals(phrase, "not ettim", StringComparison.OrdinalIgnoreCase) &&
                    Regex.IsMatch(result, @"hedefini\s+not\s+ettim", RegexOptions.IgnoreCase))
                    continue;

                var index = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    result = result.Remove(index, phrase.Length);
                    index = result.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                }
            }

            return CollapseWhitespace(result);
        }

        private static bool EndsWithForbiddenToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lower = text.Trim().ToLowerInvariant();
            return ForbiddenFooterTokens.Any(token =>
                lower.EndsWith(token, StringComparison.Ordinal) ||
                lower.EndsWith(token + ".", StringComparison.Ordinal) ||
                lower.EndsWith(token + "!", StringComparison.Ordinal));
        }

        private static bool ContainsMemoryLeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var lower = text.ToLowerInvariant();
            return lower.Contains("[entity:") ||
                   lower.Contains("bildiğin kalıcı") ||
                   lower.Contains("bilgin kalici");
        }

        private static bool HasDuplicatePhrases(string text)
        {
            var sentences = SplitSentences(text);
            var normalized = sentences
                .Select(NormalizeForComparison)
                .Where(s => s.Length > 0)
                .ToList();

            return normalized.Count != normalized.Distinct(StringComparer.Ordinal).Count();
        }

        private static int CountSentences(string text) => SplitSentences(text).Count;

        private static IList<string> SplitSentences(string text) =>
            Regex.Split(text ?? string.Empty, @"(?<=[.!?…])\s+")
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

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

            return Regex.Replace(text.Trim(), @"\s+", " ");
        }
    }
}
