using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class MemoryRecallHelper
    {
        private static readonly Regex NameContentPattern =
            new Regex(@"(?:ad[ıi]m(?:\s+|:)?|ad[ıi]:\s*)([A-Za-zÇçĞğİıÖöŞşÜü]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ResidencePattern =
            new Regex(@"([A-Za-zÇçĞğİıÖöŞşÜü]+)['']?(?:de|da)\s+ya[sş][ıi]yorum", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool ContainsProfileSignals(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return ContainsAny(lower,
                "adım", "adim", "ismim", "benim ad", "yaşıyorum", "yasiyorum",
                "severim", "seviyorum", "sevdiğim", "sevdigim", "hatırla", "hatirla",
                "kaydet", "profil", "doğum", "dogum", "memleket", "şehir", "sehir",
                "kedimin", "kedim", "köpeğimin", "kopegimin", "yavru", "baba deme",
                "öğrenmek istiyorum", "ogrenmek istiyorum", "renk")
                || EntityExtractor.ContainsPetOrEntitySignals(message);
        }

        public static bool IsRecallQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return ContainsAny(lower,
                "hatırlıyor musun", "hatirliyor musun", "hatırla", "hatirla",
                "adım ne", "adim ne", "ismim ne", "neydi", "ne biliyorsun",
                "hangi şehir", "hangi sehir", "nerede yaşıyorum", "nerede yasiyorum",
                "daha önce", "daha once", "söylediğim", "soyledigim",
                "kedimin adı", "kedimin adi", "kedim adı", "yavruların adı", "yavrularin adi",
                "yavruların isimleri", "yavrularin isimleri", "yavruların ismi", "yavrularin ismi",
                "hedefim neydi", "hedefim ne",
                "en sevdiğim renk", "en sevdigim renk", "sevdiğim renk ne", "sevdigim renk ne",
                "favori renk ne", "favori yemek ne");
        }

        public static bool IsMemoryQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            if (IsCatNameRecallQuery(lower) || IsKittenNameRecallQuery(lower) ||
                IsGoalRecallQuery(lower) || IsUserNameRecallQuery(lower) ||
                IsCityRecallQuery(lower) || IsColorRecallQuery(lower))
                return true;

            if (!IsRecallQuery(message))
                return false;

            return ContainsQuestionSignals(lower);
        }

        public static bool ContainsQuestionSignals(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            if (lower.Contains("?"))
                return true;

            if (lower.EndsWith(" ne", StringComparison.Ordinal) ||
                lower.EndsWith(" ne?", StringComparison.Ordinal) ||
                lower.EndsWith(" mi", StringComparison.Ordinal) ||
                lower.EndsWith(" mı", StringComparison.Ordinal) ||
                lower.EndsWith(" mu", StringComparison.Ordinal) ||
                lower.EndsWith(" mü", StringComparison.Ordinal))
                return true;

            return ContainsAny(lower,
                " neydi", " ne ", " nedir", " ne?", " ne,", " ne.",
                " mi ", " mı ", " mu ", " mü ",
                " musun", " misin", " mısın", " misiniz",
                " hangi", " ne biliyorsun", " hatırlıyor musun", " hatirliyor musun",
                "what is", "what's", "what was", "do you remember");
        }

        public static bool IsKittenNameRecall(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return IsKittenNameRecallQuery(lower);
        }

        public static bool IsCatNameRecall(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return IsCatNameRecallQuery(message.Trim().ToLowerInvariant());
        }

        public static bool IsGoalRecall(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return IsGoalRecallQuery(message.Trim().ToLowerInvariant());
        }

        public static IList<string> ExtractKittenNamesFromReply(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var parts = text
                .Replace(" ve ", ",")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => CleanNameToken(p))
                .Where(KittenNameGuard.IsValidKittenName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts;
        }

        public static string FormatKittenNameList(IList<string> names) =>
            FormatNameList(string.Join(", ", names)) + ".";

        public static string ExtractSingleTokenAnswer(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim().TrimEnd('.', '!', '?');
            var first = trimmed.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(first) ? null : CleanNameToken(first);
        }

        private static string CleanNameToken(string value)
        {
            var trimmed = (value ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',', ';', ':');
            if (trimmed.StartsWith("ve ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(3).Trim();
            return trimmed;
        }

        public static bool IsDogNameRecall(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return IsDogNameRecallQuery(message.Trim().ToLowerInvariant());
        }

        public static string TryBuildDirectRecallReply(string userMessage, string memoryContext, string ownerName)
        {
            if (string.IsNullOrWhiteSpace(memoryContext) || string.IsNullOrWhiteSpace(userMessage))
                return null;

            var hitap = UserPreferenceResolver.ResolveHitap(ownerName, memoryContext);
            var lower = userMessage.Trim().ToLowerInvariant();
            var lines = ParseMemoryLines(memoryContext);

            if (IsCatNameRecallQuery(lower))
            {
                var cat = ExtractEntityValue(lines, EntityKeys.CatName);
                if (!string.IsNullOrWhiteSpace(cat))
                    return FormatWithHitap(cat + ".", hitap);
            }

            if (IsDogNameRecallQuery(lower))
            {
                var dog = ExtractEntityValue(lines, EntityKeys.DogName);
                if (!string.IsNullOrWhiteSpace(dog))
                    return FormatWithHitap(dog + ".", hitap);
            }

            if (IsKittenNameRecallQuery(lower))
            {
                var kittens = ExtractEntityValue(lines, EntityKeys.KittenNames);
                var names = FilterKittenNames(kittens);
                if (names.Count > 0)
                    return FormatKittenNameList(names);
            }

            if (IsGoalRecallQuery(lower))
            {
                var goal = ExtractEntityValue(lines, EntityKeys.ActiveGoal);
                if (!string.IsNullOrWhiteSpace(goal))
                    return FormatWithHitap(Capitalize(goal) + " istemiştin.", hitap);
            }

            if (IsUserNameRecallQuery(lower))
            {
                var name = ExtractBestName(lines) ?? ExtractEntityValue(lines, EntityKeys.PreferredName);
                if (!string.IsNullOrWhiteSpace(name))
                    return FormatWithHitap("Adın " + name + ".", hitap);
            }

            if (IsCityRecallQuery(lower))
            {
                var city = ExtractBestCity(lines);
                if (!string.IsNullOrWhiteSpace(city))
                    return FormatWithHitap(city + "'de yaşıyorsun.", hitap);
            }

            if (IsColorRecallQuery(lower))
            {
                var color = ExtractEntityValue(lines, EntityKeys.FavoriteColor) ?? ExtractBestColor(lines);
                if (!string.IsNullOrWhiteSpace(color))
                    return FormatWithHitap("En sevdiğin renk " + color + ".", hitap);
            }

            if (IsSmallTalkWithoutBaba(lower, memoryContext))
            {
                return "İyiyim, teşekkür ederim.";
            }

            return null;
        }

        public static IList<string> ParseMemoryLines(string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(memoryContext))
                return new List<string>();

            return memoryContext
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- [", StringComparison.Ordinal))
                .ToList();
        }

        private static bool IsDogNameRecallQuery(string lower) =>
            (ContainsAny(lower, "köpeğimin adı", "kopegimin adi", "köpeğim adı", "kopegim adi", "köpek adı", "kopek adi") &&
             ContainsQuestionSignals(lower)) ||
            (ContainsAny(lower, "neydi", "neydi?") && ContainsAny(lower, "köpek", "kopek", "köpeğim", "kopegim", "köpeğimin", "kopegimin")) ||
            (ContainsAny(lower, "dog", "dog's", "dogs") && lower.Contains("name") && ContainsQuestionSignals(lower));

        private static bool IsCatNameRecallQuery(string lower) =>
            (ContainsAny(lower, "kedimin adı", "kedimin adi", "kedim adı", "kedim adi", "kedi adı", "kedi adi") &&
             ContainsQuestionSignals(lower)) ||
            (ContainsAny(lower, "neydi", "neydi?") && ContainsAny(lower, "kedi", "kedim", "kedimin")) ||
            (ContainsAny(lower, "cat", "cat's", "cats") && lower.Contains("name") && ContainsQuestionSignals(lower));

        private static bool IsKittenNameRecallQuery(string lower) =>
            (ContainsAny(lower, "yavruların adı", "yavrularin adi", "yavruların ismi", "yavrularin ismi",
                "yavruların isimleri", "yavrularin isimleri", "yavru adı", "yavru adi") &&
             ContainsQuestionSignals(lower)) ||
            (ContainsAny(lower, "neydi") && lower.Contains("yavru"));

        private static bool IsGoalRecallQuery(string lower) =>
            ContainsAny(lower, "hedefim neydi", "hedefim ne", "hedef neydi");

        private static bool IsUserNameRecallQuery(string lower) =>
            ContainsAny(lower, "adım ne", "adim ne", "ismim ne", "adım neydi", "adim neydi", "ismim neydi") ||
            (ContainsAny(lower, "neydi", "hatırlıyor musun", "hatirliyor musun") &&
             ContainsAny(lower, " adım", " adim", " ismim", "benim ad"));

        private static bool IsCityRecallQuery(string lower) =>
            ContainsAny(lower, "hangi şehir", "hangi sehir", "nerede yaşıyorum", "nerede yasiyorum", "şehirde", "sehirde") ||
            (ContainsAny(lower, "hatırlıyor musun", "hatirliyor musun", "neydi") &&
             ContainsAny(lower, "şehir", "sehir", "yaşad", "yasad"));

        private static bool IsColorRecallQuery(string lower) =>
            ContainsAny(lower, "sevdiğim renk ne", "sevdigim renk ne", "favori renk ne") ||
            (ContainsAny(lower, "renk", "sevdiğim renk", "favori renk") && ContainsQuestionSignals(lower)) ||
            (lower.Contains("favorite color") && ContainsQuestionSignals(lower)) ||
            (lower.Contains("favourite color") && ContainsQuestionSignals(lower));

        private static bool IsSmallTalkWithoutBaba(string lower, string memoryContext) =>
            UserPreferenceResolver.ShouldAvoidBaba(memoryContext) &&
            ContainsAny(lower, "nasılsın", "nasilsin", "naber", "ne haber");

        private static string ExtractEntityValue(IList<string> lines, string entityKey)
        {
            var marker = "[Entity:" + entityKey + "]";
            foreach (var line in lines)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var afterBracket = line.IndexOf(']');
                if (afterBracket < 0 || afterBracket + 1 >= line.Length)
                    continue;

                return line.Substring(afterBracket + 1).Trim().TrimStart(':').Trim();
            }

            return null;
        }

        private static string ExtractBestName(IList<string> lines)
        {
            foreach (var line in lines)
            {
                if (line.IndexOf("[İsim]", StringComparison.OrdinalIgnoreCase) < 0 &&
                    line.IndexOf("[Isim]", StringComparison.OrdinalIgnoreCase) < 0 &&
                    line.IndexOf("[Entity:preferred_name]", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var match = NameContentPattern.Match(line);
                if (match.Success)
                    return Capitalize(match.Groups[1].Value);

                var afterBracket = line.IndexOf(']');
                if (afterBracket >= 0 && afterBracket + 1 < line.Length)
                {
                    var tail = line.Substring(afterBracket + 1).Trim().TrimStart(':').Trim();
                    if (tail.Length >= 2)
                        return Capitalize(tail.Split(' ', '.', ',')[0]);
                }
            }

            return null;
        }

        private static string ExtractBestCity(IList<string> lines)
        {
            foreach (var line in lines)
            {
                var match = ResidencePattern.Match(line);
                if (match.Success)
                    return Capitalize(match.Groups[1].Value);
            }

            return null;
        }

        private static string ExtractBestColor(IList<string> lines)
        {
            foreach (var line in lines)
            {
                if (line.IndexOf("renk", StringComparison.OrdinalIgnoreCase) < 0 &&
                    line.IndexOf(EntityKeys.FavoriteColor, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var lower = line.ToLowerInvariant();
                foreach (var color in new[] { "mavi", "kırmızı", "kirmizi", "yeşil", "yesil", "sarı", "sari", "mor", "turuncu", "siyah", "beyaz" })
                {
                    if (lower.Contains(color))
                        return Capitalize(color);
                }
            }

            return null;
        }

        private static IList<string> FilterKittenNames(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new List<string>();

            return csv
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(KittenNameGuard.IsValidKittenName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FormatNameList(string csv)
        {
            var names = FilterKittenNames(csv);

            if (names.Count == 0)
                return csv;

            if (names.Count == 1)
                return names[0];

            if (names.Count == 2)
                return names[0] + " ve " + names[1];

            return string.Join(", ", names.Take(names.Count - 1)) + " ve " + names[names.Count - 1];
        }

        private static string FormatWithHitap(string text, string hitap)
        {
            if (string.IsNullOrWhiteSpace(hitap))
                return text.Trim();

            return text.Trim().TrimEnd('.') + " " + hitap + ".";
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var trimmed = value.Trim();
            if (trimmed.Length == 1)
                return trimmed.ToUpperInvariant();

            return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1).ToLowerInvariant();
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
