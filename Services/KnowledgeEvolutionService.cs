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
using Koca_Kafa.Services.Knowledge;

namespace Koca_Kafa.Services
{
    public sealed class KnowledgeEvolutionService : IKnowledgeEvolutionService
    {
        private const int MinScore = 0;
        private const int MaxScore = 100;

        private readonly IKnowledgeDomainRepository _repository;

        public KnowledgeEvolutionService(IKnowledgeDomainRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public IList<KnowledgeDomain> GetDomains()
        {
            EnsureDefaults();
            return _repository.GetAll();
        }

        public int GetOverallKnowledgeScore()
        {
            var domains = GetDomains();
            if (domains.Count == 0)
                return 0;

            var active = domains.Where(d => d.KnowledgeScore > 0).ToList();
            if (active.Count == 0)
                return 0;

            var average = active.Average(d => d.KnowledgeScore);
            return Clamp((int)Math.Round(average, MidpointRounding.AwayFromZero), MinScore, MaxScore);
        }

        public Task<KnowledgeIngestResult> ObserveDocumentIngestAsync(
            string documentText,
            string fileName,
            int chunkCount,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDefaults();

            var boosts = KnowledgeDomainAnalyzer.Analyze(documentText);
            if (boosts.Count == 0)
            {
                boosts = new List<DocumentDomainBoost>
                {
                    new DocumentDomainBoost
                    {
                        DomainName = "Technology",
                        Boost = 1,
                        MatchCount = 0
                    }
                };
            }

            var now = DateTime.UtcNow;
            foreach (var boost in boosts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var domain = _repository.GetByName(boost.DomainName) ?? new KnowledgeDomain
                {
                    DomainName = boost.DomainName,
                    KnowledgeScore = 0,
                    DocumentCount = 0,
                    LastUpdated = now
                };

                domain.KnowledgeScore = Clamp(domain.KnowledgeScore + boost.Boost, MinScore, MaxScore);
                domain.DocumentCount = Math.Max(0, domain.DocumentCount) + 1;
                domain.LastUpdated = now;
                _repository.Upsert(domain);
            }

            var xpFromDomains = boosts.Sum(b => b.Boost);
            var xpFromChunks = Math.Min(12, Math.Max(0, chunkCount / 2));
            var xpContribution = Math.Min(35, xpFromDomains + xpFromChunks);

            var result = new KnowledgeIngestResult
            {
                DomainBoosts = boosts,
                XpContribution = xpContribution,
                OverallKnowledgeScore = GetOverallKnowledgeScore()
            };

            return Task.FromResult(result);
        }

        public string BuildPromptContext()
        {
            var domains = GetDomains()
                .OrderByDescending(d => d.KnowledgeScore)
                .ThenBy(d => d.DomainName, StringComparer.Ordinal)
                .ToList();

            if (domains.Count == 0 || domains.All(d => d.KnowledgeScore <= 0))
                return string.Empty;

            var overall = GetOverallKnowledgeScore();
            var builder = new StringBuilder();
            builder.AppendLine(ConversationalPersonalityRules.InternalStatePrefix);
            builder.AppendLine("Bilgi profili (gizli, sayıları okuma):");
            builder.AppendLine();

            foreach (var domain in domains.Where(d => d.KnowledgeScore > 0))
            {
                builder.Append(KnowledgeDomainAnalyzer.ToDisplayName(domain.DomainName))
                    .Append(": ")
                    .Append(domain.KnowledgeScore.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            builder.AppendLine();
            builder.Append("Genel bilgi seviyesi (gizli): ")
                .Append(overall.ToString(CultureInfo.InvariantCulture))
                .AppendLine("/100");
            builder.AppendLine();
            builder.AppendLine("Ton ayarı:");
            builder.AppendLine("- Yüksek skorlu alanlarda (60+) daha özgüvenli ve net konuş.");
            builder.AppendLine("- Orta skorlu alanlarda (30-59) öğrendiklerini paylaş; emin olmadığın detayları doğrula.");
            builder.Append("- Düşük skorlu alanlarda (30 altı) temkinli ol; bilmediğini söyle. ");
            builder.Append("\"Knowledge Profile\", alan adları veya skorları kullanıcıya okuma.");

            return builder.ToString().Trim();
        }

        private void EnsureDefaults() =>
            _repository.EnsureDefaults(KnowledgeDomainAnalyzer.DefaultDomains);

        private static int Clamp(int value, int min, int max) =>
            Math.Min(max, Math.Max(min, value));
    }
}
