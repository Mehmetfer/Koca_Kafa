using System;
using System.Collections.Generic;
using System.Linq;
using Koca_Kafa.Core.RuntimeContext;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class ConversationBrainResponseRequest
    {
        public ConversationBrainInput Input { get; set; }
        public ConversationBrainState State { get; set; }
        public RelationshipProfile Relationship { get; set; }
        public MessageCategory Category { get; set; }
        public bool EmpathyAllowed { get; set; }
        public bool AllowGreeting { get; set; }
        public IMemoryExtractorService Extractor { get; set; }
    }

    public static class ConversationBrainResponseBuilder
    {
        public static string TryBuild(ConversationBrainResponseRequest request)
        {
            if (request?.Input == null)
                return null;

            var userMessage = request.Input.UserMessage ?? string.Empty;
            var memoryContext = request.Input.MemoryContext;
            var ownerName = request.Input.OwnerName;
            var cognitive = request.Input.Cognitive;
            var legacyIntent = cognitive?.LegacyIntent?.Intent ?? MessageIntent.Unknown;

            if (CreativeTaskEngine.IsCreativeTask(userMessage))
                return null;

            var learnAck = TryBuildLearnAcknowledgment(request);
            if (!string.IsNullOrWhiteSpace(learnAck))
                return ApplyRelationship(learnAck, request.Relationship, memoryContext);

            var relationshipAck = TryBuildRelationshipAcknowledgment(userMessage);
            if (!string.IsNullOrWhiteSpace(relationshipAck))
                return ApplyRelationship(relationshipAck, request.Relationship, memoryContext);

            if (!string.IsNullOrWhiteSpace(memoryContext) &&
                (cognitive?.Plan?.RequiredMemory == true || MemoryRecallHelper.IsMemoryQuestion(userMessage)))
            {
                var recall = MemoryRecallHelper.TryBuildDirectRecallReply(userMessage, memoryContext, ownerName);
                if (!string.IsNullOrWhiteSpace(recall))
                    return ApplyRelationship(recall, request.Relationship, memoryContext);
            }

            if (MissingResponseGuard.IsLikelyKittenName(userMessage))
            {
                var clarify = ClarificationResponseEngine.BuildClarification(userMessage, ownerName, memoryContext);
                if (!string.IsNullOrWhiteSpace(clarify))
                    return ApplyRelationship(clarify, request.Relationship, memoryContext);
            }

            if (request.EmpathyAllowed)
            {
                var empathy = TryBuildControlledEmpathy(request);
                if (!string.IsNullOrWhiteSpace(empathy))
                    return ApplyRelationship(empathy, request.Relationship, memoryContext);
            }

            if (request.AllowGreeting)
            {
                var greeting = GreetingEngine.TryBuildDirectReply(
                    userMessage,
                    legacyIntent,
                    cognitive?.LegacyEmpathy,
                    ownerName,
                    memoryContext);

                if (!string.IsNullOrWhiteSpace(greeting))
                {
                    var signature = PersonalityVariationGuard.BuildGreetingSignature(greeting);
                    if (!PersonalityVariationGuard.ShouldSkipRepeatedGreeting(signature, request.State?.LastGreetingSignature))
                        return ApplyRelationship(greeting, request.Relationship, memoryContext);
                }
            }

            if (DateTimeAwarenessEngine.IsDateTimeQuestion(userMessage))
            {
                var dateReply = DateTimeAwarenessEngine.TryBuildDirectReply(
                    userMessage,
                    request.Input.DateTimeContext ?? DateTimeContext.CaptureNow(),
                    ownerName);
                if (!string.IsNullOrWhiteSpace(dateReply))
                    return ApplyRelationship(dateReply, request.Relationship, memoryContext);
            }

            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(userMessage))
            {
                var knowledge = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = cognitive?.Plan?.KnowledgeKind ?? KnowledgeQuestionClassifier.Classify(userMessage),
                    UserMessage = userMessage,
                    OwnerName = ownerName,
                    RagContext = request.Input.RagContext,
                    MemoryContext = memoryContext,
                    HadRagResults = request.Input.HadRagResults,
                    HadMemoryResults = request.Input.HadMemoryResults,
                    MemoryCount = request.Input.MemoryCount,
                    Level = request.Input.Level,
                    AgeStage = request.Input.AgeStage
                });

                if (!string.IsNullOrWhiteSpace(knowledge))
                    return ApplyRelationship(knowledge, request.Relationship, memoryContext);
            }

            return null;
        }

        private static string TryBuildLearnAcknowledgment(ConversationBrainResponseRequest request)
        {
            var message = request.Input.UserMessage ?? string.Empty;
            if (MemoryRecallHelper.IsMemoryQuestion(message))
                return null;

            if (MessageCategoryClassifier.IsEmotionalStatement(message) ||
                MessageCategoryClassifier.IsImplicitEmotionalStatement(message))
                return null;

            if (GreetingEngine.IsGreetingMessage(message) || GreetingEngine.IsSmallTalkMessage(message))
                return null;

            if (request.Extractor == null)
                return null;

            var extracted = request.Extractor.Extract(message);
            if (extracted == null || extracted.Count == 0)
                return null;

            foreach (var item in extracted.Where(i => i.ShouldSave))
            {
                if (string.Equals(item.EntityKey, EntityKeys.AvoidNicknameBaba, StringComparison.OrdinalIgnoreCase))
                    return "Bundan sonra sana öyle hitap etmeyeceğim.";

                if (string.Equals(item.EntityKey, EntityKeys.PreferredName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Content))
                    return "Tamam, sana " + item.Content.Trim() + " diye hitap edeceğim.";

                if (string.Equals(item.EntityKey, EntityKeys.CatName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Content))
                    return item.Content.Trim() + "'u hatırladım.";

                if (string.Equals(item.EntityKey, EntityKeys.DogName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Content))
                    return item.Content.Trim() + "'u hatırladım.";

                if (string.Equals(item.EntityKey, EntityKeys.ActiveGoal, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Content))
                    return "Hedefini not ettim: " + item.Content.Trim() + ".";
            }

            return null;
        }

        private static string TryBuildRelationshipAcknowledgment(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var lower = message.Trim().ToLowerInvariant();
            if (lower.Contains("bana baba deme") || lower.Contains("bana 'baba' deme"))
                return "Bundan sonra sana öyle hitap etmeyeceğim.";

            return null;
        }

        private static string TryBuildControlledEmpathy(ConversationBrainResponseRequest request)
        {
            var userMessage = request.Input.UserMessage ?? string.Empty;
            string reply = null;

            if (MessageCategoryClassifier.IsEmotionalStatement(userMessage))
            {
                reply = EmpathyResponseEngine.TryBuildDirectReply(
                    userMessage,
                    request.Input.OwnerName,
                    request.Input.Cognitive?.LegacyEmpathy,
                    request.Input.MemoryContext);
            }
            else if (MessageCategoryClassifier.IsImplicitEmotionalStatement(userMessage) &&
                     ImplicitEmotionDetector.ShouldTriggerEmpathy(userMessage))
            {
                reply = EmpathyResponseEngine.TryBuildImplicitReply(
                    userMessage,
                    request.Input.OwnerName,
                    request.Input.MemoryContext);
            }

            if (string.IsNullOrWhiteSpace(reply))
                return null;

            reply = PersonalityVariationGuard.LimitEmpathySentences(reply, 2);
            var signature = PersonalityVariationGuard.BuildEmpathySignature(reply);
            if (PersonalityVariationGuard.ShouldSkipRepeatedEmpathy(signature, request.State?.LastEmpathySignature))
                return null;

            return reply;
        }

        private static string ApplyRelationship(string reply, RelationshipProfile relationship, string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return reply;

            var text = UserPreferenceResolver.StripForbiddenNickname(reply, memoryContext);
            return RelationshipMemoryLayer.ApplyOverrides(text, relationship);
        }
    }
}
