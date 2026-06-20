using System;
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
    public sealed class EmotionStateService : IEmotionStateService
    {
        private const int Min = 0;
        private const int Max = 100;

        private readonly IEmotionStateRepository _repository;
        private EmotionState _cached;

        public EmotionStateService(IEmotionStateRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public EmotionState GetCurrent()
        {
            if (_cached != null)
                return Clone(_cached);

            var latest = _repository.GetLatest();
            _cached = latest ?? CreateDefault();
            return Clone(_cached);
        }

        public EmotionState ObserveUserMessage(string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            var current = GetCurrent();
            var updated = ApplyHeuristics(current, message ?? string.Empty, sourceRole: "user");
            Persist(updated);
            return Clone(updated);
        }

        public Task<EmotionState> ObserveAssistantMessageAsync(string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            var current = GetCurrent();
            var updated = ApplyHeuristics(current, message ?? string.Empty, sourceRole: "assistant");
            Persist(updated);
            return Task.FromResult(Clone(updated));
        }

        public string BuildPromptContext()
        {
            var e = GetCurrent();
            var builder = new StringBuilder();
            builder.AppendLine(ConversationalPersonalityRules.InternalStatePrefix);
            builder.AppendLine("Duygusal durum (gizli, rakamları söyleme):");
            builder.Append("- Merak: ").Append(e.Curiosity.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.Append("- Mutluluk: ").Append(e.Happiness.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.Append("- Özgüven: ").Append(e.Confidence.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.Append("- Güven: ").Append(e.Trust.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.Append("- Enerji: ").Append(e.Energy.ToString(CultureInfo.InvariantCulture)).AppendLine();
            builder.AppendLine();
            builder.Append("Ton ayarı: enerji düşükse kısa ve net; merak yüksekse açıklayıcı ol; güven düşükse temkinli ol. ");
            builder.Append("Duygu skorlarını veya 'duygusal durumum' ifadesini kullanıcıya okuma.");

            if (e.Happiness < 45)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append(
                    "Kullanıcı olumsuz duygu belirtmiş olabilir: önce kısa empati kur " +
                    "(\"Üzüldüm\", \"Anlıyorum\" gibi), sonra konuşmaya devam et. " +
                    "Asistan kapanışı veya \"Size nasıl yardımcı olabilirim\" kullanma.");
            }

            return builder.ToString().Trim();
        }

        private void Persist(EmotionState state)
        {
            state.CreatedAt = DateTime.UtcNow;
            var id = _repository.Insert(state);
            state.Id = id;
            _cached = Clone(state);
        }

        private static EmotionState CreateDefault()
        {
            return new EmotionState
            {
                CreatedAt = DateTime.UtcNow,
                SourceRole = "system",
                Curiosity = 55,
                Happiness = 55,
                Confidence = 55,
                Trust = 55,
                Energy = 55
            };
        }

        private static EmotionState ApplyHeuristics(EmotionState current, string message, string sourceRole)
        {
            var next = Clone(current);
            next.SourceRole = sourceRole ?? string.Empty;

            var text = (message ?? string.Empty).Trim();
            if (text.Length == 0)
                return next;

            var lower = text.ToLowerInvariant();
            var qCount = text.Count(c => c == '?') + CountOccurrences(lower, " mi ") + CountOccurrences(lower, " mı ") + CountOccurrences(lower, " mu ") + CountOccurrences(lower, " mü ");

            // Curiosity: questions & exploratory words
            var curiosityDelta = 0;
            if (qCount > 0) curiosityDelta += 6;
            if (ContainsAny(lower, "neden", "nasıl", "ne", "hangi", "nerede", "anlat", "öğren", "öğret", "açıkla")) curiosityDelta += 3;
            if (text.Length > 200) curiosityDelta += 2;

            // Happiness: positive vs negative words
            var happinessDelta = 0;
            if (ContainsAny(lower, "teşekkür", "sağ ol", "harika", "süper", "mükemmel", "sevindim", "güzel", "tamam")) happinessDelta += 6;
            if (ContainsAny(lower, "kötü", "üzgün", "sinir", "bıktım", "nefret", "berbat", "rezalet", "olmadı", "hata",
                    "sıkkın", "sıkıldım", "bunal", "moralim", "yorgunum", "üzül", "canım sıkkın", "keyifsiz")) happinessDelta -= 6;
            if (text.Contains("!")) happinessDelta += 1;

            // Confidence: clarity and decisiveness signals
            var confidenceDelta = 0;
            if (ContainsAny(lower, "eminim", "kesin", "bence", "bunu yap", "şöyle yap", "lütfen")) confidenceDelta += 2;
            if (ContainsAny(lower, "sanırım", "galiba", "bilmiyorum", "emin değilim", "kararsız")) confidenceDelta -= 3;

            // Trust: tone and politeness / hostility
            var trustDelta = 0;
            if (ContainsAny(lower, "lütfen", "rica", "teşekkür", "sağ ol")) trustDelta += 4;
            if (ContainsAny(lower, "aptal", "salak", "yalan", "dolandır", "işe yaramıyor", "saçma", "bok", "lan")) trustDelta -= 8;

            // Energy: urgency, length, caps
            var energyDelta = 0;
            if (ContainsAny(lower, "acil", "hemen", "şimdi", "çabuk")) energyDelta += 6;
            if (text.Length < 20) energyDelta -= 1;
            if (text.Length > 400) energyDelta -= 2; // long messages often correlate with fatigue
            if (IsMostlyUpper(text)) energyDelta += 2;

            next.Curiosity = Clamp(next.Curiosity + curiosityDelta);
            next.Happiness = Clamp(next.Happiness + happinessDelta);
            next.Confidence = Clamp(next.Confidence + confidenceDelta);
            next.Trust = Clamp(next.Trust + trustDelta);
            next.Energy = Clamp(next.Energy + energyDelta);

            // Gentle decay toward neutral to avoid runaway.
            next.Curiosity = DecayToward(next.Curiosity, 55, 1);
            next.Happiness = DecayToward(next.Happiness, 55, 1);
            next.Confidence = DecayToward(next.Confidence, 55, 1);
            next.Trust = DecayToward(next.Trust, 55, 1);
            next.Energy = DecayToward(next.Energy, 55, 1);

            return next;
        }

        private static int Clamp(int value) => Math.Min(Max, Math.Max(Min, value));

        private static int DecayToward(int value, int target, int step)
        {
            if (value == target) return value;
            if (value > target) return Math.Max(target, value - Math.Max(1, step));
            return Math.Min(target, value + Math.Max(1, step));
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0) break;
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static bool IsMostlyUpper(string text)
        {
            var letters = text.Count(char.IsLetter);
            if (letters < 6) return false;
            var upper = text.Count(char.IsUpper);
            return upper >= (int)(letters * 0.7);
        }

        private static EmotionState Clone(EmotionState s)
        {
            return new EmotionState
            {
                Id = s.Id,
                CreatedAt = s.CreatedAt,
                SourceRole = s.SourceRole,
                Curiosity = s.Curiosity,
                Happiness = s.Happiness,
                Confidence = s.Confidence,
                Trust = s.Trust,
                Energy = s.Energy
            };
        }
    }
}

