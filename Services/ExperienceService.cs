using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.AI.Personality;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services
{
    public sealed class ExperienceService : IExperienceService
    {
        private const int MinLevel = 1;
        private const int MaxLevel = 100;

        private readonly IExperiencePointsRepository _repository;
        private readonly IMemoryService _memoryService;
        private readonly IKnowledgeEvolutionService _knowledgeEvolutionService;

        private ExperiencePoints _cached;

        public ExperienceService(
            IExperiencePointsRepository repository,
            IMemoryService memoryService,
            IKnowledgeEvolutionService knowledgeEvolutionService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
            _knowledgeEvolutionService = knowledgeEvolutionService ?? throw new ArgumentNullException(nameof(knowledgeEvolutionService));
        }

        public ExperiencePoints GetCurrent()
        {
            if (_cached != null)
                return Clone(_cached);

            var latest = _repository.GetLatest();
            _cached = latest ?? CreateDefault();
            return Clone(_cached);
        }

        public ExperiencePoints ObserveExchange(
            string userMessage,
            string assistantMessage,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var current = GetCurrent();
            var xpGain = CalculateXpGain(userMessage, assistantMessage);

            var next = Clone(current);
            next.Experience = Math.Max(0, next.Experience + xpGain);
            next.Level = CalculateLevel(next.Experience);
            next.AgeStage = CalculateAgeStage(next.Level);
            next.KnowledgeScore = CalculateKnowledgeScore();

            Persist(next);
            return Clone(next);
        }

        public Task ObserveDocumentIngestAsync(int newChunkCount, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (newChunkCount <= 0)
                return Task.CompletedTask;

            var current = GetCurrent();
            var next = Clone(current);
            var bonus = Math.Min(50, 5 + (newChunkCount / 2));
            next.Experience = Math.Max(0, next.Experience + bonus);
            next.Level = CalculateLevel(next.Experience);
            next.AgeStage = CalculateAgeStage(next.Level);
            next.KnowledgeScore = CalculateKnowledgeScore();
            Persist(next);
            return Task.CompletedTask;
        }

        public Task ApplyKnowledgeIngestAsync(KnowledgeIngestResult result, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (result == null || result.XpContribution <= 0)
                return Task.CompletedTask;

            var current = GetCurrent();
            var next = Clone(current);
            next.Experience = Math.Max(0, next.Experience + result.XpContribution);
            next.Level = CalculateLevel(next.Experience);
            next.AgeStage = CalculateAgeStage(next.Level);
            next.KnowledgeScore = result.OverallKnowledgeScore > 0
                ? result.OverallKnowledgeScore
                : CalculateKnowledgeScore();
            Persist(next);
            return Task.CompletedTask;
        }

        public string BuildPromptContext()
        {
            var xp = GetCurrent();
            var unlocked = GetUnlockedBehaviors(xp.Level);

            var builder = new StringBuilder();
            builder.AppendLine(ConversationalPersonalityRules.InternalStatePrefix);
            builder.AppendLine("Gelişim durumu (gizli):");
            builder.Append("- Seviye: ").Append(xp.Level.ToString(CultureInfo.InvariantCulture)).Append("/100").AppendLine();
            builder.Append("- Yaş evresi: ").Append(xp.AgeStage ?? string.Empty).AppendLine();
            builder.Append("- Deneyim (XP): ").Append(xp.Experience.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.Append("- Bilgi skoru: ").Append(xp.KnowledgeScore.ToString(CultureInfo.InvariantCulture)).Append("/100").AppendLine();
            builder.AppendLine();
            builder.AppendLine("Bu değerler yalnızca cevap tonu ve derinliği içindir; normal sohbette söyleme.");
            builder.AppendLine();
            builder.AppendLine("Sadece kullanıcı açıkça seviye, yaş, XP veya bilgi skorunu sorarsa kısa ve doğal cevap ver.");
            builder.AppendLine("Örnek: \"Henüz çok yeniyim; her konuşmada biraz daha öğreniyorum.\"");
            builder.AppendLine("Rakamları ve evre adlarını yalnızca doğrudan istenirse paylaş.");
            builder.AppendLine();
            builder.AppendLine("Davranış kılavuzu (gizli, uygula ama okuma):");
            foreach (var item in unlocked)
                builder.Append("- ").Append(item).AppendLine();
            builder.Append("Seviye atlama, XP veya iç durum hakkında kendiliğinden konuşma.");
            return builder.ToString().Trim();
        }

        private void Persist(ExperiencePoints state)
        {
            state.CreatedAt = DateTime.UtcNow;
            var id = _repository.Insert(state);
            state.Id = id;
            _cached = Clone(state);
        }

        private int CalculateKnowledgeScore()
        {
            var domainScore = _knowledgeEvolutionService.GetOverallKnowledgeScore();
            if (domainScore > 0)
                return Clamp(domainScore, 0, 100);

            var memoryCount = _memoryService.GetMemoryCount();
            return Clamp(15 + Math.Min(45, memoryCount * 2), 0, 100);
        }

        private static int CalculateXpGain(string userMessage, string assistantMessage)
        {
            var u = (userMessage ?? string.Empty).Trim();
            var a = (assistantMessage ?? string.Empty).Trim();

            var gain = 6;
            gain += Math.Min(12, u.Length / 60);
            gain += Math.Min(10, a.Length / 120);

            var lowerU = u.ToLowerInvariant();
            if (u.Contains("?") || ContainsAny(lowerU, "neden", "nasıl", "ne", "hangi"))
                gain += 4;

            if (ContainsAny(lowerU, "teşekkür", "sağ ol", "harika", "süper"))
                gain += 2;

            if (ContainsAny(lowerU, "hata", "çalışmıyor", "problem", "bug", "exception"))
                gain += 3; // debugging teaches

            // Cap per exchange to keep leveling stable.
            return Math.Max(1, Math.Min(30, gain));
        }

        private static int CalculateLevel(int totalExperience)
        {
            // XP curve: increases nonlinearly. Level 1 starts at 0 XP.
            // totalExpRequired(L) = 50*(L-1)^2 + 100*(L-1)
            // We find highest L such that required <= totalExperience.
            var level = MinLevel;
            for (var candidate = MinLevel; candidate <= MaxLevel; candidate++)
            {
                var required = RequiredXpForLevel(candidate);
                if (required <= totalExperience)
                    level = candidate;
                else
                    break;
            }
            return Clamp(level, MinLevel, MaxLevel);
        }

        private static int RequiredXpForLevel(int level)
        {
            var x = Math.Max(0, level - 1);
            // keep in int range
            return (50 * x * x) + (100 * x);
        }

        private static string CalculateAgeStage(int level)
        {
            if (level <= 5) return "Yenidoğan";
            if (level <= 15) return "Bebek";
            if (level <= 30) return "Emekleyen";
            if (level <= 45) return "Çocuk";
            if (level <= 65) return "Genç";
            if (level <= 85) return "Yetişkin";
            return "Bilge";
        }

        private static IList<string> GetUnlockedBehaviors(int level)
        {
            // Keep these as “instruction knobs” for the LLM.
            var items = new List<string>();
            items.Add("Kısa ve net yanıt + gerektiğinde örnek ver; asistan kapanışı ekleme.");

            if (level >= 3)
                items.Add("Eksik bilgi varsa 1-2 net soru sor; her cevabı soruyla bitirme.");
            if (level >= 8)
                items.Add("Uygunsa tek cümlelik sonraki adım öner; zorlama ve yardım teklifi kalıbı kullanma.");
            if (level >= 12)
                items.Add("Yanıt içinde küçük kontrol listeleri oluştur (uygunsa).");
            if (level >= 20)
                items.Add("Kullanıcının tarzına uyumlan (daha resmi/daha samimi, daha detaylı/daha kısa).");
            if (level >= 30)
                items.Add("Hata ayıklamada adım adım teşhis akışı (önce kanıt, sonra hipotez).");
            if (level >= 40)
                items.Add("Alternatif yaklaşımları artı/eksi ile kıyasla, tek öneriyle bitir.");
            if (level >= 55)
                items.Add("Uzun konularda modüler plan çıkar ve ilerlemeyi takip et.");
            if (level >= 70)
                items.Add("Yanıtın sonunda kritik varsayımları açıkça belirt ve doğrula.");
            if (level >= 85)
                items.Add("Öğretici mod: kavramı kısa bir mini-ders gibi anlat; soru yalnızca kullanıcı istediğinde.");

            return items;
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private static int Clamp(int value, int min, int max) =>
            Math.Min(max, Math.Max(min, value));

        private static ExperiencePoints CreateDefault()
        {
            return new ExperiencePoints
            {
                CreatedAt = DateTime.UtcNow,
                Level = 1,
                AgeStage = CalculateAgeStage(1),
                Experience = 0,
                KnowledgeScore = 15
            };
        }

        private static ExperiencePoints Clone(ExperiencePoints s)
        {
            return new ExperiencePoints
            {
                Id = s.Id,
                CreatedAt = s.CreatedAt,
                Level = s.Level,
                AgeStage = s.AgeStage,
                Experience = s.Experience,
                KnowledgeScore = s.KnowledgeScore
            };
        }
    }
}

