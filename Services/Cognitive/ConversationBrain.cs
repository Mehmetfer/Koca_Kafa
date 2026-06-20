using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Koca_Kafa.MemoryStore;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services.Cognitive
{
    public interface IConversationBrain
    {
        ConversationBrainDecision ProcessTurn(ConversationBrainInput input);
        void RecordAssistantResponse(string sessionId, string reply, ConversationBrainDecision decision);
        ConversationBrainState GetState(string sessionId);
        void ResetSession(string sessionId);
    }

    public sealed class ConversationBrain : IConversationBrain
    {
        private readonly IMemoryExtractorService _extractor;
        private readonly ConcurrentDictionary<string, ConversationBrainState> _sessions =
            new ConcurrentDictionary<string, ConversationBrainState>(StringComparer.OrdinalIgnoreCase);

        public ConversationBrain(IMemoryExtractorService extractor)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        }

        public ConversationBrainState GetState(string sessionId) =>
            _sessions.TryGetValue(sessionId ?? string.Empty, out var state)
                ? state
                : new ConversationBrainState();

        public void ResetSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            _sessions.TryRemove(sessionId, out _);
        }

        public ConversationBrainDecision ProcessTurn(ConversationBrainInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            var sessionId = input.SessionId ?? string.Empty;
            var state = _sessions.GetOrAdd(sessionId, _ => new ConversationBrainState());

            var userMessage = input.UserMessage ?? string.Empty;
            var category = input.Cognitive?.Plan?.MessageCategory ??
                           MessageCategoryClassifier.Classify(userMessage);
            var memoryIntent = MemoryIntentClassifier.Classify(userMessage);
            var previousIntent = ParseMemoryIntent(state.MemoryFocus);
            var contextSwitched = ContextSwitchDetector.DetectTopicShift(
                state.ContextFocus,
                previousIntent,
                memoryIntent,
                category);

            var relationship = RelationshipMemoryLayer.Build(input.MemoryContext, input.OwnerName, state);
            UpdateBrainState(state, input, category, memoryIntent, relationship, contextSwitched);

            var empathyAllowed = ShouldAllowEmpathy(category, input);
            var allowGreeting = ShouldAllowGreeting(category, input, state);
            var personaDirective = BuildPersonaDirective(state, relationship, contextSwitched);
            var empathyDirective = empathyAllowed
                ? BuildEmpathyDirective(category, input)
                : string.Empty;

            var responseRequest = new ConversationBrainResponseRequest
            {
                Input = input,
                State = state,
                Relationship = relationship,
                Category = category,
                EmpathyAllowed = empathyAllowed,
                AllowGreeting = allowGreeting,
                Extractor = _extractor
            };

            var directReply = ConversationBrainResponseBuilder.TryBuild(responseRequest);
            var source = ResolveSource(directReply, category, empathyAllowed);

            return new ConversationBrainDecision
            {
                State = state,
                DirectReply = directReply,
                UseLlm = string.IsNullOrWhiteSpace(directReply),
                PersonaDirective = personaDirective,
                EmpathyDirective = empathyDirective,
                EmpathyInjected = empathyAllowed && !string.IsNullOrWhiteSpace(directReply),
                MemoryIntent = memoryIntent,
                MessageCategory = category,
                ContextSwitched = contextSwitched,
                ResponseSource = source
            };
        }

        public void RecordAssistantResponse(string sessionId, string reply, ConversationBrainDecision decision)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || decision?.State == null)
                return;

            var state = decision.State;
            state.TurnCount++;
            state.LastUpdatedUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(reply))
                return;

            if (decision.MessageCategory == MessageCategory.Greeting ||
                decision.MessageCategory == MessageCategory.CasualChat)
            {
                state.LastGreetingSignature = PersonalityVariationGuard.BuildGreetingSignature(reply);
            }

            if (decision.EmpathyInjected ||
                decision.MessageCategory == MessageCategory.EmotionalStatement ||
                decision.MessageCategory == MessageCategory.ImplicitEmotionalStatement)
            {
                state.LastEmpathySignature = PersonalityVariationGuard.BuildEmpathySignature(reply);
            }

            _sessions[sessionId] = state;
        }

        private static void UpdateBrainState(
            ConversationBrainState state,
            ConversationBrainInput input,
            MessageCategory category,
            MemoryIntentKind memoryIntent,
            RelationshipProfile relationship,
            bool contextSwitched)
        {
            state.ActiveIntent = category;
            state.MemoryFocus = memoryIntent.ToString();
            state.ContextFocus = ContextSwitchDetector.ResolveFocus(memoryIntent, category);
            state.ForbidBabaAddress = relationship.ForbidBabaAddress;
            state.PreferredName = relationship.PreferredName;
            state.TrustLevel = relationship.TrustLevel;
            state.RelationshipStage = relationship.Stage;
            state.TurnCount++;
            state.LastUpdatedUtc = DateTime.UtcNow;

            if (relationship.ForbidBabaAddress)
                state.LastUserPreference = "avoid_baba";

            if (!string.IsNullOrWhiteSpace(relationship.PreferredName))
                state.LastUserPreference = "preferred_name:" + relationship.PreferredName;

            if (category == MessageCategory.EmotionalStatement ||
                category == MessageCategory.ImplicitEmotionalStatement)
            {
                state.EmotionalState = input.Cognitive?.LegacyEmpathy?.Emotion.ToString() ?? "emotional";
            }

            if (category == MessageCategory.Goal || memoryIntent == MemoryIntentKind.GoalPlanning)
                state.ActiveGoal = ExtractActiveGoal(input.MemoryContext) ?? state.ActiveGoal;

            if (contextSwitched && memoryIntent != MemoryIntentKind.General)
                state.MemoryFocus = memoryIntent.ToString();
        }

        private static bool ShouldAllowEmpathy(MessageCategory category, ConversationBrainInput input)
        {
            if (category == MessageCategory.EmotionalStatement)
                return true;

            if (category == MessageCategory.ImplicitEmotionalStatement)
            {
                var confidence = input.Cognitive?.Plan?.ImplicitEmotionConfidence ??
                                 ImplicitEmotionDetector.Detect(input.UserMessage).Confidence;
                return confidence > ImplicitEmotionDetector.EmpathyThreshold;
            }

            return false;
        }

        private static bool ShouldAllowGreeting(
            MessageCategory category,
            ConversationBrainInput input,
            ConversationBrainState state)
        {
            if (category == MessageCategory.EmotionalStatement ||
                category == MessageCategory.ImplicitEmotionalStatement ||
                category == MessageCategory.MemoryReference)
                return false;

            if (input.Cognitive?.Plan?.RequiredMemory == true)
                return false;

            if (MemoryRecallHelper.IsRecallQuery(input.UserMessage))
                return false;

            return category == MessageCategory.Greeting ||
                   category == MessageCategory.CasualChat ||
                   GreetingEngine.IsGreetingMessage(input.UserMessage) ||
                   GreetingEngine.IsSmallTalkMessage(input.UserMessage);
        }

        private static string BuildPersonaDirective(
            ConversationBrainState state,
            RelationshipProfile relationship,
            bool contextSwitched)
        {
            var parts = new List<string>
            {
                "CONVERSATION BRAIN — personality shaping",
                "Tone: warm, slightly informal, supportive, non-repetitive.",
                "Max response length: 1-3 sentences unless translation/task requires more.",
                "Do not repeat previous greeting or empathy patterns."
            };

            if (relationship.ForbidBabaAddress)
                parts.Add("RELATIONSHIP OVERRIDE: Never use 'baba' as address.");

            if (!string.IsNullOrWhiteSpace(relationship.PreferredName))
                parts.Add("Preferred address: " + relationship.PreferredName);

            if (!string.IsNullOrWhiteSpace(state.ActiveGoal))
                parts.Add("Active goal (contextual only): " + state.ActiveGoal);

            if (contextSwitched)
                parts.Add("Topic shifted — do not force previous topic memory into this reply.");

            return string.Join("\n", parts);
        }

        private static string BuildEmpathyDirective(MessageCategory category, ConversationBrainInput input)
        {
            if (category == MessageCategory.EmotionalStatement)
                return EmpathyResponseEngine.BuildPromptDirective();

            if (category == MessageCategory.ImplicitEmotionalStatement)
            {
                var implicitResult = ImplicitEmotionDetector.Detect(input.UserMessage);
                return EmpathyResponseEngine.BuildImplicitPromptDirective(implicitResult);
            }

            return string.Empty;
        }

        private static string ExtractActiveGoal(string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(memoryContext))
                return null;

            const string marker = "[Entity:active_goal]";
            var index = memoryContext.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            var lineEnd = memoryContext.IndexOf('\n', index);
            var line = lineEnd < 0
                ? memoryContext.Substring(index)
                : memoryContext.Substring(index, lineEnd - index);

            var parts = line.Split(new[] { ']' }, 2);
            return parts.Length < 2 ? null : parts[1].Trim();
        }

        private static MemoryIntentKind ParseMemoryIntent(string focus)
        {
            if (string.IsNullOrWhiteSpace(focus))
                return MemoryIntentKind.General;

            MemoryIntentKind parsed;
            return Enum.TryParse(focus, true, out parsed) ? parsed : MemoryIntentKind.General;
        }

        private static string ResolveSource(string directReply, MessageCategory category, bool empathyAllowed)
        {
            if (!string.IsNullOrWhiteSpace(directReply))
            {
                if (directReply.IndexOf("hatırladım", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    directReply.IndexOf("not ettim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    directReply.IndexOf("hitap etmeyeceğim", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "brain:learn";

                if (category == MessageCategory.MemoryReference)
                    return "brain:memory";
                if (empathyAllowed)
                    return "brain:empathy";
                if (category == MessageCategory.Greeting || category == MessageCategory.CasualChat)
                    return "brain:greeting";
                return "brain:direct";
            }

            return "brain:llm";
        }
    }
}
