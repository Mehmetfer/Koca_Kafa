using System;
using Koca_Kafa.MemoryStore;
using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public static class ContextSwitchDetector
    {
        public static bool DetectTopicShift(
            string previousFocus,
            MemoryIntentKind previousIntent,
            MemoryIntentKind currentIntent,
            MessageCategory currentCategory)
        {
            if (string.IsNullOrWhiteSpace(previousFocus))
                return false;

            if (previousIntent == currentIntent)
                return false;

            if (currentCategory == MessageCategory.Greeting ||
                currentCategory == MessageCategory.CasualChat)
                return false;

            return IsDistinctTopic(previousIntent, currentIntent);
        }

        public static string ResolveFocus(MemoryIntentKind intent, MessageCategory category)
        {
            switch (intent)
            {
                case MemoryIntentKind.PetRecall:
                    return "pet";
                case MemoryIntentKind.GoalPlanning:
                case MemoryIntentKind.CasualPlanning:
                    return "goal";
                case MemoryIntentKind.PreferenceRecall:
                    return "preference";
                case MemoryIntentKind.Emotional:
                    return "emotion";
                case MemoryIntentKind.IdentityPreference:
                    return "relationship";
                case MemoryIntentKind.RecentFact:
                    return "recent";
                default:
                    if (category == MessageCategory.Goal)
                        return "goal";
                    if (category == MessageCategory.MemoryReference)
                        return "memory";
                    return "general";
            }
        }

        private static bool IsDistinctTopic(MemoryIntentKind previous, MemoryIntentKind current)
        {
            if (previous == MemoryIntentKind.PetRecall &&
                (current == MemoryIntentKind.GoalPlanning || current == MemoryIntentKind.CasualPlanning))
                return true;

            if ((previous == MemoryIntentKind.GoalPlanning || previous == MemoryIntentKind.CasualPlanning) &&
                current == MemoryIntentKind.PetRecall)
                return true;

            if (previous == MemoryIntentKind.PreferenceRecall && current == MemoryIntentKind.PetRecall)
                return true;

            return previous != MemoryIntentKind.General &&
                   current != MemoryIntentKind.General &&
                   previous != current;
        }
    }
}
