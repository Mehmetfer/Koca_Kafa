using System;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public sealed class RelationshipProfile
    {
        public string Hitap { get; set; }
        public string PreferredName { get; set; }
        public bool ForbidBabaAddress { get; set; }
        public RelationshipStage Stage { get; set; }
        public int TrustLevel { get; set; }
    }

    public static class RelationshipMemoryLayer
    {
        public static RelationshipProfile Build(string memoryContext, string ownerName, ConversationBrainState state)
        {
            var forbidBaba = UserPreferenceResolver.ShouldAvoidBaba(memoryContext) ||
                             (state?.ForbidBabaAddress ?? false);
            var preferred = ExtractPreferredName(memoryContext) ?? state?.PreferredName;
            var hitap = forbidBaba
                ? ResolveNonBabaHitap(ownerName, preferred)
                : (string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim());

            var trust = state?.TrustLevel ?? 50;
            if (forbidBaba)
                trust = Math.Max(trust, 60);

            return new RelationshipProfile
            {
                Hitap = hitap,
                PreferredName = preferred,
                ForbidBabaAddress = forbidBaba,
                Stage = state?.RelationshipStage ?? RelationshipStage.Familiar,
                TrustLevel = trust
            };
        }

        public static string ApplyOverrides(string text, RelationshipProfile relationship)
        {
            if (string.IsNullOrWhiteSpace(text) || relationship == null)
                return text ?? string.Empty;

            if (!relationship.ForbidBabaAddress)
                return text;

            return UserPreferenceResolver.StripForbiddenNickname(text, "[Entity:avoid_nickname_baba] true");
        }

        private static string ResolveNonBabaHitap(string ownerName, string preferredName)
        {
            if (!string.IsNullOrWhiteSpace(preferredName))
                return preferredName.Trim();

            var owner = (ownerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(owner) ||
                string.Equals(owner, "baba", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return owner;
        }

        private static string ExtractPreferredName(string memoryContext)
        {
            if (string.IsNullOrWhiteSpace(memoryContext))
                return null;

            const string marker = "[Entity:preferred_name]";
            var index = memoryContext.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            var lineEnd = memoryContext.IndexOf('\n', index);
            var line = lineEnd < 0
                ? memoryContext.Substring(index)
                : memoryContext.Substring(index, lineEnd - index);

            var parts = line.Split(new[] { ']' }, 2);
            return parts.Length < 2 ? null : parts[1].Trim();
        }
    }
}
