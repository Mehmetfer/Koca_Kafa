using System;
using System.Collections.Generic;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class ImplicitEmotionDetector
    {
        public const double EmpathyThreshold = 0.35;

        private sealed class SignalGroup
        {
            public string Hint { get; set; }
            public string[] StrongSignals { get; set; }
            public string[] WeakSignals { get; set; }
            public double StrongWeight { get; set; }
            public double WeakWeight { get; set; }
        }

        private static readonly SignalGroup[] Groups =
        {
            new SignalGroup
            {
                Hint = "rumination",
                StrongSignals = new[] { "tavana baktım", "tavana baktim", "tavana bakıyorum", "tavana bakiyorum" },
                WeakSignals = new[] { "boş boş", "bos bos", "hiçbir şey yapmadım", "hicbir sey yapmadim" },
                StrongWeight = 0.38,
                WeakWeight = 0.14
            },
            new SignalGroup
            {
                Hint = "fatigue",
                StrongSignals = new[]
                {
                    "direkt yatağa", "direkt yataga", "direkt yattım", "direkt yattim",
                    "yatağa girdim", "yataga girdim", "yatağa yattım", "yataga yattim"
                },
                WeakSignals = new[] { "eve gelince", "eve gelip", "bitkin düştüm", "bitkin dustum", "tükenmiş", "tukenmis" },
                StrongWeight = 0.36,
                WeakWeight = 0.16
            },
            new SignalGroup
            {
                Hint = "loneliness",
                StrongSignals = new[]
                {
                    "kimse aramadı", "kimse aramadi", "kimse yazmadı", "kimse yazmadi",
                    "aramadı bugün", "aramadi bugun", "yazmadı bugün", "yazmadi bugun"
                },
                WeakSignals = new[] { "sessiz geçti", "sessiz gecti", "tek kaldım", "tek kaldim" },
                StrongWeight = 0.40,
                WeakWeight = 0.14
            },
            new SignalGroup
            {
                Hint = "apathy",
                StrongSignals = new[]
                {
                    "eskisi kadar heyecanlanmıyorum", "eskisi kadar heyecanlanmiyorum",
                    "heyecanlanmıyorum", "heyecanlanmiyorum", "ilgi duymuyorum", "ilgi duymuyorum"
                },
                WeakSignals = new[] { "umursamıyorum", "umursamiyorum", "motivasyonum yok", "isteksizim", "hevesim yok" },
                StrongWeight = 0.42,
                WeakWeight = 0.16
            },
            new SignalGroup
            {
                Hint = "sadness",
                StrongSignals = new[] { "içim sıkıldı", "icim sikildi", "keyifsizim", "moralim yok" },
                WeakSignals = new[] { "boşlukta", "boslukta", "anlamsız geliyor", "anlamsiz geliyor" },
                StrongWeight = 0.34,
                WeakWeight = 0.12
            },
            new SignalGroup
            {
                Hint = "boredom",
                StrongSignals = new[] { "sıkıldım", "sikildim", "canım sıkıldı", "canim sikildi", "sıkıcı geçti", "sikici gecti",
                    "hiçbir şey yapmadım", "hicbir sey yapmadim" },
                WeakSignals = new[] { "bir şey yapmadım", "bir sey yapmadim", "durgun geçti", "durgun gecti" },
                StrongWeight = 0.32,
                WeakWeight = 0.12
            }
        };

        private static readonly string[] ContextBoostSignals =
        {
            "bugün", "bugun", "bütün gün", "butun gun", "tüm gün", "tum gun"
        };

        public static ImplicitEmotionResult Detect(string message)
        {
            var empty = new ImplicitEmotionResult { Confidence = 0, IsImplicit = false };
            if (string.IsNullOrWhiteSpace(message))
                return empty;

            if (ExplicitEmotionDetector.IsExplicitEmotionalStatement(message))
                return empty;

            var lower = message.Trim().ToLowerInvariant();
            var hints = new List<string>();
            var confidence = 0.0;

            foreach (var group in Groups)
            {
                var groupScore = 0.0;
                foreach (var signal in group.StrongSignals)
                {
                    if (lower.Contains(signal))
                        groupScore = Math.Max(groupScore, group.StrongWeight);
                }

                foreach (var signal in group.WeakSignals)
                {
                    if (lower.Contains(signal))
                        groupScore = Math.Max(groupScore, group.WeakWeight);
                }

                if (groupScore <= 0)
                    continue;

                hints.Add(group.Hint);
                confidence += groupScore;
            }

            if (ContainsAny(lower, ContextBoostSignals))
                confidence += 0.08;

            confidence = Math.Round(Math.Min(0.98, confidence), 2);

            if (hints.Count == 0 || confidence <= 0)
                return empty;

            return new ImplicitEmotionResult
            {
                IsImplicit = true,
                Confidence = confidence,
                Hints = hints,
                PrimaryHint = hints[0]
            };
        }

        public static bool IsImplicitEmotionalStatement(string message) =>
            Detect(message).IsImplicit;

        public static bool ShouldTriggerEmpathy(string message)
        {
            var result = Detect(message);
            return result.IsImplicit && result.Confidence > EmpathyThreshold;
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
