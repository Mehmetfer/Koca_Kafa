using Koca_Kafa.AI.Personality;
using Koca_Kafa.Core;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class MessageIntentAnalyzer : IMessageIntentAnalyzer
    {
        private readonly IIntentAnalyzer _core;

        public MessageIntentAnalyzer(IIntentAnalyzer core)
        {
            _core = core;
        }

        public MessageIntentAnalysis Analyze(string message)
        {
            var result = _core.Analyze(message);
            return IntentBridge.ToLegacy(result, message);
        }

        public string BuildPromptContext(MessageIntentAnalysis analysis)
        {
            if (analysis == null)
                return string.Empty;

            return ConversationalPersonalityRules.InternalStatePrefix + "\n" +
                   "Current Intent:\n" + analysis.Intent + "\n\n" +
                   "Confidence:\n" + analysis.Confidence.ToString("0.00") + "\n\n" +
                   "Reason:\n" + (analysis.Reason ?? string.Empty) + "\n\n" +
                   "Behavior:\n" + (analysis.Behavior ?? string.Empty);
        }
    }
}
