using Koca_Kafa.Core;
using Koca_Kafa.Core.Models;
using Koca_Kafa.Core.RuntimeContext;
using Koca_Kafa.MemoryStore;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class ConversationBrainInput
    {
        public string SessionId { get; set; }
        public string UserMessage { get; set; }
        public string OwnerName { get; set; }
        public string MemoryContext { get; set; }
        public string RagContext { get; set; }
        public bool HadMemoryResults { get; set; }
        public bool HadRagResults { get; set; }
        public bool IsFirstAssistantTurn { get; set; }
        public CognitiveContext Cognitive { get; set; }
        public DateTimeContext DateTimeContext { get; set; }
        public int MemoryCount { get; set; }
        public int Level { get; set; }
        public string AgeStage { get; set; }
    }

    public sealed class ConversationBrainDecision
    {
        public ConversationBrainState State { get; set; }
        public string DirectReply { get; set; }
        public bool UseLlm { get; set; }
        public string PersonaDirective { get; set; }
        public string EmpathyDirective { get; set; }
        public bool EmpathyInjected { get; set; }
        public MemoryIntentKind MemoryIntent { get; set; }
        public MessageCategory MessageCategory { get; set; }
        public bool ContextSwitched { get; set; }
        public string ResponseSource { get; set; }
    }
}
