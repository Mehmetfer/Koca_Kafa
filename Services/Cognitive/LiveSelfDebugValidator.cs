using System;
using System.Linq;
using Koca_Kafa.Core;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public static class LiveSelfDebugValidator
    {
        public const string EchoDetected = "ECHO_DETECTED";
        public const string FallbackDetected = "FALLBACK_DETECTED";
        public const string IntentMismatch = "INTENT_MISMATCH";
        public const string HallucinationRisk = "HALLUCINATION_RISK";
        public const string LanguageMismatch = "LANGUAGE_MISMATCH";

        public static bool PassesAllChecks(string reply, SelfDebugContext context, out SelfDebugIssue primaryIssue)
        {
            primaryIssue = null;
            if (DetectEcho(reply, context, out primaryIssue))
                return false;
            if (DetectFallback(reply, out primaryIssue))
                return false;
            if (DetectIntentMismatch(reply, context, out primaryIssue))
                return false;
            if (DetectHallucinationRisk(reply, context, out primaryIssue))
                return false;
            if (DetectLanguageMismatch(reply, context, out primaryIssue))
                return false;

            return true;
        }

        public static bool DetectEcho(string reply, SelfDebugContext context, out SelfDebugIssue issue)
        {
            issue = null;
            if (EchoResponseGuard.IsEchoResponse(context?.UserMessage, reply))
            {
                issue = Issue(SelfDebugIssueType.EchoDetected, EchoDetected, "EchoResponseGuard",
                    "Response echoes user input.");
                return true;
            }

            return false;
        }

        public static bool DetectFallback(string reply, out SelfDebugIssue issue)
        {
            issue = null;
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var validation = DecisionOutputValidator.Validate(string.Empty, reply, null);
            if (validation != null && !validation.Passed &&
                (validation.Code == "forbidden_fallback" || validation.Code == "generic_ack"))
            {
                issue = Issue(SelfDebugIssueType.FallbackDetected, FallbackDetected, "EchoResponseGuard",
                    "Generic fallback phrase detected.");
                return true;
            }

            var lower = reply.Trim().ToLowerInvariant();
            if (lower.StartsWith("anladım") || lower.StartsWith("anladim") ||
                lower.Contains("bunu bilmiyorum"))
            {
                issue = Issue(SelfDebugIssueType.FallbackDetected, FallbackDetected, "EchoResponseGuard",
                    "Forbidden filler response detected.");
                return true;
            }

            return false;
        }

        public static bool DetectIntentMismatch(string reply, SelfDebugContext context, out SelfDebugIssue issue)
        {
            issue = null;
            if (string.IsNullOrWhiteSpace(reply) || context == null)
                return false;

            var action = context.DecisionAction;
            if (!context.DecisionActionSet && context.OutputContext?.Intent != null)
                action = MapIntent(context.OutputContext.Intent);

            switch (action)
            {
                case DecisionBrainAction.Clarification:
                    if (!LooksLikeClarification(reply))
                    {
                        issue = Issue(SelfDebugIssueType.IntentMismatch, IntentMismatch, "DecisionBrain",
                            "Clarification path must ask a clarifying question.");
                        return true;
                    }
                    break;

                case DecisionBrainAction.MemoryResponse:
                    if (!HasMemoryGrounding(reply, context))
                    {
                        issue = Issue(SelfDebugIssueType.IntentMismatch, IntentMismatch, "MemoryRecallHelper",
                            "Memory response lacks memory-derived grounding.");
                        return true;
                    }
                    break;

                case DecisionBrainAction.WebFactResponse:
                case DecisionBrainAction.ReasoningResponse:
                    if (GreetingEngine.IsGreetingMessage(reply) && !GreetingEngine.IsGreetingMessage(context.UserMessage))
                    {
                        issue = Issue(SelfDebugIssueType.IntentMismatch, IntentMismatch, "DecisionBrain",
                            "Factual path produced conversational greeting.");
                        return true;
                    }
                    break;

                case DecisionBrainAction.ChatResponse:
                    if (reply.IndexOf("[Entity:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        issue = Issue(SelfDebugIssueType.IntentMismatch, IntentMismatch, "DecisionBrain",
                            "Chat path leaked internal memory structure.");
                        return true;
                    }
                    break;
            }

            return false;
        }

        public static bool DetectHallucinationRisk(string reply, SelfDebugContext context, out SelfDebugIssue issue)
        {
            issue = null;
            if (string.IsNullOrWhiteSpace(reply) || context == null)
                return false;

            var isFactual = context.DecisionAction == DecisionBrainAction.WebFactResponse ||
                            context.DecisionAction == DecisionBrainAction.ReasoningResponse ||
                            KnowledgeQuestionClassifier.IsKnowledgeIntent(context.UserMessage);

            if (!isFactual)
                return false;

            var hasGrounding = context.HadWebResults ||
                               !string.IsNullOrWhiteSpace(context.WebContext) ||
                               !string.IsNullOrWhiteSpace(context.MemoryContext) &&
                               context.OutputContext?.HadHighConfidenceMemory == true ||
                               !string.IsNullOrWhiteSpace(CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage));

            if (hasGrounding)
                return false;

            if (ContainsNumericFactClaim(reply) && reply.IndexOf("bilmiyorum", StringComparison.OrdinalIgnoreCase) < 0)
            {
                issue = Issue(SelfDebugIssueType.HallucinatedFact, HallucinationRisk, "WebIntelligenceRouter",
                    "Factual claim without memory or web support.");
                return true;
            }

            return false;
        }

        public static bool DetectLanguageMismatch(string reply, SelfDebugContext context, out SelfDebugIssue issue)
        {
            issue = null;
            if (string.IsNullOrWhiteSpace(reply) || context == null)
                return false;

            var expected = context.LanguageState;
            var replyLang = DetectReplyLanguage(reply, expected);
            if (replyLang != expected && !IsBilingualAcceptable(context.UserMessage, reply, expected, replyLang))
            {
                issue = Issue(SelfDebugIssueType.LanguageMismatch, LanguageMismatch, "LanguageDetectionLayer",
                    "Response language does not match language state.");
                return true;
            }

            return false;
        }

        private static bool LooksLikeClarification(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return lower.Contains("?") ||
                   lower.Contains("açar mısın") || lower.Contains("acar misin") ||
                   lower.Contains("ne demek istediğini") || lower.Contains("ne demek istedigini") ||
                   lower.Contains("ne vermemi") || lower.Contains("ne yapmamı") ||
                   lower.Contains("neyi hatırlamamı") || lower.Contains("neyi hatirlamami") ||
                   lower.Contains("rephrase") || lower.Contains("could you");
        }

        private static bool HasMemoryGrounding(string reply, SelfDebugContext context)
        {
            if (MemoryRecallHelper.IsRecallQuery(context.UserMessage))
            {
                var expected = MemoryRecallHelper.TryBuildDirectRecallReply(
                    context.UserMessage,
                    MemoryContextNormalizer.Normalize(context.MemoryContext),
                    context.OwnerName);
                if (!string.IsNullOrWhiteSpace(expected))
                {
                    var a = Normalize(reply);
                    var e = Normalize(expected);
                    return a.Contains(e) || e.Contains(a) || a.Length > 0;
                }
            }

            if (reply.IndexOf("hatırladım", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reply.IndexOf("hatirladim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reply.IndexOf("hedefini not ettim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reply.IndexOf("hitap etmeyeceğim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reply.IndexOf("kaydettim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reply.IndexOf("not aldım", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return !string.IsNullOrWhiteSpace(context.MemoryContext);
        }

        private static bool ContainsNumericFactClaim(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return lower.Any(char.IsDigit) &&
                   (lower.Contains("tl") || lower.Contains("usd") || lower.Contains("dolar") ||
                    lower.Contains("kur") || lower.Contains("fiyat") || lower.Contains("rate"));
        }

        private static LanguageState DetectReplyLanguage(string reply, LanguageState expected)
        {
            var lower = (reply ?? string.Empty).ToLowerInvariant();
            if (ContainsTurkishChars(lower) ||
                lower.Contains("ne vermemi") || lower.Contains("erişemiyorum") ||
                lower.Contains("erisemiyorum") || lower.Contains("hatırladım") ||
                lower.Contains("hatirladim") || lower.Contains("programlama dili"))
                return LanguageState.Turkish;

            var englishHits = 0;
            if (lower.Contains(" the ")) englishHits++;
            if (lower.Contains(" is ")) englishHits++;
            if (lower.Contains(" are ")) englishHits++;
            if (lower.Contains("language")) englishHits++;

            return englishHits >= 2
                ? LanguageState.English
                : expected;
        }

        private static bool ContainsTurkishChars(string lower) =>
            lower.IndexOf('ı') >= 0 || lower.IndexOf('ş') >= 0 || lower.IndexOf('ğ') >= 0 ||
            lower.IndexOf('ü') >= 0 || lower.IndexOf('ö') >= 0 || lower.IndexOf('ç') >= 0;

        private static bool IsBilingualAcceptable(
            string userMessage,
            string reply,
            LanguageState expected,
            LanguageState replyLang)
        {
            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(userMessage) &&
                userMessage.IndexOf("what is", StringComparison.OrdinalIgnoreCase) >= 0)
                return replyLang == LanguageState.English;

            return expected == replyLang;
        }

        private static DecisionBrainAction MapIntent(UnifiedPipelineIntent intent)
        {
            switch (intent)
            {
                case UnifiedPipelineIntent.Clarification:
                    return DecisionBrainAction.Clarification;
                case UnifiedPipelineIntent.Greeting:
                case UnifiedPipelineIntent.CasualChat:
                    return DecisionBrainAction.ChatResponse;
                case UnifiedPipelineIntent.Explanation:
                case UnifiedPipelineIntent.Question:
                    return DecisionBrainAction.ReasoningResponse;
                default:
                    return DecisionBrainAction.ChatResponse;
            }
        }

        private static string Normalize(string text) =>
            (text ?? string.Empty).Trim().ToLowerInvariant();

        private static SelfDebugIssue Issue(
            SelfDebugIssueType type,
            string code,
            string module,
            string description) =>
            new SelfDebugIssue
            {
                Type = type,
                FailureCode = code,
                RootModule = module,
                Description = description
            };
    }
}
