using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public interface IMessageIntentAnalyzer
    {
        MessageIntentAnalysis Analyze(string message);
        string BuildPromptContext(MessageIntentAnalysis analysis);
    }
}
