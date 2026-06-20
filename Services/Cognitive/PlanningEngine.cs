using Koca_Kafa.Core;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class PlanningEngine : IPlanningEngine
    {
        private readonly IPlanningEngineCore _core;
        private readonly IIntentAnalyzer _intentAnalyzer;

        public PlanningEngine(IPlanningEngineCore core, IIntentAnalyzer intentAnalyzer)
        {
            _core = core;
            _intentAnalyzer = intentAnalyzer;
        }

        public ResponsePlan CreatePlan(string message, MessageIntentAnalysis intent)
        {
            var intentResult = _intentAnalyzer.Analyze(message);
            return _core.CreatePlan(message, intentResult);
        }

        public string BuildPromptContext(ResponsePlan plan) => _core.BuildPromptContext(plan);
    }
}
