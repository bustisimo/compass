namespace Compass.Services;

public static class FuzzyMatcher
{
    /// <summary>
    /// Returns a fuzzy match score (0.0–1.0) for how well query matches text.
    /// Considers sequential character matching, word-boundary bonuses, camelCase detection.
    /// </summary>
    public static double Score(string text, string query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
            return 0;

        string lowerText = text.ToLowerInvariant();
        string lowerQuery = query.ToLowerInvariant();

        // Exact prefix match — highest score
        if (lowerText.StartsWith(lowerQuery))
            return 1.0;

        // Word-boundary prefix match (e.g., "code" matches "Visual Studio Code")
        if (lowerText.Contains(" " + lowerQuery))
            return 0.9;

        // Substring match
        if (lowerText.Contains(lowerQuery))
            return 0.75;

        // Sequential character matching with bonuses
        int queryIdx = 0;
        double score = 0;
        int consecutiveBonus = 0;
        bool prevWasBoundary = true;

        for (int i = 0; i < text.Length && queryIdx < lowerQuery.Length; i++)
        {
            char tc = char.ToLowerInvariant(text[i]);
            bool isBoundary = i == 0 || text[i - 1] == ' ' || text[i - 1] == '-' || text[i - 1] == '_'
                || (char.IsLower(text[Math.Max(0, i - 1)]) && char.IsUpper(text[i]));

            if (tc == lowerQuery[queryIdx])
            {
                queryIdx++;
                score += 1.0;

                // Bonus for word boundary matches
                if (isBoundary)
                    score += 2.0;

                // Bonus for consecutive matches
                consecutiveBonus++;
                score += consecutiveBonus * 0.5;
            }
            else
            {
                consecutiveBonus = 0;
            }
            prevWasBoundary = isBoundary;
        }

        if (queryIdx < lowerQuery.Length)
            return 0; // Not all query characters matched

        // Normalize score
        double maxPossible = lowerQuery.Length * 4.0; // rough max
        return Math.Min(score / maxPossible, 0.7);
    }
}
