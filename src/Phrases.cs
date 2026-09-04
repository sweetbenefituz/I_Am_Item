using System.Collections.Generic;

namespace IamItem
{
    /// <summary>
    /// Раздел 12 диздока. Английский, короткие слова, без апострофов и
    /// сокращений — встроенный синтезатор игры читает такое разборчиво.
    /// Отдельный файл, чтобы позже подставить перевод.
    /// </summary>
    internal static class Phrases
    {
        public static readonly string[] Pickup =
        {
            "Sorry, I need to sell you, friend.",
            "Mate, you will serve the future, sorry.",
            "It is nothing personal. It is just money.",
            "You were a good teammate. Now you are good money.",
            "I promise I will spend you well.",
            "Rest now, buddy. The truck is waiting.",
            "Please do not look at me like that.",
            "I really hate this job.",
            "You always wanted to be useful.",
            "One last ride, pal.",
            "I will tell them you went quietly.",
            "Please stop rattling. It is harder this way.",
            "We all end up in the cart eventually.",
            "Say hello to the taxman for me.",
            "Sorry. The rent is due.",
            "You feel heavier than you looked.",
            "This is exactly why I have no friends.",
            "I would carry you out, but you are the loot now.",
            "Forgive me. The quota does not.",
            "You are worth more like this. Sorry.",
        };

        public static readonly string[] Cart =
        {
            "Goodbye, friend.",
            "In you go.",
            "Try to be worth something.",
            "It is warm in there. Probably.",
            "See you next level. Maybe.",
            "You are officially cargo now.",
            "Sleep well, little guy.",
        };

        public static readonly string[] Break =
        {
            "Oh no. Oh no no no.",
            "That was a person.",
            "I did not do that.",
            "We do not talk about this.",
            "He was already broken. Probably.",
        };

        // Что сказали в прошлый раз из каждого набора — чтобы одна и та же
        // фраза не выпала дважды подряд.
        private static readonly Dictionary<string[], int> lastIndex = new Dictionary<string[], int>();

        public static string Pick(string[] set)
        {
            if (set == null || set.Length == 0) return null;
            if (set.Length == 1) return set[0];

            lastIndex.TryGetValue(set, out int previous);
            int i;
            do
            {
                i = UnityEngine.Random.Range(0, set.Length);
            }
            while (i == previous);

            lastIndex[set] = i;
            return set[i];
        }
    }
}
