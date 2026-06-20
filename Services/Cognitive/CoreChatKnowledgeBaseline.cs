using System;
using System.Collections.Generic;
using System.Linq;

namespace Koca_Kafa.Services.Cognitive
{
    public static class CoreChatKnowledgeBaseline
    {
        private sealed class KnowledgeEntry
        {
            public string[] Triggers { get; set; }
            public string Turkish { get; set; }
            public string English { get; set; }
        }

        private static readonly IList<KnowledgeEntry> Entries = new List<KnowledgeEntry>
        {
            new KnowledgeEntry
            {
                Triggers = new[] { "python" },
                Turkish = "Python, genel amaçlı bir programlama dilidir. Özellikle veri analizi, yapay zeka ve web geliştirme alanlarında kullanılır.",
                English = "Python is a general-purpose programming language widely used in data analysis, artificial intelligence, and web development."
            },
            new KnowledgeEntry
            {
                Triggers = new[] { "c#", "c sharp", "csharp" },
                Turkish = "C#, Microsoft'un .NET platformu için geliştirdiği modern bir programlama dilidir. Masaüstü, web ve oyun geliştirmede yaygın kullanılır.",
                English = "C# is a modern programming language by Microsoft for the .NET platform, widely used for desktop, web, and game development."
            },
            new KnowledgeEntry
            {
                Triggers = new[] { "javascript", "java script" },
                Turkish = "JavaScript, web sayfalarına etkileşim kazandırmak için kullanılan bir programlama dilidir.",
                English = "JavaScript is a programming language used to add interactivity to web pages."
            },
            new KnowledgeEntry
            {
                Triggers = new[] { "html" },
                Turkish = "HTML, web sayfalarının yapısını tanımlayan bir işaretleme dilidir.",
                English = "HTML is a markup language used to define the structure of web pages."
            }
        };

        public static string TryAnswer(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var lower = message.Trim().ToLowerInvariant();
            if (!IsExplanationOrTopicQuery(lower))
                return null;

            foreach (var entry in Entries)
            {
                if (!entry.Triggers.Any(t => lower.Contains(t)))
                    continue;

                return IsEnglish(message) ? entry.English : entry.Turkish;
            }

            return null;
        }

        private static bool IsExplanationOrTopicQuery(string lower) =>
            ContainsAny(lower,
                "nedir", "ne demek", "nelerdir", "what is", "what are", "explain",
                "hakkında ne biliyorsun", "hakkinda ne biliyorsun", "hakkında ne", "hakkinda ne");

        private static bool IsEnglish(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return ContainsAny(lower, "what is", "what are", "explain", "how does");
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
