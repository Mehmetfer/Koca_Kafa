using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public interface IEmpathyEngine
    {
        EmpathyAnalysis Analyze(string message, MessageIntentAnalysis intent, string ownerName);
        void ApplyToPlan(ResponsePlan plan, EmpathyAnalysis empathy);
        string BuildPromptContext(EmpathyAnalysis empathy);
    }
}
