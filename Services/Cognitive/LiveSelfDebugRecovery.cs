using Koca_Kafa.Models;
using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public static class LiveSelfDebugRecovery
    {
        public static string TryRegenerateFromDecisionBrain(SelfDebugContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.UserMessage))
                return null;

            var route = IntentActionRouter.Route(context.UserMessage);
            var pipelineIntent = UnifiedIntentRouter.Route(context.UserMessage);
            var decisionContext = new DecisionBrainContext
            {
                UserMessage = context.UserMessage,
                OwnerName = context.OwnerName,
                FilteredMemoryContext = context.MemoryContext,
                LanguageState = context.LanguageState,
                PipelineIntent = pipelineIntent,
                IntentRoute = route,
                WebIntelligenceEnabled = context.HadWebResults,
                StrictRecompute = true
            };

            var decision = DecisionBrain.Evaluate(decisionContext);
            if (context.DecisionAction != 0)
                decision.Action = context.DecisionAction;

            var locked = DecisionLockGate.Lock(decision, decisionContext, attempt: 1);
            var tool = DecisionToolRouter.Execute(locked.LockedDecision, decisionContext);
            var reply = tool.DirectReply;

            if (string.IsNullOrWhiteSpace(reply))
            {
                if (locked.LockedDecision.Action == DecisionBrainAction.Clarification)
                {
                    reply = ClarificationResponseEngine.BuildClarification(
                        context.UserMessage, context.OwnerName, context.MemoryContext);
                }
                else
                {
                    reply = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
                }
            }

            if (string.IsNullOrWhiteSpace(reply))
                return null;

            return EchoResponseGuard.SanitizeReply(context.UserMessage, reply);
        }

        public static string BuildForcedClarification(SelfDebugContext context) =>
            EchoResponseGuard.SanitizeReply(
                context?.UserMessage,
                ClarificationResponseEngine.BuildClarification(
                    context?.UserMessage,
                    context?.OwnerName,
                    context?.MemoryContext));
    }
}
