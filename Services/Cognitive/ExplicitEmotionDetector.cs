using System;
using System.Linq;

namespace Koca_Kafa.Services.Cognitive
{
    public static class ExplicitEmotionDetector
    {
        private static readonly string[] ExplicitSignals =
        {
            "kimse benimle konuşmadı", "kimse benimle konusmadi",
            "kimse konuşmadı", "kimse konusmadi", "benimle konuşmadı", "benimle konusmadi",
            "kendimi kötü hissediyorum", "kendimi kotu hissediyorum",
            "kendimi kötü hissed", "kendimi kotu hissed",
            "yalnızım", "yalnizim", "yalnız hissed", "yalniz hissed",
            "üzgünüm", "uzgunum",
            "moralim bozuk", "moralim düşük", "moralim dusuk",
            "çok yoruldum", "cok yoruldum",
            "başarısız hissediyorum", "basarisiz hissediyorum",
            "başarısız hissed", "basarisiz hissed",
            "kimse yok", "hiç kimse yok", "hic kimse yok",
            "tek başıma", "tek basima", "yalnız kaldım", "yalniz kaldim",
            "patron bana bağırdı", "patron bana bagirdi", "patron bağırdı", "patron bagirdi",
            "terfi aldım", "terfi aldim", "başardım", "basardim", "kazandım", "kazandim",
            "kaybettim", "vefat etti", "özledim", "ozledim", "ağlıyorum", "agliyorum"
        };

        public static bool IsExplicitEmotionalStatement(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return ContainsAny(message.Trim().ToLowerInvariant(), ExplicitSignals);
        }

        private static bool ContainsAny(string text, params string[] needles) =>
            needles.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
