using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;
using Koca_Kafa.Services.Cognitive;

namespace Koca_Kafa.Services
{
    /// <summary>
    /// Kullanıcı mesajlarından kalıcı hafıza adayları çıkarır.
    /// Geçici olayları (bugün markete gittim) filtreler; kalıcı bilgileri (severim, yaşıyorum) kaydeder.
    /// </summary>
    public sealed class MemoryExtractorService : IMemoryExtractorService
    {
        private static readonly string[] TransientTimeMarkers =
        {
            "bugün", "dün", "yarın", "şimdi", "az önce", "bu sabah", "bu akşam", "bu gece",
            "geçen gün", "geçen hafta", "geçen ay", "geçen yıl", "biraz önce", "henüz", "az sonra"
        };

        private static readonly string[] EphemeralVerbs =
        {
            "gittim", "geldim", "yaptım", "aldım", "sattım", "gördüm", "konuştum", "yedim",
            "içtim", "uyudum", "kalktım", "açtım", "kapattım", "başladım", "bitirdim", "izledim",
            "okudum", "yazdım", "aradım", "buldum", "kaybettim", "unuttum", "hatırladım"
        };

        private static readonly Regex NamePattern =
            new Regex(@"(?:benim\s+)?ad[ıi]m\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NicknamePattern =
            new Regex(@"bana\s+(.+?)\s+de(?:sene)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ResidencePattern =
            new Regex(@"(.+?)['’]?(?:de|da)\s+ya[sş][ıi]yorum", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PreferencePattern =
            new Regex(@"(.+?)\s+(?:çok\s+)?(?:severim|seviyorum|sevmem|ho[sş]lan[ıi]r[ıi]m|ho[sş]lanmam)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PreferenceReversePattern =
            new Regex(@"(?:en\s+)?sevdi[gğ]im\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProfessionPattern =
            new Regex(@"ben\s+(?:bir\s+)?(.+?)(?:yim|y[ıi]m|um|üm)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WorkPattern =
            new Regex(@"ben\s+(.+?)\s+(?:olarak\s+)?çal[ıi][sş][ıi]yorum", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex GoalPattern =
            new Regex(@"(?:hedefim|hayalim|ama[cç][ıi]m)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProjectPattern =
            new Regex(@"(?:projem|üzerinde\s+çal[ıi][sş]t[ıi][gğ][ıi]m)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RememberPattern =
            new Regex(@"hat[ıi]rla[,:]?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CommunicationStylePattern =
            new Regex(@"(?:bana\s+)?(?:her\s+zaman\s+)?(.+?)\s+konu[sş](?:ur|sun|al)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IList<ExtractedMemory> Extract(string message, IReadOnlyList<ChatMessage> recentHistory = null)
        {
            var results = new List<ExtractedMemory>();
            if (string.IsNullOrWhiteSpace(message))
                return results;

            var entityResults = EntityExtractor.Extract(message, recentHistory);
            if (entityResults.Count > 0)
            {
                results.AddRange(entityResults);
                if (entityResults.Any(e => !string.IsNullOrWhiteSpace(e.EntityKey)))
                    return results;
            }

            var text = message.Trim();
            var lower = text.ToLowerInvariant();

            if (IsTransientEvent(text, lower))
                return results;

            if (EntityExtractor.ContainsPetOrEntitySignals(text))
            {
                TryAdd(results, TryMatchGoal(text));
                TryAdd(results, TryMatchRemember(text));
                return results;
            }

            TryAdd(results, TryMatchName(text));
            TryAdd(results, TryMatchNickname(text));
            TryAdd(results, TryMatchResidence(text));
            TryAdd(results, TryMatchPreference(text));
            TryAdd(results, TryMatchPreferenceReverse(text));
            TryAdd(results, TryMatchProfession(text));
            TryAdd(results, TryMatchWork(text));
            TryAdd(results, TryMatchGoal(text));
            TryAdd(results, TryMatchProject(text));
            TryAdd(results, TryMatchRemember(text));
            TryAdd(results, TryMatchCommunicationStyle(text));

            if (results.Count == 0 && IsPersistentPersonalStatement(text, lower))
            {
                results.Add(new ExtractedMemory
                {
                    ShouldSave = true,
                    Topic = "Kişisel",
                    Content = "Kullanıcı hakkında: " + text,
                    Importance = CalculateImportance("Kişisel", text, lower, hasExplicitRemember: false)
                });
            }

            return results;
        }

        private static bool IsTransientEvent(string text, string lower)
        {
            var hasTimeMarker = ContainsAny(lower, TransientTimeMarkers);
            var hasEphemeralVerb = ContainsAny(lower, EphemeralVerbs);

            if (hasTimeMarker && hasEphemeralVerb)
                return true;

            if (hasTimeMarker && Regex.IsMatch(lower, @"\b(gittim|geldim|yaptım|aldım|gördüm|yedim)\b"))
                return true;

            if (Regex.IsMatch(lower, @"^(?:ben\s+)?(?:markete|okula|işe|hastaneye|parka|eve|dışarı)\s+gittim"))
                return true;

            return false;
        }

        private static bool IsPersistentPersonalStatement(string text, string lower)
        {
            if (text.Length < 10)
                return false;

            if (ContainsAny(lower, TransientTimeMarkers))
                return false;

            return lower.StartsWith("ben ") || lower.StartsWith("benim ");
        }

        private static ExtractedMemory TryMatchName(string text)
        {
            var match = NamePattern.Match(text);
            if (!match.Success)
                return null;

            var name = Clean(match.Groups[1].Value);
            return Create("İsim", "Kullanıcının adı: " + name, "İsim", text, explicitRemember: false, baseScore: 95);
        }

        private static ExtractedMemory TryMatchNickname(string text)
        {
            var match = NicknamePattern.Match(text);
            if (!match.Success)
                return null;

            var nickname = Clean(match.Groups[1].Value);
            return Create("Hitap", "Kullanıcıya şöyle hitap et: " + nickname, "Hitap", text, false, 90);
        }

        private static ExtractedMemory TryMatchResidence(string text)
        {
            var match = ResidencePattern.Match(text);
            if (!match.Success)
                return null;

            var place = Clean(match.Groups[1].Value);
            return Create("Konum", "Kullanıcı " + place + "'de yaşıyor", "Konum", text, false, 85);
        }

        private static ExtractedMemory TryMatchPreference(string text)
        {
            var match = PreferencePattern.Match(text);
            if (!match.Success)
                return null;

            var subject = Clean(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(subject))
                return null;

            var lower = text.ToLowerInvariant();
            var dislikes = lower.Contains("sevmem") || lower.Contains("hoşlanmam");
            var content = dislikes
                ? "Kullanıcı " + subject + " sevmiyor"
                : "Kullanıcı " + subject + " seviyor";

            return Create("İlgi Alanı", content, "İlgi Alanı", text, false, 75);
        }

        private static ExtractedMemory TryMatchPreferenceReverse(string text)
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains("kedimin") || lower.Contains("köpeğimin") || lower.Contains("kopegimin") || lower.Contains("renk"))
                return null;

            var match = PreferenceReversePattern.Match(text);
            if (!match.Success)
                return null;

            var subject = Clean(match.Groups[1].Value);
            return Create("İlgi Alanı", "Kullanıcının favorisi: " + subject, "İlgi Alanı", text, false, 72);
        }

        private static ExtractedMemory TryMatchProfession(string text)
        {
            var match = ProfessionPattern.Match(text);
            if (!match.Success)
                return null;

            var role = Clean(match.Groups[1].Value);
            return Create("Meslek", "Kullanıcının mesleği/rolü: " + role, "Meslek", text, false, 80);
        }

        private static ExtractedMemory TryMatchWork(string text)
        {
            var match = WorkPattern.Match(text);
            if (!match.Success)
                return null;

            var work = Clean(match.Groups[1].Value);
            return Create("Meslek", "Kullanıcı şu işi yapıyor: " + work, "Meslek", text, false, 78);
        }

        private static ExtractedMemory TryMatchGoal(string text)
        {
            var match = GoalPattern.Match(text);
            if (!match.Success)
                return null;

            var goal = Clean(match.Groups[1].Value);
            return Create("Hedef", "Kullanıcının hedefi: " + goal, "Hedef", text, false, 82);
        }

        private static ExtractedMemory TryMatchProject(string text)
        {
            var match = ProjectPattern.Match(text);
            if (!match.Success)
                return null;

            var project = Clean(match.Groups[1].Value);
            return Create("Proje", "Kullanıcının projesi: " + project, "Proje", text, false, 76);
        }

        private static ExtractedMemory TryMatchRemember(string text)
        {
            var match = RememberPattern.Match(text);
            if (!match.Success)
                return null;

            var note = Clean(match.Groups[1].Value);
            return Create("Hatırlatma", "Kullanıcı bunu hatırlamamı istedi: " + note, "Hatırlatma", text, true, 92);
        }

        private static ExtractedMemory TryMatchCommunicationStyle(string text)
        {
            var match = CommunicationStylePattern.Match(text);
            if (!match.Success)
                return null;

            var style = Clean(match.Groups[1].Value);
            return Create("İletişim", "Kullanıcı " + style + " konuşulmasını istiyor", "İletişim", text, false, 70);
        }

        private static ExtractedMemory Create(
            string topic,
            string content,
            string category,
            string originalText,
            bool explicitRemember,
            int baseScore)
        {
            var lower = originalText.ToLowerInvariant();
            return new ExtractedMemory
            {
                ShouldSave = true,
                Topic = topic,
                Content = content,
                Importance = CalculateImportance(category, originalText, lower, explicitRemember, baseScore)
            };
        }

        internal static int CalculateImportance(
            string category,
            string text,
            string lower,
            bool hasExplicitRemember,
            int? baseOverride = null)
        {
            var score = baseOverride ?? GetCategoryBaseScore(category);

            if (hasExplicitRemember || lower.Contains("hatırla") || lower.Contains("unutma"))
                score += 10;

            if (lower.Contains("her zaman") || lower.Contains("asla") || lower.Contains("kesinlikle"))
                score += 5;

            if (lower.Contains("belki") || lower.Contains("sanırım") || lower.Contains("galiba") || lower.Contains("olabilir"))
                score -= 15;

            if (text.TrimEnd().EndsWith("?"))
                score -= 20;

            if (lower.StartsWith("ben ") || lower.StartsWith("benim "))
                score += 3;

            return BoundImportance(score);
        }

        private static int GetCategoryBaseScore(string category)
        {
            switch (category)
            {
                case "İsim": return 95;
                case "Hitap": return 90;
                case "Hatırlatma": return 92;
                case "Konum": return 85;
                case "Hedef": return 82;
                case "Meslek": return 80;
                case "Proje": return 76;
                case "İlgi Alanı": return 75;
                case "İletişim": return 70;
                case "Kişisel": return 55;
                default: return 60;
            }
        }

        private static int BoundImportance(int score)
        {
            if (score < 1) return 1;
            if (score > 100) return 100;
            return score;
        }

        private static void TryAdd(ICollection<ExtractedMemory> results, ExtractedMemory item)
        {
            if (item != null && item.ShouldSave)
                results.Add(item);
        }

        private static bool ContainsAny(string text, IEnumerable<string> markers)
        {
            foreach (var marker in markers)
            {
                if (text.Contains(marker))
                    return true;
            }
            return false;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .TrimEnd('.', '!', '?', ',', ';', ':');
        }
    }
}
