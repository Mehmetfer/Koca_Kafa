using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.AI.Abstractions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class SelfCheckEngine : ISelfCheckEngine
    {
        private static readonly string[] TherapistPatterns =
        {
            "neden moraliniz",
            "neden uzgun",
            "hissettiğiniz duygular",
            "hissettiginiz duygular",
            "profesyonel destek",
            "terapist",
            "psikolog",
            "duygularınızı yönet",
            "duygularinizi yonet"
        };

        private static readonly string[] EmpathyOpenerSignals =
        {
            "üzüldüm",
            "uzuldum",
            "anlıyorum",
            "anliyorum",
            "zor bir gün",
            "zor gun",
            "can sıkıcı",
            "can sikici",
            "haklı olarak",
            "hakli olarak",
            "tebrik",
            "harika haber",
            "heyecan verici",
            "dinlenmek iyi",
            "bu gayet normal"
        };

        private static readonly string[] RoboticPatterns =
        {
            "ben koca kafa'yım",
            "ben koca kafayım",
            "size nasıl yardımcı olabilirim",
            "size nasil yardimci olabilirim",
            "bir yapay zeka",
            "bir ai asistan",
            "yenidoğan evresinde",
            "yenidogan evresinde"
        };

        private static readonly string[] UncertaintyPhrases =
        {
            "bilmiyorum",
            "emin değilim",
            "emin degilim",
            "tahmin",
            "sanırım",
            "sanirim",
            "galiba"
        };

        private readonly ILanguageModelClient _languageModelClient;

        public SelfCheckEngine(ILanguageModelClient languageModelClient)
        {
            _languageModelClient = languageModelClient ?? throw new ArgumentNullException(nameof(languageModelClient));
        }

        public async Task<SelfCheckOutcome> ValidateAndReviseAsync(
            string userMessage,
            string draftReply,
            SelfCheckContext context,
            string model,
            CancellationToken cancellationToken = default(CancellationToken),
            bool skipModelRevision = false)
        {
            var reply = (draftReply ?? string.Empty).Trim();
            if (reply.Length == 0)
            {
                return new SelfCheckOutcome
                {
                    Reply = reply,
                    Passed = false,
                    WasRevised = false,
                    Issues = new List<SelfCheckIssue>
                    {
                        new SelfCheckIssue
                        {
                            Type = SelfCheckIssueType.QuestionNotAnswered,
                            Description = "Draft reply was empty."
                        }
                    }
                };
            }

            var issues = EvaluateLocally(userMessage, reply, context);
            if (issues.Count == 0)
            {
                return new SelfCheckOutcome
                {
                    Reply = ReplySanitizer.Sanitize(reply),
                    Passed = true,
                    WasRevised = false,
                    Issues = issues
                };
            }

            var quickFixed = ApplyQuickFixes(reply, issues);
            var empathyFixed = TryEmpathyQuickRevise(quickFixed, context?.Empathy, context);
            if (!string.IsNullOrWhiteSpace(empathyFixed))
                quickFixed = empathyFixed;

            var remaining = EvaluateLocally(userMessage, quickFixed, context);
            if (remaining.Count == 0)
            {
                return new SelfCheckOutcome
                {
                    Reply = ReplySanitizer.Sanitize(quickFixed),
                    Passed = true,
                    WasRevised = !string.Equals(quickFixed, reply, StringComparison.Ordinal),
                    Issues = issues
                };
            }

            if (remaining.All(i => i.Type == SelfCheckIssueType.EmpathySkipped))
            {
                var empathyOnly = TryEmpathyQuickRevise(quickFixed, context?.Empathy, context) ?? quickFixed;
                return new SelfCheckOutcome
                {
                    Reply = ReplySanitizer.Sanitize(empathyOnly),
                    Passed = true,
                    WasRevised = true,
                    Issues = remaining
                };
            }

            if (skipModelRevision)
            {
                return new SelfCheckOutcome
                {
                    Reply = ReplySanitizer.Sanitize(quickFixed),
                    Passed = remaining.Count == 0,
                    WasRevised = !string.Equals(quickFixed, reply, StringComparison.Ordinal),
                    Issues = remaining
                };
            }

            var revised = await ReviseWithModelAsync(
                userMessage,
                quickFixed,
                remaining,
                context,
                model,
                cancellationToken).ConfigureAwait(false);

            return new SelfCheckOutcome
            {
                Reply = ReplySanitizer.Sanitize(revised),
                Passed = true,
                WasRevised = true,
                Issues = remaining
            };
        }

        private static string TryEmpathyQuickRevise(string draftReply, EmpathyAnalysis empathy, SelfCheckContext context)
        {
            if (context != null &&
                (context.KnowledgeKind != KnowledgeQuestionKind.None ||
                 context.IsDateTimeQuestion ||
                 context.CreativeTaskKind != CreativeTaskKind.None ||
                 DateTimeAwarenessEngine.IsDateTimeQuestion(context.UserMessage) ||
                 CreativeTaskEngine.IsCreativeTask(context.UserMessage) ||
                 KnowledgeQuestionClassifier.ShouldDisableEmpathy(context.UserMessage)))
                return draftReply;

            if (empathy == null || !empathy.RequiresEmpathyFirst)
                return null;

            var draft = (draftReply ?? string.Empty).Trim();
            var opener = (empathy.SampleOpener ?? string.Empty).Trim();
            if (opener.Length == 0)
                return null;

            if (HasMeaningfulEmpathy(draft.ToLowerInvariant(), empathy))
                return draft;

            var followUp = (empathy.FollowUpQuestion ?? string.Empty).Trim();
            var support = followUp.Length > 0 &&
                          !IsFollowUpOnlyReply(draft, followUp)
                ? followUp
                : "İstersen anlatabilirsin.";

            if (draft.Length == 0 || IsFollowUpOnlyReply(draft, followUp))
                return opener + " " + support;

            return opener + " " + support + " " + draft;
        }

        private static bool IsFollowUpOnlyReply(string draft, string followUp)
        {
            if (string.IsNullOrWhiteSpace(draft) || string.IsNullOrWhiteSpace(followUp))
                return false;

            var normDraft = NormalizeForEmpathyCheck(draft);
            var normFollow = NormalizeForEmpathyCheck(followUp);
            return normDraft == normFollow ||
                   normDraft.StartsWith(normFollow, StringComparison.Ordinal);
        }

        private static bool HasMeaningfulEmpathy(string lowerReply, EmpathyAnalysis empathy)
        {
            if (string.IsNullOrWhiteSpace(lowerReply))
                return false;

            if (ContainsAny(lowerReply, EmpathyOpenerSignals))
                return true;

            var opener = (empathy?.SampleOpener ?? string.Empty).Trim().ToLowerInvariant();
            if (opener.Length > 0 && lowerReply.StartsWith(opener, StringComparison.Ordinal))
                return true;

            return false;
        }

        private static string NormalizeForEmpathyCheck(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lower = text.ToLowerInvariant().Trim();
            lower = lower.TrimEnd('?', '!', '.', ' ');
            return lower;
        }

        private static List<SelfCheckIssue> EvaluateLocally(
            string userMessage,
            string reply,
            SelfCheckContext context)
        {
            var issues = new List<SelfCheckIssue>();
            var user = (userMessage ?? string.Empty).Trim();
            var lowerUser = user.ToLowerInvariant();
            var lowerReply = (reply ?? string.Empty).ToLowerInvariant();

            if (ShouldAnswerQuestion(context, user, reply))
            {
                issues.Add(new SelfCheckIssue
                {
                    Type = SelfCheckIssueType.QuestionNotAnswered,
                    Description = "The reply may not address the user's question."
                });
            }

            if (context != null && context.HadRagResults && IsStrictRagMode(context.RagMode))
            {
                if (ContainsAny(lowerReply, UncertaintyPhrases))
                {
                    issues.Add(new SelfCheckIssue
                    {
                        Type = SelfCheckIssueType.RagConflict,
                        Description = "Reply expresses uncertainty despite available knowledge-base context."
                    });
                }
            }

            if (context != null &&
                context.Plan != null &&
                context.Plan.RequiredRag &&
                !context.HadRagResults &&
                ContainsAny(lowerUser, "nedir", "kimdir", "hangi", "kaç", "kac", "?") &&
                LooksOverconfident(lowerReply))
            {
                issues.Add(new SelfCheckIssue
                {
                    Type = SelfCheckIssueType.PossibleHallucination,
                    Description = "Reply may invent facts without retrieval support."
                });
            }

            if (HasUnnecessaryRepetition(reply))
            {
                issues.Add(new SelfCheckIssue
                {
                    Type = SelfCheckIssueType.UnnecessaryRepetition,
                    Description = "Reply contains repeated sentences or phrases."
                });
            }

            if (ContainsAny(lowerReply, RoboticPatterns) || ContainsAny(lowerReply, TherapistPatterns))
            {
                issues.Add(new SelfCheckIssue
                {
                    Type = SelfCheckIssueType.RoboticPattern,
                    Description = "Reply contains robotic or therapist-like phrasing."
                });
            }

            if (context?.Empathy?.RequiresEmpathyFirst == true &&
                !context.IsDateTimeQuestion &&
                context.CreativeTaskKind == CreativeTaskKind.None &&
                !DateTimeAwarenessEngine.IsDateTimeQuestion(context.UserMessage) &&
                !CreativeTaskEngine.IsCreativeTask(context.UserMessage) &&
                !HasMeaningfulEmpathy(lowerReply, context.Empathy))
            {
                issues.Add(new SelfCheckIssue
                {
                    Type = SelfCheckIssueType.EmpathySkipped,
                    Description = "Reply should acknowledge the user's emotion before offering solutions."
                });
            }

            return issues;
        }

        private static bool ShouldAnswerQuestion(SelfCheckContext context, string user, string reply)
        {
            if (context?.Empathy?.RequiresEmpathyFirst == true)
                return false;

            var intent = context?.Intent?.Intent;
            var isQuestion = user.Contains("?") ||
                             intent == MessageIntent.Question ||
                             intent == MessageIntent.Teaching ||
                             intent == MessageIntent.Task;

            if (!isQuestion)
                return false;

            if (context?.Intent?.Intent == MessageIntent.Greeting)
                return false;

            return reply.Trim().Length < 12;
        }

        private static bool IsStrictRagMode(RagRetrievalMode? mode) =>
            mode == RagRetrievalMode.StrictRag || mode == RagRetrievalMode.DirectAnswer;

        private static bool LooksOverconfident(string lowerReply)
        {
            if (ContainsAny(lowerReply, UncertaintyPhrases))
                return false;

            return ContainsAny(
                lowerReply,
                "kesinlikle",
                "mutlaka",
                " %",
                "version",
                "sürüm",
                "surum",
                ".net",
                "framework");
        }

        private static bool HasUnnecessaryRepetition(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lines = reply
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            if (lines.Distinct(StringComparer.OrdinalIgnoreCase).Count() < lines.Count)
                return true;

            var sentences = reply
                .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 12)
                .ToList();

            return sentences.Distinct(StringComparer.OrdinalIgnoreCase).Count() < sentences.Count;
        }

        private static string ApplyQuickFixes(string reply, IList<SelfCheckIssue> issues)
        {
            var fixedReply = reply ?? string.Empty;
            if (issues.Any(i => i.Type == SelfCheckIssueType.RoboticPattern))
            {
                foreach (var pattern in RoboticPatterns)
                {
                    var index = fixedReply.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    while (index >= 0)
                    {
                        fixedReply = fixedReply.Remove(index, pattern.Length);
                        index = fixedReply.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    }
                }

                fixedReply = CollapseWhitespace(fixedReply);
            }

            if (issues.Any(i => i.Type == SelfCheckIssueType.UnnecessaryRepetition))
            {
                var lines = fixedReply
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                fixedReply = string.Join("\n", lines);
            }

            return fixedReply.Trim();
        }

        private async Task<string> ReviseWithModelAsync(
            string userMessage,
            string draftReply,
            IList<SelfCheckIssue> issues,
            SelfCheckContext context,
            string model,
            CancellationToken cancellationToken)
        {
            var userPayload = new StringBuilder();
            userPayload.AppendLine(userMessage ?? string.Empty);
            userPayload.AppendLine();
            userPayload.AppendLine(draftReply ?? string.Empty);

            if (issues != null && issues.Count > 0)
            {
                userPayload.AppendLine();
                foreach (var issue in issues)
                    userPayload.AppendLine(issue.Description);
            }

            if (context?.Empathy?.RequiresEmpathyFirst == true &&
                !string.IsNullOrWhiteSpace(context.Empathy.SampleOpener))
            {
                userPayload.AppendLine();
                userPayload.AppendLine(context.Empathy.SampleOpener);
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage(
                    ChatRole.System,
                    "Sen Koca Kafa'sın. Verilen taslak cevabı Türkçe olarak düzelt. " +
                    "YALNIZCA nihai cevabı yaz. Talimat, başlık, etiket, açıklama veya meta metin yazma."),
                new ChatMessage(ChatRole.User, userPayload.ToString().Trim())
            };

            try
            {
                var revised = await _languageModelClient
                    .GenerateReplyAsync(model, messages, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(revised))
                {
                    var sanitized = ReplySanitizer.Sanitize(revised.Trim());
                    if (sanitized.Length > 0)
                        return sanitized;
                }
            }
            catch
            {
                // fall back to quick-fixed draft
            }

            return ReplySanitizer.Sanitize(draftReply ?? string.Empty);
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
