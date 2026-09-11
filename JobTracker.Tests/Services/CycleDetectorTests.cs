using JobTracker.Services;

namespace JobTracker.Tests.Services;

public class CycleDetectorTests
{
    [Theory]
    [InlineData("Summer 2026 Internship", "Summer 2026")]
    [InlineData("summer, 2026 internship program", "Summer 2026")]
    [InlineData("summer-2026 cohort", "Summer 2026")]
    [InlineData("summer intern 2026", null)] // season/year must be adjacent, not word-separated
    [InlineData("2026 Summer intake", "Summer 2026")]
    [InlineData("Fall 2025 New Grad Program", "Fall 2025")]
    [InlineData("autumn 2025", "Fall 2025")]
    [InlineData("New Grad 2026 SWE", "New Grad 2026")]
    [InlineData("University Grad 2027", "New Grad 2027")]
    [InlineData("No cycle mentioned here", null)]
    public void Detect_extracts_normalized_cycle(string input, string? expected)
    {
        Assert.Equal(expected, CycleDetector.Detect(input));
    }

    [Fact]
    public void SortKey_orders_by_year_then_season()
    {
        var spring26 = CycleDetector.SortKey("Spring 2026");
        var summer26 = CycleDetector.SortKey("Summer 2026");
        var fall25 = CycleDetector.SortKey("Fall 2025");

        Assert.True(fall25.Year < spring26.Year);
        Assert.True(spring26.Season < summer26.Season);
    }
}
