using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.AI.Abstractions;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services
{
    public sealed class ReflectionService : IReflectionService
    {
        private const int ReflectionIntervalMessages = 50;
        private const int AnalyzeWindowMessages = 50;

        private readonly ILessonsLearnedRepository _lessonsRepo;
        private readonly IMemoryService _memoryService;
        private readonly ILanguageModelClient _llm;
        private readonly ISettingsRepository _settings;
        private readonly IJsonSerializer _json;

        private int _lastReflectedOnMessageCount = -1;

        public ReflectionService(
            ILessonsLearnedRepository lessonsRepo,
            IMemoryService memoryService,
            ILanguageModelClient llm,
            ISettingsRepository settings,
            IJsonSerializer json)
        {
            _lessonsRepo = lessonsRepo ?? throw new ArgumentNullException(nameof(lessonsRepo));
            _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        public async Task RunIfNeededAsync(IReadOnlyList<ChatMessage> currentMessages, CancellationToken cancellationToken = default(CancellationToken))
        {
            var nonSystem = (currentMessages ?? new List<ChatMessage>())
                .Where(m => m != null && !string.Equals(m.Role, ChatRole.System, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonSystem.Count == 0)
                return;

            if (nonSystem.Count == _lastReflectedOnMessageCount)
                return;

            if (nonSystem.Count % ReflectionIntervalMessages != 0)
                return;

            _lastReflectedOnMessageCount = nonSystem.Count;

            var window = nonSystem.Skip(Math.Max(0, nonSystem.Count - AnalyzeWindowMessages)).ToList();
            var lessons = await ExtractLessonsAsync(window, cancellationToken).ConfigureAwait(false);
            if (lessons.Count == 0)
                return;

            foreach (var lesson in lessons)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lesson == null || string.IsNullOrWhiteSpace(lesson.Lesson))
                    continue;

                var normalizedLesson = lesson.Lesson.Trim();
                if (_lessonsRepo.Exists(normalizedLesson))
                    continue;

                lesson.Lesson = normalizedLesson;
                lesson.Category = (lesson.Category ?? "Genel").Trim();
                lesson.Importance = BoundImportance(lesson.Importance);
                lesson.CreatedAt = DateTime.UtcNow;

                try
                {
                    lesson.Id = _lessonsRepo.Insert(lesson);
                }
                catch
                {
                    // Unique index may reject duplicates; ignore.
                    continue;
                }

                // Memory integration: store as durable memory too (MemoryService has its own dedupe).
                _memoryService.AddMemory("Ders/" + lesson.Category, lesson.Lesson, lesson.Importance);
            }
        }

        public string BuildPromptContext(int limit = 6)
        {
            var lessons = _lessonsRepo.GetTopImportant(Math.Max(1, limit));
            if (lessons == null || lessons.Count == 0)
                return string.Empty;

            var lines = lessons
                .OrderByDescending(l => l.Importance)
                .ThenByDescending(l => l.CreatedAt)
                .Take(limit)
                .Select(l => "- " + (l.Lesson ?? string.Empty).Trim())
                .Where(x => x.Length > 2)
                .ToList();

            if (lines.Count == 0)
                return string.Empty;

            return "Öğrenilen dersler (gizli, doğal hatırla):\n" +
                   string.Join("\n", lines) +
                   "\nBu dersleri sohbete doğal yedir; 'ders çıkardım' veya sistem metaforu kullanma.";
        }

        private async Task<IList<LessonLearned>> ExtractLessonsAsync(IList<ChatMessage> messages, CancellationToken cancellationToken)
        {
            if (messages == null || messages.Count == 0)
                return new List<LessonLearned>();

            // Keep prompt small and robust for local models.
            var transcriptLines = messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => (m.Role ?? "user").ToUpperInvariant() + ": " + m.Content.Trim())
                .ToList();

            var start = Math.Max(0, transcriptLines.Count - AnalyzeWindowMessages);
            var transcript = string.Join("\n", transcriptLines.Skip(start));

            var instruction =
                "Aşağıdaki konuşmayı analiz et ve KULLANICI hakkında kalıcı dersler çıkar.\n" +
                "Şunları yakalamaya çalış: ilgi alanları, sevdiği cevap türleri, tekrar eden konular, hedefler.\n" +
                "Çıktı SADECE geçerli JSON dizi olsun. Başka metin yazma.\n" +
                "Her eleman: {\"Lesson\":\"...\",\"Category\":\"...\",\"Importance\":N}\n" +
                "Category örnekleri: Interests, PreferredStyle, RepeatedTopics, Goals.\n" +
                "Importance 1-100.\n" +
                "En fazla 6 ders üret.\n\n" +
                "KONUŞMA:\n" + transcript;

            var settings = _settings.Load();
            var reply = await _llm.GenerateReplyAsync(
                settings.PreferredModel,
                new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, "Sen bir analiz aracısın. JSON dışında hiçbir şey yazma."),
                    new ChatMessage(ChatRole.User, instruction)
                },
                cancellationToken).ConfigureAwait(false);

            try
            {
                var parsed = _json.Deserialize<List<LessonCandidate>>(reply);
                if (parsed == null || parsed.Count == 0)
                    return new List<LessonLearned>();

                return parsed
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Lesson))
                    .Select(x => new LessonLearned
                    {
                        Lesson = x.Lesson.Trim(),
                        Category = string.IsNullOrWhiteSpace(x.Category) ? "Genel" : x.Category.Trim(),
                        Importance = x.Importance
                    })
                    .ToList();
            }
            catch
            {
                // If the model didn't return JSON, fallback to simple heuristics.
                return HeuristicLessons(messages);
            }
        }

        private static IList<LessonLearned> HeuristicLessons(IList<ChatMessage> messages)
        {
            var text = string.Join("\n", messages.Where(m => m != null).Select(m => m.Content ?? string.Empty)).ToLowerInvariant();
            var results = new List<LessonLearned>();

            if (ContainsAny(text, "yazılım", "kod", "program", "c#", "dotnet", "sqlite", "api", "debug", "ollama"))
            {
                results.Add(new LessonLearned
                {
                    Lesson = "Kullanıcı yapay zeka ve yazılım konularını seviyor.",
                    Category = "Interests",
                    Importance = 70
                });
            }

            if (ContainsAny(text, "detaylı", "adım adım", "açıkla", "neden", "nasıl"))
            {
                results.Add(new LessonLearned
                {
                    Lesson = "Kullanıcı detaylı ve adım adım açıklamalardan hoşlanıyor.",
                    Category = "PreferredStyle",
                    Importance = 65
                });
            }

            return results;
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

        private static int BoundImportance(int importance)
        {
            if (importance < 1) return 1;
            if (importance > 100) return 100;
            return importance;
        }

        private sealed class LessonCandidate
        {
            public string Lesson { get; set; }
            public string Category { get; set; }
            public int Importance { get; set; }
        }
    }
}

