using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.KnowledgeBase;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.KnowledgeBase.Abstractions;
using Koca_Kafa.Models;
using Koca_Kafa.MemoryStore;
using Koca_Kafa.Services.Abstractions;
using Koca_Kafa.Services.Background;
using Koca_Kafa.Services.Cognitive;

namespace Koca_Kafa.Services
{
    public sealed class MemoryService : IMemoryService
    {
        private const double SimilarityThreshold = 0.55;
        private readonly IMemoryRepository _repository;
        private readonly IMemoryExtractorService _extractor;
        private readonly IMemoryEmbeddingService _memoryEmbedding;
        private readonly IBackgroundTaskCoordinator _backgroundTasks;
        private readonly MemoryRetriever _retriever;

        public MemoryService(
            IMemoryRepository repository,
            IMemoryExtractorService extractor,
            IMemoryEmbeddingService memoryEmbedding,
            IBackgroundTaskCoordinator backgroundTasks,
            MemoryRetriever retriever)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _memoryEmbedding = memoryEmbedding ?? throw new ArgumentNullException(nameof(memoryEmbedding));
            _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
            _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
        }

        public long UpsertEntityMemory(string entityKey, string value, int importance = 80, bool appendToList = false)
        {
            if (string.IsNullOrWhiteSpace(entityKey))
                throw new ArgumentException("Entity key boş olamaz.", nameof(entityKey));

            var topic = EntityKeys.Topic(entityKey);
            var normalizedValue = (value ?? string.Empty).Trim();
            var boundedImportance = BoundImportance(importance);

            if (MemoryPriorityEngine.IsIdentityLocked(entityKey))
            {
                boundedImportance = MemoryPriorityEngine.CriticalIdentityPriority;
                if (string.Equals(entityKey, EntityKeys.AvoidNicknameBaba, StringComparison.OrdinalIgnoreCase))
                    normalizedValue = "true";
            }

            var existing = _repository.GetAll()
                .FirstOrDefault(m => string.Equals(m.Topic, topic, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (MemoryPriorityEngine.IsIdentityLocked(entityKey))
                {
                    if (string.IsNullOrWhiteSpace(normalizedValue))
                        return existing.Id;

                    existing.Importance = MemoryPriorityEngine.CriticalIdentityPriority;
                    existing.LastAccess = DateTime.UtcNow;
                    _repository.Update(existing);
                    QueueIndexMemory(existing);
                    return existing.Id;
                }

                if (appendToList && !string.IsNullOrWhiteSpace(normalizedValue))
                {
                    var names = existing.Content
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(n => n.Trim())
                        .Where(n => n.Length > 0)
                        .ToList();

                    if (string.Equals(entityKey, EntityKeys.KittenNames, StringComparison.OrdinalIgnoreCase) &&
                        !KittenNameGuard.IsValidKittenName(normalizedValue))
                        return existing.Id;

                    if (!names.Any(n => string.Equals(n, normalizedValue, StringComparison.OrdinalIgnoreCase)))
                        names.Add(normalizedValue);

                    existing.Content = string.Join(", ", names);
                }
                else if (!string.IsNullOrWhiteSpace(normalizedValue))
                {
                    existing.Content = normalizedValue;
                }

                existing.Importance = Math.Max(existing.Importance, boundedImportance);
                existing.LastAccess = DateTime.UtcNow;
                _repository.Update(existing);
                QueueIndexMemory(existing);
                return existing.Id;
            }

            return AddMemory(topic, normalizedValue, boundedImportance);
        }

        public string GetEntityValue(string entityKey)
        {
            if (string.IsNullOrWhiteSpace(entityKey))
                return null;

            var topic = EntityKeys.Topic(entityKey);
            var existing = _repository.GetAll()
                .FirstOrDefault(m => string.Equals(m.Topic, topic, StringComparison.OrdinalIgnoreCase));

            return existing?.Content?.Trim();
        }

        public long AddMemory(string topic, string content, int importance = 50)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Hafıza içeriği boş olamaz.", nameof(content));

            var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? "Genel" : topic.Trim();
            var normalizedContent = content.Trim();
            var boundedImportance = BoundImportance(importance);

            var similar = GetSimilarMemories(normalizedContent, 1);
            if (similar.Count > 0 && ComputeSimilarity(normalizedContent, similar[0].Content) >= 0.85)
            {
                var existing = similar[0];
                existing.Topic = normalizedTopic;
                existing.Content = normalizedContent;
                existing.Importance = Math.Max(existing.Importance, boundedImportance);
                existing.LastAccess = DateTime.UtcNow;
                _repository.Update(existing);
                QueueIndexMemory(existing);
                return existing.Id;
            }

            var item = new MemoryItem
            {
                CreatedAt = DateTime.UtcNow,
                Topic = normalizedTopic,
                Content = normalizedContent,
                Importance = boundedImportance,
                LastAccess = null,
                AccessCount = 0
            };

            var id = _repository.Insert(item);
            item.Id = id;
            QueueIndexMemory(item);
            return id;
        }

        public bool UpdateMemory(long id, string topic, string content, int? importance = null)
        {
            var existing = _repository.GetById(id);
            if (existing == null)
                return false;

            if (!string.IsNullOrWhiteSpace(topic))
                existing.Topic = topic.Trim();
            if (!string.IsNullOrWhiteSpace(content))
                existing.Content = content.Trim();
            if (importance.HasValue)
                existing.Importance = BoundImportance(importance.Value);

            var updated = _repository.Update(existing);
            if (updated)
                QueueIndexMemory(existing);
            return updated;
        }

        public IList<MemoryItem> SearchMemories(string query, int limit = 20)
        {
            var results = _repository.Search(query, limit);
            foreach (var item in results)
                _repository.RecordAccess(item.Id);
            return results;
        }

        public IList<MemoryItem> GetSimilarMemories(string text, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<MemoryItem>();

            var candidates = _repository.Search(text, Math.Max(limit * 4, 20));
            return candidates
                .Select(item => new ScoredMemory(item, ComputeSimilarity(text, item.Content + " " + item.Topic)))
                .Where(x => x.Score >= SimilarityThreshold)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.Importance)
                .Take(limit)
                .Select(x => x.Item)
                .ToList();
        }

        public IList<MemoryItem> GetMostImportantMemories(int limit = 10)
        {
            return _repository.GetAll()
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.AccessCount)
                .ThenByDescending(m => m.CreatedAt)
                .Take(limit)
                .ToList();
        }

        public async Task<string> BuildContextForQueryAsync(
            string query,
            int limit = MemoryEmbeddingService.DefaultTopK,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _retriever.BuildContextAsync(query, limit, cancellationToken).ConfigureAwait(false);
        }

        public string BuildFastContext(int limit = 3) => _retriever.BuildQueryAwareContext(string.Empty, Math.Max(limit, 3));

        public string BuildQueryAwareContext(string query, int limit = 10) =>
            _retriever.BuildQueryAwareContext(query, limit);

        private void QueueIndexMemory(MemoryItem item)
        {
            _backgroundTasks.Queue(
                "IndexMemory",
                () => _memoryEmbedding.IndexMemoryAsync(item));
        }

        public Task LearnFromUserMessageAsync(
            string message,
            IReadOnlyList<ChatMessage> recentHistory = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extracted = _extractor.Extract(message, recentHistory);
            foreach (var item in extracted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.ShouldSave)
                    continue;

                if (!string.IsNullOrWhiteSpace(item.EntityKey))
                {
                    UpsertEntityMemory(item.EntityKey, item.Content, item.Importance, item.AppendToList);
                    continue;
                }

                AddMemory(item.Topic, item.Content, item.Importance);
            }

            return Task.CompletedTask;
        }

        public int GetMemoryCount() => _repository.Count();

        private static int BoundImportance(int importance)
        {
            if (importance < 1) return 1;
            if (importance > 100) return 100;
            return importance;
        }

        private static double ComputeSimilarity(string left, string right)
        {
            var leftTokens = Tokenize(left);
            var rightTokens = Tokenize(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
                return 0;

            var intersection = leftTokens.Intersect(rightTokens).Count();
            var union = leftTokens.Union(rightTokens).Count();
            return union == 0 ? 0 : (double)intersection / union;
        }

        private static HashSet<string> Tokenize(string text)
        {
            return new HashSet<string>(
                (text ?? string.Empty)
                    .ToLowerInvariant()
                    .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length > 1));
        }

        private sealed class ScoredMemory
        {
            public ScoredMemory(MemoryItem item, double score)
            {
                Item = item;
                Score = score;
            }

            public MemoryItem Item { get; }
            public double Score { get; }
        }
    }
}
