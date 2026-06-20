using System.Collections.Generic;
using Koca_Kafa.Core;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class EmpathyEngine : IEmpathyEngine
    {
        private readonly IEmpathyEngineCore _core;
        private readonly IIntentAnalyzer _intentAnalyzer;

        public EmpathyEngine(IEmpathyEngineCore core, IIntentAnalyzer intentAnalyzer)
        {
            _core = core;
            _intentAnalyzer = intentAnalyzer;
        }

        public EmpathyAnalysis Analyze(string message, MessageIntentAnalysis intent, string ownerName)
        {
            var intentResult = _intentAnalyzer.Analyze(message);
            var tone = _core.Analyze(message, intentResult, ownerName, null);
            return EmpathyBridge.ToLegacy(tone);
        }

        public void ApplyToPlan(ResponsePlan plan, EmpathyAnalysis empathy)
        {
            if (plan == null || empathy == null || !empathy.RequiresEmpathyFirst)
                return;

            _core.ApplyToPlan(plan, new EmpathyToneResult
            {
                RequiresEmpathyFirst = true,
                EmotionalTone = empathy.Emotion.ToString(),
                SuggestedTone = empathy.Behavior,
                SampleOpener = empathy.SampleOpener,
                FollowUp = empathy.FollowUpQuestion
            });
        }

        public string BuildPromptContext(EmpathyAnalysis empathy)
        {
            if (empathy == null || !empathy.RequiresEmpathyFirst)
                return string.Empty;

            return _core.BuildPromptContext(new EmpathyToneResult
            {
                RequiresEmpathyFirst = true,
                EmotionalTone = empathy.Emotion.ToString(),
                SuggestedTone = empathy.Behavior,
                SampleOpener = empathy.SampleOpener,
                FollowUp = empathy.FollowUpQuestion
            });
        }
    }
}
