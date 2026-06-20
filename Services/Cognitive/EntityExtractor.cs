using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class EntityExtractor
    {
        private static readonly Regex CatNamePattern = new Regex(
            @"(?:benim\s+)?ked(?:im|imin|imin)?(?:in)?\s+(?:ad[ıi]|ismi)\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DogNamePattern = new Regex(
            @"(?:benim\s+)?köpe(?:ğim|gim|ğimin|gimin)?(?:in)?\s+(?:ad[ıi]|ismi)\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FavoriteColorPattern = new Regex(
            @"(?:en\s+sevdi[gğ]im\s+renk\s+(.+)|(.+?)\s+rengi\s+severim|(.+?)\s+renk\s+severim)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FavoriteFoodPattern = new Regex(
            @"(?:en\s+sevdi[gğ]im\s+(?:yemek|yiyecek)\s+(.+)|(.+?)\s+(?:yeme[gğ]i|yiyeceği)\s+severim)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AvoidBabaPattern = new Regex(
            @"bana\s+baba\s+deme",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ActiveGoalPattern = new Regex(
            @"(.+?)\s+öğrenmek\s+istiyorum",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HasKittensPattern = new Regex(
            @"(.+?)['’]?(?:un|ın|in)?\s+yavrular[ıi]\s+var",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex KittenCountPattern = new Regex(
            @"^(\d+)\s+tane\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] KnownColors =
        {
            "mavi", "kırmızı", "kirmizi", "yeşil", "yesil", "sarı", "sari",
            "mor", "turuncu", "siyah", "beyaz", "pembe", "gri"
        };

        public static IList<ExtractedMemory> Extract(string message, IReadOnlyList<ChatMessage> recentHistory = null)
        {
            var results = new List<ExtractedMemory>();
            if (string.IsNullOrWhiteSpace(message))
                return results;

            var text = message.Trim();
            var lower = text.ToLowerInvariant();
            var context = ConversationMemoryContextBuilder.Analyze(recentHistory);

            TryAdd(results, TryCatName(text));
            TryAdd(results, TryDogName(text));
            TryAdd(results, TryFavoriteColor(text, lower));
            TryAdd(results, TryFavoriteFood(text, lower));
            TryAdd(results, TryAvoidBaba(text));
            TryAdd(results, TryActiveGoal(text));
            TryAddMany(results, TryHasKittens(text, context));
            TryAdd(results, TryKittenCount(text, context));
            TryAdd(results, TryKittenName(text, context));
            TryAdd(results, TryPreferredName(text));

            return results;
        }

        public static bool ContainsPetOrEntitySignals(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var lower = message.Trim().ToLowerInvariant();
            return lower.Contains("kedimin") || lower.Contains("kedim") || lower.Contains("köpeğimin") ||
                   lower.Contains("kopegimin") || lower.Contains("yavru") || lower.Contains("kitten") ||
                   AvoidBabaPattern.IsMatch(lower) || ActiveGoalPattern.IsMatch(lower) ||
                   lower.Contains("öğrenmek istiyorum") || lower.Contains("ogrenmek istiyorum") ||
                   FavoriteColorPattern.IsMatch(message);
        }

        private static ExtractedMemory TryCatName(string text)
        {
            if (MemoryRecallHelper.IsMemoryQuestion(text) || MemoryRecallHelper.ContainsQuestionSignals(text))
                return null;

            var match = CatNamePattern.Match(text);
            if (!match.Success)
                return null;

            var name = CleanEntityValue(match.Groups[1].Value);
            if (!IsValidEntityName(name))
                return null;

            return Entity(EntityKeys.CatName, name, 96);
        }

        private static ExtractedMemory TryDogName(string text)
        {
            if (MemoryRecallHelper.IsMemoryQuestion(text) || MemoryRecallHelper.ContainsQuestionSignals(text))
                return null;

            var match = DogNamePattern.Match(text);
            if (!match.Success)
                return null;

            var name = CleanEntityValue(match.Groups[1].Value);
            if (!IsValidEntityName(name))
                return null;

            return Entity(EntityKeys.DogName, name, 94);
        }

        private static ExtractedMemory TryFavoriteColor(string text, string lower)
        {
            if (lower.Contains("kedimin") || lower.Contains("köpeğimin") || lower.Contains("kopegimin"))
                return null;

            var match = FavoriteColorPattern.Match(text);
            if (!match.Success)
                return null;

            var value = CleanEntityValue(
                match.Groups[1].Success ? match.Groups[1].Value :
                match.Groups[2].Success ? match.Groups[2].Value :
                match.Groups[3].Value);

            if (!IsKnownColor(value))
                return null;

            return Entity(EntityKeys.FavoriteColor, value, 88);
        }

        private static ExtractedMemory TryFavoriteFood(string text, string lower)
        {
            var match = FavoriteFoodPattern.Match(text);
            if (!match.Success)
                return null;

            var value = CleanEntityValue(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            return Entity(EntityKeys.FavoriteFood, value, 82);
        }

        private static ExtractedMemory TryAvoidBaba(string text)
        {
            if (!AvoidBabaPattern.IsMatch(text))
                return null;

            return Entity(EntityKeys.AvoidNicknameBaba, "true", 100);
        }

        private static ExtractedMemory TryActiveGoal(string text)
        {
            var match = ActiveGoalPattern.Match(text);
            if (!match.Success)
                return null;

            var goal = CleanEntityValue(match.Groups[1].Value) + " öğrenmek";
            return Entity(EntityKeys.ActiveGoal, goal, 95);
        }

        private static IList<ExtractedMemory> TryHasKittens(string text, ConversationMemoryContext context)
        {
            var match = HasKittensPattern.Match(text);
            if (!match.Success)
                return null;

            var subject = CleanEntityValue(match.Groups[1].Value);
            var results = new List<ExtractedMemory>
            {
                Entity(EntityKeys.HasKittens, "true", 92)
            };

            if (!string.IsNullOrWhiteSpace(subject) &&
                !string.Equals(subject, "o", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(subject, "onun", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Entity(EntityKeys.CatName, subject, 96));
            }

            return results;
        }

        private static ExtractedMemory TryKittenCount(string text, ConversationMemoryContext context)
        {
            if (!context.ExpectingKittenDetails)
                return null;

            var match = KittenCountPattern.Match(text.Trim());
            if (!match.Success)
                return null;

            return Entity(EntityKeys.KittenCount, match.Groups[1].Value, 90);
        }

        private static ExtractedMemory TryKittenName(string text, ConversationMemoryContext context)
        {
            if (!context.ExpectingKittenNames && !context.ExpectingKittenDetails)
                return null;

            var name = CleanEntityValue(text);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30)
                return null;

            if (!KittenNameGuard.IsValidKittenName(name))
                return null;

            if (!Regex.IsMatch(name, @"^[A-Za-zÇçĞğİıÖöŞşÜü][A-Za-zÇçĞğİıÖöŞşÜü0-9'-]{0,19}$"))
                return null;

            return new ExtractedMemory
            {
                ShouldSave = true,
                EntityKey = EntityKeys.KittenNames,
                Topic = EntityKeys.Topic(EntityKeys.KittenNames),
                Content = name,
                Importance = 88,
                AppendToList = true
            };
        }

        private static ExtractedMemory TryPreferredName(string text)
        {
            var match = Regex.Match(text, @"(?:benim\s+)?ad[ıi]m\s+(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return Entity(EntityKeys.PreferredName, CleanEntityValue(match.Groups[1].Value), 95);
        }

        private static ExtractedMemory Entity(string key, string value, int importance)
        {
            return new ExtractedMemory
            {
                ShouldSave = true,
                EntityKey = key,
                Topic = EntityKeys.Topic(key),
                Content = value,
                Importance = importance,
                AppendToList = false
            };
        }

        private static bool IsKnownColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var lower = value.Trim().ToLowerInvariant();
            return KnownColors.Any(c => lower.Contains(c));
        }

        private static void TryAdd(ICollection<ExtractedMemory> results, ExtractedMemory item)
        {
            if (item != null && item.ShouldSave)
                results.Add(item);
        }

        private static void TryAddMany(ICollection<ExtractedMemory> results, IEnumerable<ExtractedMemory> items)
        {
            if (items == null)
                return;

            foreach (var item in items)
                TryAdd(results, item);
        }

        private static string CleanEntityValue(string value) =>
            (value ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',', ';', ':');

        private static bool IsValidEntityName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var lower = name.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            return !ContainsAny(lower,
                "neydi", "nedir", "ne", "kim", "hangi", "kaç", "kac", "nerede", "ne zaman",
                "misin", "mısın", "musun", "miyim", "mıyım");
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public sealed class ConversationMemoryContext
    {
        public bool ExpectingKittenNames { get; set; }
        public bool ExpectingKittenDetails { get; set; }
        public string KnownCatName { get; set; }
        public int ExpectedKittenCount { get; set; }
        public int CollectedKittenNames { get; set; }
    }

    public static class ConversationMemoryContextBuilder
    {
        public static ConversationMemoryContext Analyze(IReadOnlyList<ChatMessage> recentHistory)
        {
            var context = new ConversationMemoryContext();
            if (recentHistory == null || recentHistory.Count == 0)
                return context;

            var recent = TakeLast(
                recentHistory
                    .Where(m => m != null && m.Role == ChatRole.User && !string.IsNullOrWhiteSpace(m.Content))
                    .Select(m => m.Content.Trim().ToLowerInvariant()),
                15)
                .ToList();

            foreach (var line in recent)
            {
                if (line.Contains("yavru") || line.Contains("kitten"))
                    context.ExpectingKittenNames = true;

                if (line.Contains("yavruları var") || line.Contains("yavrulari var"))
                    context.ExpectingKittenDetails = true;

                var countMatch = Regex.Match(line, @"^(\d+)\s+tane\.?$");
                if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var count))
                    context.ExpectedKittenCount = count;

                var catMatch = Regex.Match(line, @"ked(?:im|imin)?(?:in)?\s+(?:ad[ıi]|ismi)\s+(.+)$");
                if (catMatch.Success)
                    context.KnownCatName = catMatch.Groups[1].Value.Trim().TrimEnd('.');

                if (context.ExpectingKittenNames &&
                    KittenNameGuard.IsValidKittenName(CleanKittenCandidate(line)))
                {
                    context.CollectedKittenNames++;
                }
            }

            if (context.ExpectingKittenDetails)
                context.ExpectingKittenNames = true;

            if (context.ExpectedKittenCount > 0 &&
                context.CollectedKittenNames >= context.ExpectedKittenCount)
            {
                context.ExpectingKittenNames = false;
            }

            return context;
        }

        private static string CleanKittenCandidate(string line)
        {
            return (line ?? string.Empty).Trim().TrimEnd('.', '!', '?', ',', ';', ':');
        }

        private static IEnumerable<T> TakeLast<T>(IEnumerable<T> source, int count)
        {
            var buffer = new Queue<T>();
            foreach (var item in source)
            {
                buffer.Enqueue(item);
                if (buffer.Count > count)
                    buffer.Dequeue();
            }

            return buffer;
        }
    }
}
