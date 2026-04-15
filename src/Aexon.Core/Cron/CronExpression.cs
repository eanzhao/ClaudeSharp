namespace Aexon.Core.Cron;

/// <summary>
/// Parses and evaluates standard 5-field cron expressions.
/// Fields: minute hour day-of-month month day-of-week.
/// Supports *, ranges (1-5), steps (*/15), lists (1,3,5), and combined (1-5/2).
/// </summary>
public sealed class CronExpression
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _daysOfMonth;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _daysOfWeek;
    private readonly string _raw;

    private CronExpression(
        string raw,
        HashSet<int> minutes,
        HashSet<int> hours,
        HashSet<int> daysOfMonth,
        HashSet<int> months,
        HashSet<int> daysOfWeek)
    {
        _raw = raw;
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    /// <summary>
    /// Parses a cron expression string. Returns null when the expression is invalid.
    /// </summary>
    public static CronExpression? TryParse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return null;

        var minutes = ParseField(parts[0], 0, 59);
        var hours = ParseField(parts[1], 0, 23);
        var daysOfMonth = ParseField(parts[2], 1, 31);
        var months = ParseField(parts[3], 1, 12);
        var daysOfWeek = ParseField(parts[4], 0, 6);

        if (minutes == null || hours == null || daysOfMonth == null ||
            months == null || daysOfWeek == null)
        {
            return null;
        }

        return new CronExpression(
            expression.Trim(),
            minutes,
            hours,
            daysOfMonth,
            months,
            daysOfWeek);
    }

    /// <summary>
    /// Computes the next occurrence strictly after <paramref name="from"/>.
    /// Returns null when no match is found within 4 years (guard against impossible expressions).
    /// </summary>
    public DateTimeOffset? NextOccurrence(DateTimeOffset from)
    {
        var candidate = new DateTimeOffset(
            from.Year, from.Month, from.Day,
            from.Hour, from.Minute, 0,
            from.Offset).AddMinutes(1);

        var limit = from.AddYears(4);

        while (candidate < limit)
        {
            if (!_months.Contains(candidate.Month))
            {
                candidate = NextMonth(candidate);
                continue;
            }

            if (!_daysOfMonth.Contains(candidate.Day) ||
                !_daysOfWeek.Contains((int)candidate.DayOfWeek))
            {
                candidate = candidate.AddDays(1);
                candidate = new DateTimeOffset(
                    candidate.Year, candidate.Month, candidate.Day,
                    0, 0, 0, candidate.Offset);
                continue;
            }

            if (!_hours.Contains(candidate.Hour))
            {
                candidate = candidate.AddHours(1);
                candidate = new DateTimeOffset(
                    candidate.Year, candidate.Month, candidate.Day,
                    candidate.Hour, 0, 0, candidate.Offset);
                continue;
            }

            if (!_minutes.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Returns the original cron expression string.
    /// </summary>
    public override string ToString() => _raw;

    private static DateTimeOffset NextMonth(DateTimeOffset dt)
    {
        var next = dt.Month == 12
            ? new DateTimeOffset(dt.Year + 1, 1, 1, 0, 0, 0, dt.Offset)
            : new DateTimeOffset(dt.Year, dt.Month + 1, 1, 0, 0, 0, dt.Offset);
        return next;
    }

    private static HashSet<int>? ParseField(string field, int min, int max)
    {
        var values = new HashSet<int>();

        foreach (var segment in field.Split(','))
        {
            if (!ParseSegment(segment.Trim(), min, max, values))
                return null;
        }

        return values.Count == 0 ? null : values;
    }

    private static bool ParseSegment(string segment, int min, int max, HashSet<int> values)
    {
        if (string.IsNullOrEmpty(segment))
            return false;

        var stepParts = segment.Split('/', 2);
        var rangePart = stepParts[0];
        int step = 1;

        if (stepParts.Length == 2)
        {
            if (!int.TryParse(stepParts[1], out step) || step < 1)
                return false;
        }

        int rangeStart, rangeEnd;

        if (rangePart == "*")
        {
            rangeStart = min;
            rangeEnd = max;
        }
        else if (rangePart.Contains('-'))
        {
            var bounds = rangePart.Split('-', 2);
            if (!int.TryParse(bounds[0], out rangeStart) ||
                !int.TryParse(bounds[1], out rangeEnd))
            {
                return false;
            }

            if (rangeStart < min || rangeEnd > max || rangeStart > rangeEnd)
                return false;
        }
        else
        {
            if (!int.TryParse(rangePart, out rangeStart))
                return false;
            if (rangeStart < min || rangeStart > max)
                return false;
            rangeEnd = rangeStart;
        }

        for (var i = rangeStart; i <= rangeEnd; i += step)
            values.Add(i);

        return true;
    }
}
