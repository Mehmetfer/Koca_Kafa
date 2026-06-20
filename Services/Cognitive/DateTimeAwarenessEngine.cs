using System;
using System.Linq;
using Koca_Kafa.Core.RuntimeContext;

namespace Koca_Kafa.Services.Cognitive
{
    public static class DateTimeAwarenessEngine
    {
        private static readonly string[] DateTimeSignals =
        {
            "hangi gün", "günlerden", "bugün ne", "bugün hangi", "ne gün",
            "gün mü", "pazar mı", "pazartesi mi", "salı mı", "çarşamba mı", "carsamba mi",
            "perşembe mi", "persembe mi", "cuma mı", "cumartesi mi",
            "saat kaç", "saat ne", "kaç saat", "zaman ne",
            "tarih ne", "bugünün tarihi", "bugunun tarihi", "ayın kaçı", "ayin kaci",
            "hangi tarih", "gün ay yıl"
        };

        public static bool IsDateTimeQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return DateTimeSignals.Any(s => lower.Contains(s));
        }

        public static string BuildPromptBlock(DateTimeContext context)
        {
            if (context == null || !context.IsAvailable)
            {
                return "RUNTIME CONTEXT:\n" +
                       "Current Date: UNKNOWN\n" +
                       "Current Day: UNKNOWN\n" +
                       "Current Time: UNKNOWN\n" +
                       "Tarih/gün/saat sorularında tahmin etme; 'Bunu doğrulayamıyorum baba.' de.";
            }

            return "RUNTIME CONTEXT (güvenilir sistem saati — tarih/gün/saat sorularında BUNU kullan):\n" +
                   "Current Date: " + context.CurrentDate + "\n" +
                   "Current Day: " + context.CurrentDay + "\n" +
                   "Current Time: " + context.CurrentTime + "\n" +
                   "Time Zone: " + context.TimeZoneId + "\n" +
                   "Kural: Gün veya tarih uydurma. Bu bloktaki değerler dışında gün/tarih söyleme.";
        }

        public static string BuildAnswerDirective(DateTimeContext context)
        {
            if (context == null || !context.IsAvailable)
            {
                return "TARİH/SAAT MODU: Sistem saati yok. Tahmin etme; 'Bunu doğrulayamıyorum baba.' de.";
            }

            return "TARİH/SAAT MODU: Kullanıcı gün/tarih/saat soruyor. Yalnızca RUNTIME CONTEXT değerlerini " +
                   "kullan (Current Day=" + context.CurrentDay + ", Current Date=" + context.CurrentDate +
                   "). Rastgele gün söyleme; Pazartesi/Salı vb. uydurma.";
        }

        public static string TryBuildDirectReply(string message, DateTimeContext context, string ownerName)
        {
            if (!IsDateTimeQuestion(message))
                return null;

            var hitap = string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();
            if (context == null || !context.IsAvailable)
                return "Bunu doğrulayamıyorum " + hitap + ".";

            var lower = message.Trim().ToLowerInvariant();

            if (ContainsAny(lower, "saat kaç", "saat ne", "kaç saat", "zaman ne"))
                return "Saat " + context.CurrentTime + " " + hitap + ".";

            if (ContainsAny(lower, "tarih", "ayın kaçı", "ayin kaci", "hangi tarih", "gün ay"))
                return "Bugün " + context.CurrentDate + " " + hitap + ".";

            if (ContainsAny(lower, "hangi gün", "günlerden", "ne gün", "bugün ne", "bugün hangi", "pazar mı",
                    "pazartesi mi", "salı mı", "sali mi", "çarşamba mı", "carsamba mi", "perşembe mi",
                    "persembe mi", "cuma mı", "cumartesi mi", "gün mü"))
                return "Bugün günlerden " + context.CurrentDay + " " + hitap + ".";

            return "Bugün " + context.CurrentDate + ", günlerden " + context.CurrentDay + " " + hitap + ".";
        }

        public static bool ReplyMatchesContext(string reply, DateTimeContext context)
        {
            if (string.IsNullOrWhiteSpace(reply) || context == null || !context.IsAvailable)
                return false;

            var lower = reply.ToLowerInvariant();
            return lower.Contains(context.CurrentDay.ToLowerInvariant()) ||
                   lower.Contains(context.CurrentDate.ToLowerInvariant());
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
