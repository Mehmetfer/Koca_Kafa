using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class ResponseQualityContext
    {
        public MessageIntentAnalysis Intent { get; set; }
        public EmpathyAnalysis Empathy { get; set; }
        public ResponsePlan Plan { get; set; }
        public bool HadRagResults { get; set; }
        public double? RagConfidence { get; set; }
        public string OwnerName { get; set; }
        public string UserMessage { get; set; }
        public bool HadMemoryResults { get; set; }
        public string MemoryContext { get; set; }
        public KnowledgeQuestionKind KnowledgeKind { get; set; }
        public bool IsDateTimeQuestion { get; set; }
        public CreativeTaskKind CreativeTaskKind { get; set; }
        public MessageCategory MessageCategory { get; set; }
        public double ImplicitEmotionConfidence { get; set; }
    }
}
