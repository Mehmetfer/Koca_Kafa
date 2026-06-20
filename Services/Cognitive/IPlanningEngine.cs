using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public interface IPlanningEngine
    {
        ResponsePlan CreatePlan(string message, MessageIntentAnalysis intent);
        string BuildPromptContext(ResponsePlan plan);
    }
}
