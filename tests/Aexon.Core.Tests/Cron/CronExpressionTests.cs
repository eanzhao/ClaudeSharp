using Aexon.Core.Cron;

namespace Aexon.Core.Tests.Cron;

public sealed class CronExpressionTests
{
    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("30 2 1 * *")]
    [InlineData("0 0 * * *")]
    [InlineData("0,30 * * * *")]
    [InlineData("0 0 1,15 * *")]
    public void TryParse_ValidExpressions_ReturnsExpression(string expression)
    {
        var result = CronExpression.TryParse(expression);

        Assert.NotNull(result);
        Assert.Equal(expression, result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("* * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * 32 * *")]
    [InlineData("* * * 0 *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 7")]
    [InlineData("abc * * * *")]
    public void TryParse_InvalidExpressions_ReturnsNull(string expression)
    {
        Assert.Null(CronExpression.TryParse(expression));
    }

    [Fact]
    public void TryParse_NullExpression_ReturnsNull()
    {
        Assert.Null(CronExpression.TryParse(null!));
    }

    [Fact]
    public void NextOccurrence_EveryFiveMinutes_ReturnsCorrectTime()
    {
        var cron = CronExpression.TryParse("*/5 * * * *")!;
        var from = new DateTimeOffset(2026, 4, 15, 10, 3, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 10, 5, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_AtExactMatch_ReturnsNextOccurrence()
    {
        var cron = CronExpression.TryParse("0 * * * *")!;
        var from = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_WeekdaysOnly_SkipsWeekend()
    {
        var cron = CronExpression.TryParse("0 9 * * 1-5")!;
        // 2026-04-18 is Saturday
        var from = new DateTimeOffset(2026, 4, 17, 18, 0, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        // Should be Monday 2026-04-20
        Assert.Equal(new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_MonthlyFirstDay_ReturnsNextMonth()
    {
        var cron = CronExpression.TryParse("30 2 1 * *")!;
        var from = new DateTimeOffset(2026, 4, 1, 3, 0, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 2, 30, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_ListValues_ReturnsClosestMatch()
    {
        var cron = CronExpression.TryParse("0,30 * * * *")!;
        var from = new DateTimeOffset(2026, 4, 15, 10, 10, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 10, 30, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_RangeWithStep_ReturnsCorrectValue()
    {
        var cron = CronExpression.TryParse("0-30/10 * * * *")!;
        var from = new DateTimeOffset(2026, 4, 15, 10, 5, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 10, 10, 0, TimeSpan.Zero), next.Value);
    }

    [Fact]
    public void NextOccurrence_CrossYearBoundary_ReturnsNextYear()
    {
        var cron = CronExpression.TryParse("0 0 1 1 *")!;
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.NextOccurrence(from);

        Assert.NotNull(next);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), next.Value);
    }
}
