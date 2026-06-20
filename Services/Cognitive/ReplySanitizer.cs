using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Koca_Kafa.Services.Cognitive
{
    /// <summary>
    /// Strips internal prompt / revision labels that must never reach the user.
    /// </summary>
    public static class ReplySanitizer
    {
        private static readonly string[] InternalLinePrefixes =
        {
            "empati öncelikli",
            "empati oncelikli",
            "answerstyle:",
            "kullanıcı sorusu:",
            "kullanici sorusu:",
            "taslak cevap:",
            "taslak:",
            "sorunlar:",
            "açıklama:",
            "aciklama:",
            "current intent:",
            "response plan:",
            "empathy engine:",
            "detected emotion:",
            "intensity:",
            "confidence:",
            "reason:",
            "behavior:",
            "priority:",
            "sample tone:",
            "örnek ton:",
            "ornek ton:",
            "avoid:",
            "subtasks:",
            "requiredmemory:",
            "requiredrag:",
            "goal:",
            "answerstyle",
            "[i̇ç sistem",
            "[ic sistem",
            "bilgi tabanı bağlamına",
            "bilgi tabani baglamina",
            "yalnızca düzeltilmiş",
            "yalnizca duzeltilmis",
            "aşağıdaki taslak",
            "asagidaki taslak",
            "mesaj:",
            "düzeltilecek sorunlar:",
            "duzeltilecek sorunlar:",
            "planı kullanıcıya",
            "plani kullaniciya",
            "intent analizini"
        };

        public static string Sanitize(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return string.Empty;

            var paragraphs = SplitParagraphs(reply);
            var clean = new List<string>();

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;

                if (IsInternalParagraph(paragraph))
                    continue;

                clean.Add(paragraph.Trim());
            }

            if (clean.Count > 0)
                return string.Join("\n\n", clean).Trim();

            return ExtractLastNonInternalLine(reply);
        }

        public static bool ContainsInternalLeakage(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var lower = reply.ToLowerInvariant();
            return InternalLinePrefixes.Any(p => lower.Contains(p));
        }

        private static List<string> SplitParagraphs(string text)
        {
            return text
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.None)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();
        }

        private static bool IsInternalParagraph(string paragraph)
        {
            var lines = paragraph
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            if (lines.Count == 0)
                return true;

            var internalLineCount = lines.Count(IsInternalLabelLine);
            if (internalLineCount == lines.Count)
                return true;

            if (internalLineCount > 0 && internalLineCount >= lines.Count / 2)
                return true;

            return IsInternalLabelLine(paragraph);
        }

        private static bool IsInternalLabelLine(string line)
        {
            var lower = (line ?? string.Empty).Trim().ToLowerInvariant();
            if (lower.Length == 0)
                return false;

            return InternalLinePrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal));
        }

        private static string ExtractLastNonInternalLine(string text)
        {
            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!IsInternalLabelLine(lines[i]))
                    return lines[i];
            }

            return string.Empty;
        }
    }
}
