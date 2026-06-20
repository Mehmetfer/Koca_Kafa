using System;
using System.Collections.Generic;

namespace Koca_Kafa.Services.Cognitive
{
    public static class KittenNameGuard
    {
        private static readonly HashSet<string> BlockedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "teşekkürler", "tesekkurler", "teşekkür", "tesekkur", "sağol", "sagol",
            "merhaba", "selam", "hey", "günaydın", "gunaydin", "iyi", "akşamlar", "aksamlar",
            "tamam", "peki", "evet", "hayır", "hayir", "hmm", "hm", "ok", "okay",
            "güzel", "guzel", "harika", "devam", "olur", "anladım", "anladim",
            "teşekkürler.", "merhaba.", "selam.", "naber", "naber.", "thanks", "hello", "hi"
        };

        public static bool IsValidKittenName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var trimmed = name.Trim().TrimEnd('.', '!', '?', ',', ';', ':');
            if (trimmed.Length < 2 || trimmed.Length > 20)
                return false;

            if (BlockedTokens.Contains(trimmed))
                return false;

            if (trimmed.IndexOf(' ') >= 0)
                return false;

            return true;
        }
    }
}
