using System;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class EmpathyResponseEngine
    {
        private static readonly string[] ForbiddenDiagnosisPhrases =
        {
            "depresyon", "depresyondasın", "depresyondasin", "anksiyete", "bipolar",
            "teşhis", "teshis", "tanı koy", "tani koy", "hastalığın", "hastalign",
            "psikolojik hastalık", "psikolojik hastalik", "mental bozukluk"
        };

        private sealed class EmpathyTemplate
        {
            public string[] Signals { get; set; }
            public string Acknowledge { get; set; }
            public string Question { get; set; }
        }

        private sealed class ImplicitTemplate
        {
            public string Hint { get; set; }
            public string Acknowledge { get; set; }
            public string Question { get; set; }
        }

        private static readonly ImplicitTemplate[] ImplicitTemplates =
        {
            new ImplicitTemplate
            {
                Hint = "rumination",
                Acknowledge = "Bugün biraz durgun geçmiş gibi görünüyor {hitap}.",
                Question = "Aklını kurcalayan bir şey mi vardı?"
            },
            new ImplicitTemplate
            {
                Hint = "boredom",
                Acknowledge = "Gün biraz ağır ve durgun geçmiş olabilir {hitap}.",
                Question = "Seni en çok ne sıktı bugün?"
            },
            new ImplicitTemplate
            {
                Hint = "sadness",
                Acknowledge = "Bugün moralin düşük gibi görünüyor {hitap}.",
                Question = "İçini en çok ne daraltıyor?"
            },
            new ImplicitTemplate
            {
                Hint = "fatigue",
                Acknowledge = "Yorucu bir gün olmuş gibi {hitap}.",
                Question = "Biraz dinlenmeye ihtiyaç duyuyor musun?"
            },
            new ImplicitTemplate
            {
                Hint = "loneliness",
                Acknowledge = "Bugün sessiz geçmiş olabilir {hitap}.",
                Question = "İletişim eksikliği hissettin mi?"
            },
            new ImplicitTemplate
            {
                Hint = "apathy",
                Acknowledge = "Eskisine göre daha soluk bir gün mü {hitap}?",
                Question = "Seni ne hareketsiz bıraktı?"
            }
        };

        private static readonly EmpathyTemplate[] Templates =
        {
            new EmpathyTemplate
            {
                Signals = new[]
                {
                    "kimse benimle konuşmadı", "kimse benimle konusmadi",
                    "kimse konuşmadı", "kimse konusmadi", "benimle konuşmadı", "benimle konusmadi"
                },
                Acknowledge = "Bu biraz yalnız hissettirmiş olabilir {hitap}.",
                Question = "Günün nasıl geçti?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "yalnızım", "yalnizim", "yalnız hissed", "yalniz hissed", "tek başıma", "tek basima" },
                Acknowledge = "Yalnız hissetmek zor olabilir {hitap}.",
                Question = "Şu an en çok ne eksik geliyor?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "kendimi kötü hissed", "kendimi kotu hissed" },
                Acknowledge = "Kendini kötü hissetmen anlaşılır {hitap}.",
                Question = "Biraz anlatmak ister misin?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "üzgünüm", "uzgunum" },
                Acknowledge = "Üzgün hissetmen çok normal {hitap}.",
                Question = "Seni en çok ne üzdü?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "moralim bozuk", "moralim düşük", "moralim dusuk" },
                Acknowledge = "Moralinin bozuk olması zor {hitap}.",
                Question = "Bugün seni en çok ne yordu?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "çok yoruldum", "cok yoruldum" },
                Acknowledge = "Çok yorulmuşsun {hitap}.",
                Question = "Biraz dinlenmek iyi gelir mi?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "başarısız hissed", "basarisiz hissed" },
                Acknowledge = "Başarısız hissetmek ağır gelebilir {hitap}.",
                Question = "En çok hangi konuda böyle hissediyorsun?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "patron bana bağırdı", "patron bana bagirdi", "patron bağırdı", "patron bagirdi" },
                Acknowledge = "Bu can sıkıcı bir durum {hitap}.",
                Question = "Ne oldu, biraz anlatmak ister misin?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "terfi aldım", "terfi aldim", "başardım", "basardim", "kazandım", "kazandim" },
                Acknowledge = "Harika haber {hitap}!",
                Question = "Nasıl hissediyorsun şu an?"
            },
            new EmpathyTemplate
            {
                Signals = new[] { "kaybettim", "vefat etti", "özledim", "ozledim" },
                Acknowledge = "Bu gerçekten zor olmalı {hitap}.",
                Question = "İstersen biraz konuşabiliriz."
            }
        };

        public static string TryBuildDirectReply(
            string message,
            string ownerName,
            EmpathyAnalysis empathy = null,
            string memoryContext = null)
        {
            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);

            if (TryGetReplyParts(message, ownerName, out var acknowledge, out var question))
            {
                var reply = FormatReply(acknowledge, question, hitap);
                return UserPreferenceResolver.StripForbiddenNickname(reply, memoryContext);
            }

            var implicitResult = ImplicitEmotionDetector.Detect(message);
            if (implicitResult.IsImplicit &&
                implicitResult.Confidence > ImplicitEmotionDetector.EmpathyThreshold &&
                TryGetImplicitReplyParts(implicitResult, ownerName, out acknowledge, out question))
            {
                var reply = FormatReply(acknowledge, question, hitap);
                return UserPreferenceResolver.StripForbiddenNickname(reply, memoryContext);
            }

            if (empathy != null && empathy.RequiresEmpathyFirst)
            {
                var opener = (empathy.SampleOpener ?? string.Empty).Trim();
                var followUp = (empathy.FollowUpQuestion ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(opener) && !string.IsNullOrWhiteSpace(followUp))
                    return UserPreferenceResolver.StripForbiddenNickname(opener + " " + followUp, memoryContext);
                if (!string.IsNullOrWhiteSpace(opener))
                    return UserPreferenceResolver.StripForbiddenNickname(opener, memoryContext);
            }

            return null;
        }

        public static string TryBuildImplicitReply(string message, string ownerName, string memoryContext = null)
        {
            var result = ImplicitEmotionDetector.Detect(message);
            if (!result.IsImplicit || result.Confidence <= ImplicitEmotionDetector.EmpathyThreshold)
                return null;

            if (!TryGetImplicitReplyParts(result, ownerName, out var acknowledge, out var question))
                return null;

            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);
            var reply = FormatReply(acknowledge, question, hitap);
            return UserPreferenceResolver.StripForbiddenNickname(reply, memoryContext);
        }

        public static bool TryGetImplicitReplyParts(
            ImplicitEmotionResult result,
            string ownerName,
            out string acknowledge,
            out string question)
        {
            acknowledge = null;
            question = null;

            if (result == null || !result.IsImplicit)
                return false;

            var template = ResolveImplicitTemplate(result);
            acknowledge = template.Acknowledge.Trim();
            question = template.Question?.Trim();
            return true;
        }

        public static bool TryGetReplyParts(
            string message,
            string ownerName,
            out string acknowledge,
            out string question)
        {
            acknowledge = null;
            question = null;

            if (!MessageCategoryClassifier.IsEmotionalStatement(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();

            foreach (var template in Templates)
            {
                if (!template.Signals.Any(s => lower.Contains(s)))
                    continue;

                acknowledge = template.Acknowledge.Trim();
                question = template.Question?.Trim();
                return true;
            }

            acknowledge = "Anlıyorum {hitap}, zor bir his bu.";
            question = "Biraz anlatmak ister misin?";
            return true;
        }

        private static string ResolveHitap(string ownerName) =>
            string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();

        private static string FormatReply(string acknowledge, string question, string hitap)
        {
            var resolvedHitap = string.IsNullOrWhiteSpace(hitap) ? string.Empty : hitap.Trim();
            var ack = (acknowledge ?? string.Empty).Replace("{hitap}", resolvedHitap).Trim();
            ack = System.Text.RegularExpressions.Regex.Replace(ack, @"\s+([.,!?])", "$1");
            ack = System.Text.RegularExpressions.Regex.Replace(ack, @"\s{2,}", " ").Trim();
            var q = (question ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(q) ? ack : ack + " " + q;
        }

        public static string BuildPromptDirective()
        {
            return "EMPATİ MODU: Kullanıcı duygusal bir ifade paylaştı.\n" +
                   "1) Duyguyu kabul et\n" +
                   "2) Kısa empati göster\n" +
                   "3) Açık uçlu bir soru sor\n" +
                   "'Bunu bilmiyorum' deme; çözüm dayatma; dinle; teşhis koyma.";
        }

        public static string BuildImplicitPromptDirective(ImplicitEmotionResult result)
        {
            var hints = result?.Hints != null ? string.Join(", ", result.Hints) : "unknown";
            return "İMA EDİLEN DUYGU MODU (confidence=" + (result?.Confidence.ToString("0.00") ?? "0") + "):\n" +
                   "İpuçları: " + hints + "\n" +
                   "Yoklayıcı, nazik empati kullan; teşhis koyma ('depresyondasın' vb. yasak).\n" +
                   "Örnek ton: 'Bugün biraz durgun geçmiş gibi görünüyor baba. Aklını kurcalayan bir şey mi vardı?'";
        }

        public static bool ContainsDiagnosisLanguage(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return ForbiddenDiagnosisPhrases.Any(p => lower.Contains(p));
        }

        public static bool ContainsForbiddenFallback(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return true;

            var lower = reply.Trim().ToLowerInvariant();
            return lower.StartsWith("bunu bilmiyorum", StringComparison.Ordinal) ||
                   lower == "bilmiyorum baba." ||
                   lower == "bilmiyorum.";
        }

        private static ImplicitTemplate ResolveImplicitTemplate(ImplicitEmotionResult result)
        {
            if (result?.Hints != null)
            {
                foreach (var hint in result.Hints)
                {
                    var match = ImplicitTemplates.FirstOrDefault(t =>
                        string.Equals(t.Hint, hint, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }

            return ImplicitTemplates[0];
        }
    }
}
