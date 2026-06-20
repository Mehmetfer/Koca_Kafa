using Koca_Kafa.Core;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class SelfDebugContext
    {
        public string UserMessage { get; set; }
        public string Reply { get; set; }
        public string DraftReply { get; set; }
        public string MemoryContext { get; set; }
        public string WebContext { get; set; }
        public string OwnerName { get; set; }
        public ProductionOutputContext OutputContext { get; set; }
        public SelfCheckOutcome SelfCheckOutcome { get; set; }
        public KnowledgeQuestionKind KnowledgeKind { get; set; }
        public DecisionBrainAction DecisionAction { get; set; }
        public bool DecisionActionSet { get; set; }
        public LanguageState LanguageState { get; set; } = LanguageState.Turkish;
        public bool HadWebResults { get; set; }
        public int MaxIterations { get; set; } = 2;
    }
    public interface ISelfDebugEngine
    {
        SelfDebugOutcome EvaluateAndRepair(SelfDebugContext context);
    }
}
