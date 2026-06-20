using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Core;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Cognitive.Pipeline;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class SelfDebugEngine : ISelfDebugEngine
    {
        private static readonly string[] UncertaintyPhrases =
        {
            "bilmiyorum", "emin değilim", "emin degilim", "tahmin", "sanırım", "sanirim", "galiba"
        };

        private static readonly string[] MemoryLeakMarkers =
        {
            "[entity:", "entity:", "avoid_nickname", "preferred_name", "cat_name:",
            "[kullanıcı tercihleri]", "[kullanici tercihleri]", "[ilgili hafıza]", "[ilgili hafiza]",
            "entity:avoid", "entity:preferred", "entity:cat", "entity:active"
        };

        private readonly IResponseGenerator _responseGenerator;

        public SelfDebugEngine(IResponseGenerator responseGenerator)
        {
            _responseGenerator = responseGenerator ?? throw new ArgumentNullException(nameof(responseGenerator));
        }

        public SelfDebugOutcome EvaluateAndRepair(SelfDebugContext context)
        {
            context = context ?? new SelfDebugContext();
            var maxIterations = Math.Max(1, Math.Min(context.MaxIterations, 2));
            var reply = ReplySanitizer.Sanitize(context.Reply ?? string.Empty);
            var outcome = new SelfDebugOutcome { Reply = reply };
            var usedDecisionRecovery = false;

            for (var i = 1; i <= maxIterations; i++)
            {
                var issues = DetectIssues(reply, context);
                if (issues.Count == 0)
                {
                    outcome.Reply = reply;
                    outcome.Passed = true;
                    outcome.IterationCount = i - 1;
                    return outcome;
                }

                var primary = issues[0];
                string fixedReply;

                if (!usedDecisionRecovery && ShouldRecoverFromDecisionBrain(primary))
                {
                    fixedReply = LiveSelfDebugRecovery.TryRegenerateFromDecisionBrain(context);
                    usedDecisionRecovery = true;
                    if (string.IsNullOrWhiteSpace(fixedReply))
                        fixedReply = ApplyMinimalFix(reply, issues, context).Reply;
                }
                else
                {
                    var fixResult = ApplyMinimalFix(reply, issues, context);
                    fixedReply = ReRunGenerationPipeline(fixResult.Reply, context, fixResult.Deterministic);
                }

                fixedReply = ReplySanitizer.Sanitize(fixedReply ?? string.Empty);

                outcome.Iterations.Add(new SelfDebugIteration
                {
                    Number = i,
                    Issues = issues,
                    FixStrategy = DescribeFix(primary.Type),
                    RootModule = primary.RootModule
                });

                if (string.Equals(fixedReply, reply, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(fixedReply))
                    break;

                reply = fixedReply;

                if (PassesLiveValidation(reply, context))
                {
                    outcome.Reply = reply;
                    outcome.Passed = true;
                    outcome.IterationCount = i;
                    return outcome;
                }
            }

            var remaining = DetectIssues(reply, context);
            if (PassesLiveValidation(reply, context))
            {
                outcome.Reply = reply;
                outcome.Passed = true;
                outcome.IterationCount = outcome.Iterations.Count;
                return outcome;
            }

            outcome.Reply = BuildSafeModeReply(context);
            outcome.Passed = PassesLiveValidation(outcome.Reply, context);
            outcome.UsedSafeMode = true;
            outcome.IterationCount = outcome.Iterations.Count;
            return outcome;
        }

        private static bool ShouldRecoverFromDecisionBrain(SelfDebugIssue primary) =>
            primary != null && (
                primary.Type == SelfDebugIssueType.EchoDetected ||
                primary.Type == SelfDebugIssueType.FallbackDetected ||
                primary.Type == SelfDebugIssueType.IntentMismatch ||
                primary.Type == SelfDebugIssueType.LanguageMismatch);

        private static bool PassesLiveValidation(string reply, SelfDebugContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            if (!LiveSelfDebugValidator.PassesAllChecks(reply, context, out _))
                return false;

            if (HasMemoryLeakage(reply))
                return false;

            if (ReplySanitizer.ContainsInternalLeakage(reply))
                return false;

            return true;
        }

        internal static string DiagnoseLiveValidation(string reply, SelfDebugContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return "empty";

            if (LiveSelfDebugValidator.DetectEcho(reply, context, out var echo))
                return echo.FailureCode ?? echo.Type.ToString();

            if (LiveSelfDebugValidator.DetectFallback(reply, out var fallback))
                return fallback.FailureCode ?? fallback.Type.ToString();

            if (LiveSelfDebugValidator.DetectIntentMismatch(reply, context, out var intent))
                return intent.FailureCode ?? intent.Type.ToString();

            if (LiveSelfDebugValidator.DetectHallucinationRisk(reply, context, out var hallucination))
                return hallucination.FailureCode ?? hallucination.Type.ToString();

            if (LiveSelfDebugValidator.DetectLanguageMismatch(reply, context, out var language))
                return language.FailureCode ?? language.Type.ToString();

            if (HasMemoryLeakage(reply))
                return "MEMORY_LEAK";

            if (ReplySanitizer.ContainsInternalLeakage(reply))
                return "INTERNAL_LEAK";

            return "ok";
        }

        private static IList<SelfDebugIssue> DetectIssues(string reply, SelfDebugContext context)
        {
            var issues = new List<SelfDebugIssue>();
            if (string.IsNullOrWhiteSpace(reply))
            {
                issues.Add(Issue(SelfDebugIssueType.FormatViolation, LiveSelfDebugValidator.FallbackDetected,
                    "ProductionOutputEnforcer", "Reply was empty."));
                return issues;
            }

            if (LiveSelfDebugValidator.DetectEcho(reply, context, out var echo))
                issues.Add(echo);
            if (LiveSelfDebugValidator.DetectFallback(reply, out var fallback))
                issues.Add(fallback);
            if (LiveSelfDebugValidator.DetectIntentMismatch(reply, context, out var intent))
                issues.Add(intent);
            if (LiveSelfDebugValidator.DetectHallucinationRisk(reply, context, out var hallucination))
                issues.Add(hallucination);
            if (LiveSelfDebugValidator.DetectLanguageMismatch(reply, context, out var language))
                issues.Add(language);

            if (HasMemoryLeakage(reply))
                issues.Add(Issue(SelfDebugIssueType.MemoryLeakage, null, "KnowledgeResponseEngine",
                    "Internal entity or memory dump leaked."));

            if (HasIdentityOverride(reply, context.MemoryContext))
                issues.Add(Issue(SelfDebugIssueType.IdentityOverride, null, "UserPreferenceResolver",
                    "Forbidden nickname used despite user preference."));

            if (HasInstructionViolation(reply, context))
                issues.Add(Issue(SelfDebugIssueType.InstructionViolation, null, ResolveInstructionModule(context),
                    "Response violated active instruction mode."));

            if (HasHallucinatedFact(reply, context))
                issues.Add(Issue(SelfDebugIssueType.HallucinatedFact, LiveSelfDebugValidator.HallucinationRisk,
                    "MemoryRecallHelper", "Reply conflicts with high-confidence memory or invents facts."));

            if (HasFormatViolation(reply, context))
                issues.Add(Issue(SelfDebugIssueType.FormatViolation, null, "ProductionOutputEnforcer",
                    "Output contract or production format check failed."));

            return issues;
        }

        private sealed class FixResult
        {
            public string Reply { get; set; }
            public bool Deterministic { get; set; }
        }

        private FixResult ApplyMinimalFix(string reply, IList<SelfDebugIssue> issues, SelfDebugContext context)
        {
            var result = reply ?? string.Empty;
            var deterministic = false;
            var primary = issues.OrderBy(Priority).FirstOrDefault();
            if (primary == null)
                return new FixResult { Reply = ReplySanitizer.Sanitize(result), Deterministic = false };

            switch (primary.Type)
            {
                case SelfDebugIssueType.EchoDetected:
                case SelfDebugIssueType.FallbackDetected:
                    result = LiveSelfDebugRecovery.TryRegenerateFromDecisionBrain(context)
                             ?? ClarificationResponseEngine.BuildClarification(
                                 context.UserMessage, context.OwnerName, context.MemoryContext);
                    deterministic = true;
                    break;
                case SelfDebugIssueType.IntentMismatch:
                    result = LiveSelfDebugRecovery.TryRegenerateFromDecisionBrain(context)
                             ?? FixInstructionViolation(result, context);
                    deterministic = true;
                    break;
                case SelfDebugIssueType.LanguageMismatch:
                    result = GreetingEngine.TryBuildDirectReply(
                                 context.UserMessage, MessageIntent.Greeting, null,
                                 context.OwnerName, context.MemoryContext)
                             ?? CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage)
                             ?? result;
                    deterministic = true;
                    break;
                case SelfDebugIssueType.MemoryLeakage:
                    result = FixMemoryLeakage(result, context);
                    deterministic = !HasMemoryLeakage(result);
                    break;
                case SelfDebugIssueType.IdentityOverride:
                    result = UserPreferenceResolver.StripForbiddenNickname(result, context.MemoryContext);
                    break;
                case SelfDebugIssueType.InstructionViolation:
                    result = FixInstructionViolation(result, context);
                    if (EchoResponseGuard.IsEchoResponse(context.UserMessage, result) ||
                        EchoResponseGuard.ContainsForbiddenFallback(result))
                        result = ClarificationResponseEngine.BuildClarification(
                            context.UserMessage, context.OwnerName, context.MemoryContext);
                    deterministic = true;
                    break;
                case SelfDebugIssueType.HallucinatedFact:
                    result = FixHallucinatedFact(result, context);
                    deterministic = true;
                    break;
                case SelfDebugIssueType.FormatViolation:
                    result = FixFormatViolation(result, context);
                    deterministic = !HasFormatViolation(result, context);
                    break;
            }

            return new FixResult
            {
                Reply = ReplySanitizer.Sanitize(result),
                Deterministic = deterministic
            };
        }

        private string ReRunGenerationPipeline(string reply, SelfDebugContext context, bool deterministicOnly)
        {
            var outputContext = context.OutputContext ?? new ProductionOutputContext
            {
                QualityContext = new ResponseQualityContext
                {
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext,
                    KnowledgeKind = context.KnowledgeKind != KnowledgeQuestionKind.None
                        ? context.KnowledgeKind
                        : KnowledgeQuestionClassifier.Classify(context.UserMessage)
                },
                FilteredMemoryContext = context.MemoryContext
            };

            if (deterministicOnly)
                return ReplySanitizer.Sanitize(reply?.Trim() ?? string.Empty);

            var qualityContext = outputContext.QualityContext ?? new ResponseQualityContext
            {
                UserMessage = context.UserMessage,
                OwnerName = context.OwnerName,
                MemoryContext = context.MemoryContext
            };

            reply = _responseGenerator.PolishResponse(reply, qualityContext);
            return ProductionOutputEnforcer.Enforce(reply, outputContext);
        }

        private static string BuildSafeModeReply(SelfDebugContext context)
        {
            var outputContext = context.OutputContext ?? new ProductionOutputContext
            {
                QualityContext = new ResponseQualityContext
                {
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext
                },
                FilteredMemoryContext = context.MemoryContext
            };

            if (outputContext.HadHighConfidenceMemory || MemoryRecallHelper.IsRecallQuery(context.UserMessage))
            {
                var recall = TryBuildRecallReply(context);
                if (!string.IsNullOrWhiteSpace(recall))
                    return ProductionOutputEnforcer.Enforce(recall, outputContext);
            }

            var knowledgeKind = context.KnowledgeKind != KnowledgeQuestionKind.None
                ? context.KnowledgeKind
                : KnowledgeQuestionClassifier.Classify(context.UserMessage);

            if (knowledgeKind != KnowledgeQuestionKind.None)
            {
                var knowledge = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = knowledgeKind,
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext,
                    HadMemoryResults = !string.IsNullOrWhiteSpace(context.MemoryContext),
                    MemoryCount = string.IsNullOrWhiteSpace(context.MemoryContext) ? 0 : 1
                });
                if (!string.IsNullOrWhiteSpace(knowledge))
                    return ProductionOutputEnforcer.Enforce(knowledge, outputContext);
            }

            var baseline = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
            if (!string.IsNullOrWhiteSpace(baseline))
                return ProductionOutputEnforcer.Enforce(baseline, outputContext);

            var clarification = LiveSelfDebugRecovery.BuildForcedClarification(context);
            return ProductionOutputEnforcer.Enforce(clarification, outputContext);
        }

        private static string FixMemoryLeakage(string reply, SelfDebugContext context)
        {
            var knowledgeKind = context.KnowledgeKind != KnowledgeQuestionKind.None
                ? context.KnowledgeKind
                : KnowledgeQuestionClassifier.Classify(context.UserMessage);

            if (knowledgeKind != KnowledgeQuestionKind.None)
            {
                var rebuilt = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = knowledgeKind,
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext,
                    HadMemoryResults = !string.IsNullOrWhiteSpace(context.MemoryContext),
                    MemoryCount = string.IsNullOrWhiteSpace(context.MemoryContext) ? 0 : 1
                });
                if (!string.IsNullOrWhiteSpace(rebuilt))
                    return rebuilt;
            }

            if (MemoryRecallHelper.IsRecallQuery(context.UserMessage))
            {
                var recall = TryBuildRecallReply(context);
                if (!string.IsNullOrWhiteSpace(recall))
                    return recall;
            }

            var baseline = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
            if (!string.IsNullOrWhiteSpace(baseline))
                return baseline;

            return StripEntityArtifacts(reply);
        }

        private static string TryBuildRecallReply(SelfDebugContext context) =>
            MemoryRecallHelper.TryBuildDirectRecallReply(
                context.UserMessage,
                MemoryContextNormalizer.Normalize(context.MemoryContext),
                context.OwnerName);

        private static string FixInstructionViolation(string reply, SelfDebugContext context)
        {
            var knowledgeKind = context.KnowledgeKind != KnowledgeQuestionKind.None
                ? context.KnowledgeKind
                : KnowledgeQuestionClassifier.Classify(context.UserMessage);

            if (knowledgeKind != KnowledgeQuestionKind.None)
            {
                var rebuilt = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = knowledgeKind,
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext,
                    HadMemoryResults = !string.IsNullOrWhiteSpace(context.MemoryContext)
                });
                if (!string.IsNullOrWhiteSpace(rebuilt))
                    return rebuilt;
            }

            if (context.OutputContext?.QualityContext?.Empathy?.RequiresEmpathyFirst == true)
            {
                var empathy = EmpathyResponseEngine.TryBuildDirectReply(
                    context.UserMessage,
                    context.OwnerName,
                    context.OutputContext.QualityContext.Empathy,
                    context.MemoryContext);
                if (!string.IsNullOrWhiteSpace(empathy))
                    return empathy;
            }

            return reply;
        }

        private static string FixHallucinatedFact(string reply, SelfDebugContext context)
        {
            if (LiveSelfDebugValidator.DetectHallucinationRisk(reply, context, out _))
            {
                var groundedAnswer = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
                if (!string.IsNullOrWhiteSpace(groundedAnswer))
                    return groundedAnswer;

                if (context.DecisionAction == DecisionBrainAction.WebFactResponse && !context.HadWebResults)
                    return "Güncel kur bilgisine şu an erişemiyorum.";
            }

            var recall = TryBuildRecallReply(context);
            if (!string.IsNullOrWhiteSpace(recall))
                return recall;

            var baseline = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
            if (!string.IsNullOrWhiteSpace(baseline))
                return baseline;

            if (context.OutputContext?.HadHighConfidenceMemory == true)
            {
                foreach (var marker in UncertaintyPhrases)
                {
                    reply = Regex.Replace(reply, @"\b" + Regex.Escape(marker) + @"\b", string.Empty, RegexOptions.IgnoreCase);
                }
            }

            return reply.Trim();
        }

        private static string FixFormatViolation(string reply, SelfDebugContext context)
        {
            var rebuilt = TryRebuildGroundedReply(context);
            if (!string.IsNullOrWhiteSpace(rebuilt))
                return rebuilt;

            var wasAggressive = RuntimeRemediationOverrides.AggressiveOutputStrip;
            try
            {
                RuntimeRemediationOverrides.AggressiveOutputStrip = true;
                return ProductionOutputEnforcer.Enforce(
                    reply,
                    context.OutputContext ?? new ProductionOutputContext
                    {
                        QualityContext = new ResponseQualityContext
                        {
                            UserMessage = context.UserMessage,
                            OwnerName = context.OwnerName,
                            MemoryContext = context.MemoryContext
                        }
                    });
            }
            finally
            {
                RuntimeRemediationOverrides.AggressiveOutputStrip = wasAggressive;
            }
        }

        private static bool HasMemoryLeakage(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return MemoryLeakMarkers.Any(m => lower.Contains(m)) ||
                   Regex.IsMatch(lower, @"\bentity\s*:\s*\w+", RegexOptions.IgnoreCase);
        }

        private static bool HasIdentityOverride(string reply, string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(reply) || !UserPreferenceResolver.ShouldAvoidBaba(memoryContext))
                return false;

            return Regex.IsMatch(reply, @"\bbaba\b", RegexOptions.IgnoreCase);
        }

        private static bool HasInstructionViolation(string reply, SelfDebugContext context)
        {
            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(context.UserMessage) &&
                KnowledgeResponseEngine.ContainsEmpathyOpener(reply))
                return true;

            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(context.UserMessage) && HasMemoryLeakage(reply))
                return true;

            return false;
        }

        private static bool HasHallucinatedFact(string reply, SelfDebugContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            if (MemoryRecallHelper.IsRecallQuery(context.UserMessage))
            {
                var expected = TryBuildRecallReply(context);
                if (!string.IsNullOrWhiteSpace(expected) && !RepliesAlign(reply, expected))
                    return true;
            }

            if (context.OutputContext?.HadHighConfidenceMemory == true &&
                UncertaintyPhrases.Any(p => reply.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            if (context.SelfCheckOutcome?.Issues != null &&
                context.SelfCheckOutcome.Issues.Any(i =>
                    i.Type == SelfCheckIssueType.PossibleHallucination ||
                    i.Type == SelfCheckIssueType.RagConflict))
                return true;

            return false;
        }

        private static bool HasFormatViolation(string reply, SelfDebugContext context)
        {
            var qualityContext = context.OutputContext?.QualityContext ?? new ResponseQualityContext
            {
                UserMessage = context.UserMessage,
                OwnerName = context.OwnerName,
                MemoryContext = context.MemoryContext
            };

            if (ReplySanitizer.ContainsInternalLeakage(reply))
                return true;

            if (!ResponseOutputContract.PassesFinalCheck(reply, qualityContext))
                return true;

            if (!ProductionOutputEnforcer.PassesProductionCheck(reply, context.OutputContext))
                return true;

            return false;
        }

        private static bool RepliesAlign(string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
                return false;

            var a = NormalizeForCompare(actual);
            var e = NormalizeForCompare(expected);
            return a.Contains(e) || e.Contains(a);
        }

        private static string NormalizeForCompare(string text)
        {
            var lower = (text ?? string.Empty).ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^\p{L}\p{N}\s]", string.Empty);
            return Regex.Replace(lower, @"\s+", " ").Trim();
        }

        private static string StripEntityArtifacts(string reply)
        {
            var result = reply ?? string.Empty;
            result = Regex.Replace(result, @"\[Entity:[^\]]+\]", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"Entity:\w+[^;.\n]*", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(result) ? reply : result;
        }

        private static string ResolveInstructionModule(SelfDebugContext context)
        {
            if (KnowledgeQuestionClassifier.IsKnowledgeIntent(context.UserMessage))
                return "KnowledgeResponseEngine";

            if (context.OutputContext?.QualityContext?.Empathy?.RequiresEmpathyFirst == true)
                return "EmpathyResponseEngine";

            return "ResponseQualityEngine";
        }

        private static int Priority(SelfDebugIssue issue)
        {
            switch (issue.Type)
            {
                case SelfDebugIssueType.EchoDetected: return 0;
                case SelfDebugIssueType.FallbackDetected: return 0;
                case SelfDebugIssueType.IntentMismatch: return 1;
                case SelfDebugIssueType.HallucinatedFact: return 2;
                case SelfDebugIssueType.LanguageMismatch: return 3;
                case SelfDebugIssueType.MemoryLeakage: return 4;
                case SelfDebugIssueType.IdentityOverride: return 5;
                case SelfDebugIssueType.InstructionViolation: return 6;
                default: return 7;
            }
        }

        private static string DescribeFix(SelfDebugIssueType type)
        {
            switch (type)
            {
                case SelfDebugIssueType.EchoDetected: return "decision_brain_regenerate";
                case SelfDebugIssueType.FallbackDetected: return "decision_brain_regenerate";
                case SelfDebugIssueType.IntentMismatch: return "decision_brain_regenerate";
                case SelfDebugIssueType.LanguageMismatch: return "rebuild_language_aligned_reply";
                case SelfDebugIssueType.MemoryLeakage: return "rebuild_knowledge_or_recall";
                case SelfDebugIssueType.IdentityOverride: return "strip_forbidden_nickname";
                case SelfDebugIssueType.HallucinatedFact: return "replace_with_grounded_answer";
                case SelfDebugIssueType.InstructionViolation: return "rebuild_mode_specific_reply";
                case SelfDebugIssueType.FormatViolation: return "re_enforce_output_contract";
                default: return "unknown";
            }
        }

        private static SelfDebugIssue Issue(SelfDebugIssueType type, string failureCode, string module, string description) =>
            new SelfDebugIssue
            {
                Type = type,
                FailureCode = failureCode,
                RootModule = module,
                Description = description
            };

        private static string TryRebuildGroundedReply(SelfDebugContext context)
        {
            var baseline = CoreChatKnowledgeBaseline.TryAnswer(context.UserMessage);
            if (!string.IsNullOrWhiteSpace(baseline))
                return baseline;

            var knowledgeKind = context.KnowledgeKind != KnowledgeQuestionKind.None
                ? context.KnowledgeKind
                : KnowledgeQuestionClassifier.Classify(context.UserMessage);

            if (knowledgeKind != KnowledgeQuestionKind.None)
            {
                var knowledge = KnowledgeResponseEngine.TryBuildDirectReply(new KnowledgeResponseContext
                {
                    Kind = knowledgeKind,
                    UserMessage = context.UserMessage,
                    OwnerName = context.OwnerName,
                    MemoryContext = context.MemoryContext,
                    HadMemoryResults = !string.IsNullOrWhiteSpace(context.MemoryContext)
                });
                if (!string.IsNullOrWhiteSpace(knowledge))
                    return knowledge;
            }

            return TryBuildRecallReply(context);
        }

        private static bool IsDeterministicReply(string reply, SelfDebugContext context)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var grounded = TryRebuildGroundedReply(context);
            if (string.IsNullOrWhiteSpace(grounded))
                return false;

            return RepliesAlign(reply, grounded);
        }
    }

    internal static class MemoryContextNormalizer
    {
        public static string Normalize(string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(memoryContext))
                return memoryContext;

            var lines = memoryContext
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var normalized = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("- [", StringComparison.Ordinal))
                {
                    normalized.Add(line);
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal))
                    normalized.Add("- " + line);
                else if (line.IndexOf("entity:", StringComparison.OrdinalIgnoreCase) >= 0)
                    normalized.Add("- [" + line.TrimStart('['));
                else
                    normalized.Add(line);
            }

            return string.Join("\n", normalized);
        }
    }
}
