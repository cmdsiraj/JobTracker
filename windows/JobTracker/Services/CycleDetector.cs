// Fallback heuristic that extracts a recruiting cycle ("Summer 2026",
// "Fall 2025", "New Grad 2026") from free text when the LLM doesn't
// return one explicitly.

using System.Text.RegularExpressions;

namespace JobTracker.Services;

public static partial class CycleDetector
{
    private const string SeasonPattern = "(spring|summer|fall|autumn|winter)";
    private const string YearPattern = "(20\\d{2})";

    [GeneratedRegex($"{SeasonPattern}[\\s,‐–-]{{0,3}}{YearPattern}")]
    private static partial Regex SeasonThenYear();

    [GeneratedRegex($"{YearPattern}[\\s,‐–-]{{0,3}}{SeasonPattern}")]
    private static partial Regex YearThenSeason();

    [GeneratedRegex($"(new\\s*grad|university\\s*grad|graduate)[\\s,‐–-]{{0,3}}{YearPattern}")]
    private static partial Regex NewGrad();

    /// Returns a normalized cycle like "Summer 2026", or null if none found.
    public static string? Detect(string text)
    {
        var lower = text.ToLowerInvariant();

        var seasonYear = SeasonThenYear().Match(lower);
        if (seasonYear.Success) return Normalize(seasonYear.Groups[1].Value, seasonYear.Groups[2].Value);

        var yearSeason = YearThenSeason().Match(lower);
        if (yearSeason.Success) return Normalize(yearSeason.Groups[2].Value, yearSeason.Groups[1].Value);

        var newGrad = NewGrad().Match(lower);
        if (newGrad.Success) return $"New Grad {newGrad.Groups[2].Value}";

        return null;
    }

    /// Sort key: newer cycles first, then by season within a year.
    public static (int Year, int Season) SortKey(string cycle)
    {
        var digitsMatch = Regex.Match(cycle, @"\b\d{4}\b");
        var year = digitsMatch.Success ? int.Parse(digitsMatch.Value) : 0;
        var lower = cycle.ToLowerInvariant();
        int season =
            lower.Contains("spring") ? 0 :
            lower.Contains("summer") ? 1 :
            lower.Contains("fall") || lower.Contains("autumn") ? 2 :
            lower.Contains("winter") ? 3 : 4;
        return (year, season);
    }

    /// Newest-cycle-first comparer, for UI lists (cycle chips, filter menus).
    public static int CompareDescending(string a, string b)
    {
        var (yearA, seasonA) = SortKey(a);
        var (yearB, seasonB) = SortKey(b);
        var year = yearB.CompareTo(yearA);
        return year != 0 ? year : seasonB.CompareTo(seasonA);
    }

    private static string Normalize(string season, string year)
    {
        var name = season == "autumn" ? "Fall" : char.ToUpperInvariant(season[0]) + season[1..];
        return $"{name} {year}";
    }
}
