using System;
using System.Linq;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class MissingResponseGuard
    {
        public static bool NeedsMinimumReply(string userMessage, string draftReply)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return false;

            if (!string.IsNullOrWhiteSpace(draftReply))
                return false;

            return true;
        }

        public static string BuildMinimumReply(string userMessage, string ownerName, string memoryContext)
        {
            var route = IntentActionRouter.Route(userMessage);
            var reply = IntentActionRouter.BuildRoutedReply(userMessage, route, ownerName, memoryContext);
            return EchoResponseGuard.SanitizeReply(userMessage, reply);
        }

        public static bool IsLikelyKittenName(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 20)
                return false;

            var trimmed = text.Trim();
            if (trimmed.Contains(" "))
                return false;

            return trimmed.All(c => char.IsLetter(c) || c == '\'' || c == '-');
        }
    }
}
