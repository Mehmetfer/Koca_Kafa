using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class SelfCheckContext
    {
        public MessageIntentAnalysis Intent { get; set; }
        public ResponsePlan Plan { get; set; }
        public EmpathyAnalysis Empathy { get; set; }
        public string RagContext { get; set; }
        public RagRetrievalMode? RagMode { get; set; }
        public bool HadRagResults { get; set; }
        public bool HadMemoryResults { get; set; }
        public string MemoryContext { get; set; }
        public KnowledgeQuestionKind KnowledgeKind { get; set; }
        public string UserMessage { get; set; }
        public bool IsDateTimeQuestion { get; set; }
        public CreativeTaskKind CreativeTaskKind { get; set; }
        public MessageCategory MessageCategory { get; set; }
        public double ImplicitEmotionConfidence { get; set; }
    }

    public interface ISelfCheckEngine
    {
        Task<SelfCheckOutcome> ValidateAndReviseAsync(
            string userMessage,
            string draftReply,
            SelfCheckContext context,
            string model,
            CancellationToken cancellationToken = default(CancellationToken),
            bool skipModelRevision = false);
    }
}
