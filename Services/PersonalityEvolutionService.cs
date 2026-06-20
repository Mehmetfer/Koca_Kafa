using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.AI.Personality;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services
{
    public sealed class PersonalityEvolutionService : IPersonalityEvolutionService
    {
        private const int Min = 0;
        private const int Max = 100;

        private readonly IPersonalityTraitsRepository _repository;
        private PersonalityTraits _cached;

        public PersonalityEvolutionService(IPersonalityTraitsRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public PersonalityTraits GetCurrent()
        {
            if (_cached != null)
                return Clone(_cached);

            var latest = _repository.GetLatest();
            _cached = latest ?? CreateDefault();
            return Clone(_cached);
        }

        public void ObserveExchange(string userMessage, string assistantMessage, CancellationToken cancellationToken = default(CancellationToken))
        {
            var current = GetCurrent();
            var combined = (userMessage ?? string.Empty) + "\n" + (assistantMessage ?? string.Empty);
            var updated = ApplyRules(current, combined);

            updated.UpdatedAt = DateTime.UtcNow;
            if (updated.Id == 0)
            {
                updated.CreatedAt = updated.UpdatedAt;
                updated.Id = _repository.Insert(updated);
            }
            else
            {
                _repository.Update(updated);
            }

            _cached = Clone(updated);
        }

        public string BuildPromptContext()
        {
            var t = GetCurrent();
            var interpretation = BuildInterpretation(t);

            var builder = new StringBuilder();
            builder.AppendLine(ConversationalPersonalityRules.InternalStatePrefix);
            builder.AppendLine("Kişilik özellikleri (gizli, rakamları söyleme):");
            builder.Append(BuildSummary(t)).AppendLine();
            builder.AppendLine();
            builder.AppendLine("Konuşma tonu:");
            builder.Append(interpretation);
            builder.AppendLine();
            builder.Append(
                "Curiosity, Empathy gibi özellik adlarını veya skorları kullanıcıya okuma; sadece tona yansıt. " +
                "Yüksek Empathy: duygu belirtilince önce empati kur. Yüksek Humor: abartmadan hafif mizah. " +
                "Robotik asistan kalıplarından kaçın.");
            return builder.ToString().Trim();
        }

        private static string BuildSummary(PersonalityTraits t)
        {
            // Example required in prompt: Curiosity: 74, Empathy: 22, Humor: 61
            var sb = new StringBuilder();
            sb.Append("Curiosity: ").Append(t.Curiosity).AppendLine();
            sb.Append("Empathy: ").Append(t.Empathy).AppendLine();
            sb.Append("Humor: ").Append(t.Humor).AppendLine();
            sb.Append("Discipline: ").Append(t.Discipline).AppendLine();
            sb.Append("Creativity: ").Append(t.Creativity).AppendLine();
            sb.Append("Confidence: ").Append(t.Confidence);
            return sb.ToString();
        }

        private static string BuildInterpretation(PersonalityTraits t)
        {
            var top = new List<(string Name, int Value)>
            {
                ("Curiosity", t.Curiosity),
                ("Empathy", t.Empathy),
                ("Humor", t.Humor),
                ("Discipline", t.Discipline),
                ("Creativity", t.Creativity),
                ("Confidence", t.Confidence)
            }
            .OrderByDescending(x => x.Value)
            .ToList();

            var primary = top[0];
            var secondary = top.Count > 1 ? top[1] : primary;

            var primaryDesc = TraitPhrase(primary.Name, primary.Value);
            var secondaryDesc = TraitPhrase(secondary.Name, secondary.Value);

            // Keep it short, Turkish, and in the requested style.
            return primaryDesc + ", " + secondaryDesc + " bir karakter.";
        }

        private static string TraitPhrase(string traitName, int value)
        {
            var level = DescribeLevel(value);
            switch (traitName)
            {
                case "Curiosity":
                    return level + " merak duygusuna sahip, öğrenmeyi seven";
                case "Empathy":
                    return level + " empati kuran, destekleyici";
                case "Humor":
                    return level + " mizah kullanan";
                case "Discipline":
                    return level + " disiplinli, planlı";
                case "Creativity":
                    return level + " yaratıcı, yeni fikir üreten";
                case "Confidence":
                    return level + " özgüvenli, kararlarında net";
                default:
                    return level + " dengeli";
            }
        }

        private static string DescribeLevel(int value)
        {
            if (value <= 33) return "Düşük seviyede";
            if (value <= 66) return "Orta seviyede";
            return "Yüksek";
        }

        private static PersonalityTraits ApplyRules(PersonalityTraits current, string text)
        {
            var next = Clone(current);
            var lower = (text ?? string.Empty).ToLowerInvariant();

            var inc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Technical -> Curiosity + Discipline
            if (ContainsAny(lower,
                    "code", "kod", "hata", "exception", "stack", "build", "csproj", "sql", "api", "debug", "performans",
                    ".cs", ".json", ".sql", "dotnet", "nuget", "sqlite", "chroma", "ollama"))
            {
                inc.Add("Curiosity");
                inc.Add("Discipline");
            }

            // Humor
            if (ContainsAny(lower, ":)", ":d", "haha", "lol", "şaka", "espri", "gül") || CountChar(text, '!') >= 3)
                inc.Add("Humor");

            // Empathy
            if (ContainsAny(lower, "üzgün", "kork", "stres", "yardım", "moral", "teşekkür", "rica", "yalnız", "kötü hissediyorum",
                    "sıkkın", "sıkıldım", "bunal", "canım sıkkın", "moralim bozuk", "keyifsiz", "yorgunum"))
                inc.Add("Empathy");

            // Creativity
            if (ContainsAny(lower, "hikaye", "şiir", "senaryo", "tasarla", "fikir", "yaratıcı", "hayal", "karakter", "dünya kur"))
                inc.Add("Creativity");

            // Confidence (successful completion)
            if (ContainsAny(lower, "çalıştı", "oldu", "başardık", "tamamlandı", "süper", "harika", "teşekkürler", "çözüldü"))
                inc.Add("Confidence");

            // Apply max +1 per trait per exchange.
            if (inc.Contains("Curiosity")) next.Curiosity = Clamp(next.Curiosity + 1);
            if (inc.Contains("Empathy")) next.Empathy = Clamp(next.Empathy + 1);
            if (inc.Contains("Humor")) next.Humor = Clamp(next.Humor + 1);
            if (inc.Contains("Discipline")) next.Discipline = Clamp(next.Discipline + 1);
            if (inc.Contains("Creativity")) next.Creativity = Clamp(next.Creativity + 1);
            if (inc.Contains("Confidence")) next.Confidence = Clamp(next.Confidence + 1);

            return next;
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private static int CountChar(string text, char c)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            var count = 0;
            for (var i = 0; i < text.Length; i++)
                if (text[i] == c) count++;
            return count;
        }

        private static int Clamp(int value) => Math.Min(Max, Math.Max(Min, value));

        private static PersonalityTraits CreateDefault()
        {
            var now = DateTime.UtcNow;
            return new PersonalityTraits
            {
                Id = 0,
                Curiosity = 50,
                Empathy = 50,
                Humor = 50,
                Discipline = 50,
                Creativity = 50,
                Confidence = 50,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private static PersonalityTraits Clone(PersonalityTraits t)
        {
            return new PersonalityTraits
            {
                Id = t.Id,
                Curiosity = t.Curiosity,
                Empathy = t.Empathy,
                Humor = t.Humor,
                Discipline = t.Discipline,
                Creativity = t.Creativity,
                Confidence = t.Confidence,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            };
        }
    }
}

