using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class CreativeTaskEngine
    {
        private static readonly string[] TranslationSignals =
        {
            "çevir", "cevir", "translate", "translation", "tercüme", "tercume",
            "ingilizceye", "ingilizce", "english", "ingilice",
            "rusçaya", "rusca", "rusça", "russian",
            "almancaya", "almanca", "fransızca", "fransizca", "ispanyolca",
            "profesyonelce çevir", "profesyonelce cevir", "başka bir dile", "baska bir dile",
            "başka dile", "baska dile", "dile çevir", "dile cevir"
        };

        private static readonly string[] LanguageSwitchSignals =
        {
            "ingilizce olsun", "ingilice olsun", "english please", "in english",
            "rusca yaz", "rusça yaz", "russian please",
            "türkçe yap", "turkce yap", "türkçe olsun", "turkce olsun",
            "almanca yaz", "fransızca yaz", "fransizca yaz"
        };

        private static readonly string[] RewriteSignals =
        {
            "kibar", "kurumsal", "resmi", "profesyonel",
            "düzenle", "duzenle", "düzelt", "duzelt", "iyileştir", "iyilestir",
            "yazım", "yazim", "imla", "gramer", "grammar",
            "daha kibar", "daha kurumsal", "hale getir", "yeniden yaz", "polish"
        };

        private static readonly string[] TemplateSignals =
        {
            "dilekçe", "dilekce", "ön yazı", "on yazi", "önyazı", "onyazi",
            "e-posta", "eposta", "e posta", "mesaj taslağı", "mesaj taslaği",
            "istifa", "başvuru", "basvuru", "cover letter", "motivasyon mektubu",
            "taslak oluştur", "taslak olustur", "taslağı", "taslagi"
        };

        private static readonly string[] SimplifySignals =
        {
            "5 yaşındaki", "5 yasindaki", "beş yaşındaki", "bes yasindaki",
            "çocuğa anlat", "cocuga anlat", "basitçe anlat", "basitce anlat",
            "basit anlat", "layman", "sade anlat", "kolay anlat"
        };

        public static CreativeTaskKind Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return CreativeTaskKind.None;

            var lower = message.Trim().ToLowerInvariant();

            if (ContainsAny(lower, LanguageSwitchSignals) || IsShortLanguageCommand(lower))
                return CreativeTaskKind.LanguageSwitch;

            if (ContainsAny(lower, TranslationSignals))
                return CreativeTaskKind.Translation;

            if (ContainsAny(lower, SimplifySignals))
                return CreativeTaskKind.Simplify;

            if (ContainsAny(lower, RewriteSignals))
                return CreativeTaskKind.Rewrite;

            if (ContainsAny(lower, TemplateSignals))
                return CreativeTaskKind.Template;

            return CreativeTaskKind.None;
        }

        public static bool IsCreativeTask(string message) =>
            Classify(message) != CreativeTaskKind.None;

        public static bool ShouldDisableEmpathy(string message) =>
            IsCreativeTask(message);

        public static bool ShouldSuppressFollowUp(string message) =>
            IsCreativeTask(message);

        public static string ExtractTargetLanguage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var lower = message.Trim().ToLowerInvariant();

            if (ContainsAny(lower, "ingilizceye", "ingilizce", "ingilice", "english", "in english"))
                return "English";
            if (ContainsAny(lower, "rusça", "rusca", "russian"))
                return "Russian";
            if (ContainsAny(lower, "türkçe", "turkce", "turkish"))
                return "Turkish";
            if (ContainsAny(lower, "almanca", "german"))
                return "German";
            if (ContainsAny(lower, "fransızca", "fransizca", "french"))
                return "French";
            if (ContainsAny(lower, "ispanyolca", "spanish"))
                return "Spanish";

            return null;
        }

        public static string BuildPromptDirective(
            CreativeTaskKind kind,
            string message,
            IReadOnlyList<ChatMessage> history = null)
        {
            if (kind == CreativeTaskKind.None)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("YARATICI GÖREV MODU (bu turda persona/sohbet kuralları geçersiz):");
            sb.AppendLine("- Empati açılışı yok ('Anlıyorum', 'Üzüldüm' vb.).");
            sb.AppendLine("- Selam/tekrar sorma yok ('Lütfen belirtir misiniz', 'Size nasıl yardımcı olabilirim' yasak).");
            sb.AppendLine("- Takip sorusu sorma.");
            sb.AppendLine("- Yanıt SADECE istenen çıktı; meta açıklama minimum.");

            var contextHint = BuildHistoryHint(kind, message, history);
            if (!string.IsNullOrWhiteSpace(contextHint))
            {
                sb.AppendLine();
                sb.AppendLine(contextHint);
            }

            switch (kind)
            {
                case CreativeTaskKind.Translation:
                    sb.AppendLine();
                    sb.AppendLine("ÇEVİRİ: Kullanıcının metnini hedef dile profesyonelce çevir.");
                    sb.AppendLine("Sadece çeviriyi yaz; kaynak dilde açıklama ekleme.");
                    AppendTargetLanguage(sb, message);
                    break;

                case CreativeTaskKind.LanguageSwitch:
                    sb.AppendLine();
                    sb.AppendLine("DİL DEĞİŞTİR: Konuşma geçmişindeki son ilgili metni belirtilen dile çevir/yaz.");
                    sb.AppendLine("Kullanıcı kısa komut verdiyse (ör. 'ingilizce olsun') son mesajdaki metni kullan.");
                    sb.AppendLine("Tek dil kullan; karışık dil (Türkçe+Rusça+Çince) yasak.");
                    AppendTargetLanguage(sb, message);
                    break;

                case CreativeTaskKind.Rewrite:
                    sb.AppendLine();
                    sb.AppendLine("METİN DÜZENLEME: Metni istenen tonda yeniden yaz (kibar/kurumsal/resmi).");
                    sb.AppendLine("'kibirli' değil 'kibar' — nazik ve profesyonel ol.");
                    sb.AppendLine("Sadece düzenlenmiş metni ver; not listesi veya talimat verme.");
                    break;

                case CreativeTaskKind.Template:
                    sb.AppendLine();
                    sb.AppendLine("ŞABLON: İstenen belge taslağını (dilekçe, ön yazı, e-posta) oluştur.");
                    sb.AppendLine("Kurumsal Türkçe format; [Ad] gibi yer tutucular kullan.");
                    sb.AppendLine("Almanca/Özbekçe/Rusça karışımı yasak — yalnızca Türkçe (aksi istenmedikçe).");
                    break;

                case CreativeTaskKind.Simplify:
                    sb.AppendLine();
                    sb.AppendLine("BASİT ANLATIM: Konuyu 5 yaşındaki bir çocuğa anlatır gibi açıkla.");
                    sb.AppendLine("Kullanıcının sorduğu konuyu değiştirme (yemek tarifi, başka konu uydurma yasak).");
                    sb.AppendLine("Kısa, somut, günlük dil.");
                    break;
            }

            return sb.ToString().Trim();
        }

        public static bool ContainsEmpathyOpener(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return lower.Contains("anlıyorum") ||
                   lower.Contains("anliyorum") ||
                   lower.Contains("üzüldüm") ||
                   lower.Contains("uzuldum") ||
                   lower.Contains("lütfen belirtir") ||
                   lower.Contains("lutfen belirtir") ||
                   lower.Contains("nasıl yardımcı olabilirim") ||
                   lower.Contains("nasil yardimci olabilirim");
        }

        public static bool ContainsMixedScript(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var hasCyrillic = reply.Any(c => c >= '\u0400' && c <= '\u04FF');
            var hasCjk = reply.Any(c => c >= '\u4E00' && c <= '\u9FFF');
            var hasLatin = reply.Any(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                                           (c >= '\u00C0' && c <= '\u024F'));

            var scriptCount = (hasCyrillic ? 1 : 0) + (hasCjk ? 1 : 0) + (hasLatin ? 1 : 0);
            return scriptCount >= 2;
        }

        private static string BuildHistoryHint(
            CreativeTaskKind kind,
            string message,
            IReadOnlyList<ChatMessage> history)
        {
            if (history == null || history.Count == 0)
                return null;

            if (kind != CreativeTaskKind.LanguageSwitch &&
                kind != CreativeTaskKind.Translation &&
                kind != CreativeTaskKind.Rewrite)
                return null;

            var lastUser = history
                .Where(m => m != null && m.Role == ChatRole.User && !string.IsNullOrWhiteSpace(m.Content))
                .LastOrDefault();

            var lastAssistant = history
                .Where(m => m != null && m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Content))
                .LastOrDefault();

            var sb = new StringBuilder();
            if (lastUser != null &&
                !string.Equals(lastUser.Content.Trim(), message.Trim(), StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("Son kullanıcı metni: " + Truncate(lastUser.Content, 400));

            if ((kind == CreativeTaskKind.LanguageSwitch || kind == CreativeTaskKind.Rewrite) &&
                lastAssistant != null)
                sb.AppendLine("Son asistan çıktısı: " + Truncate(lastAssistant.Content, 400));

            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static void AppendTargetLanguage(StringBuilder sb, string message)
        {
            var lang = ExtractTargetLanguage(message);
            if (!string.IsNullOrWhiteSpace(lang))
                sb.AppendLine("Hedef dil: " + lang + " — yanıt yalnızca bu dilde.");
        }

        private static bool IsShortLanguageCommand(string lower)
        {
            if (lower.Length > 40)
                return false;

            return (lower.Contains("ingiliz") || lower.Contains("english") ||
                    lower.Contains("rus") || lower.Contains("türk") || lower.Contains("turk") ||
                    lower.Contains("almanca") || lower.Contains("frans")) &&
                   (lower.Contains("olsun") || lower.Contains("yaz") || lower.Contains("yap") ||
                    lower.Contains("please") || lower.Length <= 20);
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrWhiteSpace(value) || value.Length <= max
                ? value
                : value.Substring(0, max) + "...";

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
