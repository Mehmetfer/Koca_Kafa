using System;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public static class IntentActionRouter
    {
        public const double ClarificationThreshold = DecisionLockGate.IntentResolutionThreshold;

        public static IntentRouteResult Route(string message)
        {
            var text = (message ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Unknown,
                    Confidence = 0.2,
                    RequiresClarification = true,
                    RootModule = "IntentActionRouter"
                };
            }

            if (LanguageDetectionLayer.IsLanguageSwitchRequest(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Command,
                    Confidence = 0.95,
                    RequiresClarification = false,
                    RootModule = "LanguageDetectionLayer"
                };
            }

            if (GreetingEngine.IsSmallTalkMessage(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Chat,
                    Confidence = 0.90,
                    RequiresClarification = false,
                    RootModule = "GreetingEngine"
                };
            }

            if (IsLikelyGibberish(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Unknown,
                    Confidence = 0.25,
                    RequiresClarification = true,
                    RootModule = "IntentActionRouter"
                };
            }

            if (GreetingEngine.IsGreetingMessage(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Greeting,
                    Confidence = 0.94,
                    RequiresClarification = false,
                    RootModule = "GreetingEngine"
                };
            }

            if (MemoryRecallHelper.IsMemoryQuestion(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.MemoryQuery,
                    Confidence = 0.92,
                    RequiresClarification = false,
                    RootModule = "MemoryRecallHelper"
                };
            }

            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(text) ||
                CoreChatKnowledgeBaseline.TryAnswer(text) != null)
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.FactQuery,
                    Confidence = 0.88,
                    RequiresClarification = false,
                    RootModule = "KnowledgeResponseEngine"
                };
            }

            if (CreativeTaskEngine.IsCreativeTask(text) || CoreChatIntentClassifier.Classify(text) == CoreChatIntentKind.Request)
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Command,
                    Confidence = 0.84,
                    RequiresClarification = false,
                    RootModule = "CreativeTaskEngine"
                };
            }

            if (GreetingEngine.IsSmallTalkMessage(text) ||
                MessageCategoryClassifier.IsEmotionalStatement(text) ||
                MessageCategoryClassifier.IsImplicitEmotionalStatement(text))
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.Chat,
                    Confidence = 0.82,
                    RequiresClarification = false,
                    RootModule = "GreetingEngine"
                };
            }

            if (CoreChatIntentClassifier.Classify(text) == CoreChatIntentKind.Question ||
                CoreChatIntentClassifier.Classify(text) == CoreChatIntentKind.Explanation)
            {
                return new IntentRouteResult
                {
                    Kind = RoutedIntentKind.FactQuery,
                    Confidence = 0.78,
                    RequiresClarification = false,
                    RootModule = "CoreChatIntentClassifier"
                };
            }

            var casual = CoreChatIntentClassifier.Classify(text) == CoreChatIntentKind.CasualChat;
            return new IntentRouteResult
            {
                Kind = casual ? RoutedIntentKind.Chat : RoutedIntentKind.Unknown,
                Confidence = casual ? 0.72 : 0.45,
                RequiresClarification = !casual,
                RootModule = "CoreChatIntentClassifier"
            };
        }

        public static UnifiedPipelineIntent RoutePipelineIntent(string message)
        {
            var route = Route(message);
            if (route.RequiresClarification || route.Confidence < ClarificationThreshold)
                return UnifiedPipelineIntent.Clarification;

            switch (route.Kind)
            {
                case RoutedIntentKind.Greeting:
                    return UnifiedPipelineIntent.Greeting;
                case RoutedIntentKind.FactQuery:
                {
                    var lower = (message ?? string.Empty).Trim().ToLowerInvariant();
                    if (lower.Contains("nedir") || lower.Contains("ne demek") || lower.Contains("explain") ||
                        KnowledgeQuestionClassifier.IsTopicSpecificKnowledgeQuery(message))
                        return UnifiedPipelineIntent.Explanation;
                    return UnifiedPipelineIntent.Question;
                }
                case RoutedIntentKind.MemoryQuery:
                    return UnifiedPipelineIntent.Question;
                case RoutedIntentKind.Command:
                    return UnifiedPipelineIntent.Request;
                case RoutedIntentKind.Chat:
                    return UnifiedPipelineIntent.CasualChat;
                default:
                    return UnifiedPipelineIntent.Clarification;
            }
        }

        public static string BuildRoutedReply(string userMessage, IntentRouteResult route, string ownerName, string memoryContext)
        {
            if (route == null || route.RequiresClarification || route.Confidence < ClarificationThreshold)
                return ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);

            switch (route.Kind)
            {
                case RoutedIntentKind.Greeting:
                    return GreetingEngine.TryBuildDirectReply(userMessage, MessageIntent.Greeting, null, ownerName, memoryContext)
                           ?? GreetingEngine.BuildGreetingReply(userMessage, UserPreferenceResolver.ResolveHitap(ownerName, memoryContext));
                case RoutedIntentKind.MemoryQuery:
                    return MemoryRecallHelper.TryBuildDirectRecallReply(userMessage, memoryContext, ownerName)
                           ?? ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);
                case RoutedIntentKind.FactQuery:
                    var knowledge = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                    {
                        Kind = KnowledgeQuestionClassifier.Classify(userMessage),
                        UserMessage = userMessage,
                        OwnerName = ownerName,
                        MemoryContext = memoryContext,
                        HadMemoryResults = !string.IsNullOrWhiteSpace(memoryContext)
                    });
                    if (!string.IsNullOrWhiteSpace(knowledge))
                        return knowledge;
                    return CoreChatKnowledgeBaseline.TryAnswer(userMessage)
                           ?? ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);
                case RoutedIntentKind.Command:
                    return ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);
                case RoutedIntentKind.Chat:
                    return GreetingEngine.TryBuildDirectReply(userMessage, MessageIntent.Greeting, null, ownerName, memoryContext)
                           ?? GreetingEngine.BuildSmallTalkReply(UserPreferenceResolver.ResolveHitap(ownerName, memoryContext));
                default:
                    return ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);
            }
        }

        private static bool IsLikelyGibberish(string message)
        {
            var text = message.Trim();
            if (text.Length < 4)
                return true;

            if (text.Contains(" ") || text.Contains("?"))
                return false;

            if (GreetingEngine.IsGreetingMessage(text) || GreetingEngine.IsSmallTalkMessage(text))
                return false;

            if (MemoryRecallHelper.IsMemoryQuestion(text) || KnowledgeQuestionClassifier.IsKnowledgeIntent(text))
                return false;

            var lower = text.ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\d"))
                return false;

            if (ContainsAny(lower, "merhaba", "selam", "nasıl", "nasil", "nedir", "python", "neden", "nasılsın", "nasilsin"))
                return false;

            var vowels = CountTurkishVowels(lower);
            if (vowels == 0)
                return true;

            if (Regex.IsMatch(lower, @"[bcdfghjklmnpqrstvwxyzçğ]{6,}"))
                return true;

            return text.Length >= 10 && vowels <= 1;
        }

        private static int CountTurkishVowels(string lower)
        {
            var count = 0;
            foreach (var c in lower)
            {
                if ("aeıioöuüAEIİOÖUÜ".IndexOf(c) >= 0)
                    count++;
            }

            return count;
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
