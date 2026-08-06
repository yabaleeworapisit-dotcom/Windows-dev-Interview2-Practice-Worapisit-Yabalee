using System.Globalization;

namespace NasaApodLib;

// Turns the chosen start month and end month into the start_date / end_date pair the APOD
// API expects. The user picks months, never individual days: the range always runs from the
// first day of the start month to the last day of the end month.
//
// Two limits shape the result: the archive begins on 1995-06-16, and the API rejects any date
// later than today. A range that falls partly outside either limit is trimmed rather than
// refused, so picking June 1995 or the current month still returns the days that exist.
public static class ApodDateRange
{
    // The first day an APOD picture was posted, and itself a valid request — the API serves
    // 1995-06-16 ("Neutron Star Earth") and rejects 1995-06-15.
    public static readonly DateOnly ArchiveFirstDay = new(1995, 6, 16);

    private const string ApiDateFormat = "yyyy-MM-dd";

    private const string DisplayDateFormat = "dd-MM-yyyy";

    public static bool TryBuildSpan(
        int StartYear,
        int StartMonth,
        int EndYear,
        int EndMonth,
        out DateOnly StartDate,
        out DateOnly EndDate,
        out string Problem)
    {
        StartDate = default;
        EndDate = default;
        Problem = string.Empty;

        if (StartMonth is < 1 or > 12 || EndMonth is < 1 or > 12)
        {
            Problem = "Pick a month between January and December.";
            return false;
        }

        DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly SpanFirstDay = new(StartYear, StartMonth, 1);
        DateOnly SpanLastDay = new(EndYear, EndMonth, DateTime.DaysInMonth(EndYear, EndMonth));

        // Caught before clamping, so the message names what the user actually chose rather
        // than the trimmed dates they never asked for.
        if (SpanFirstDay > SpanLastDay)
        {
            Problem = "The start date is after the end date. Pick an end month that is not earlier than the start month.";
            return false;
        }

        DateOnly ClampedStart = SpanFirstDay < ArchiveFirstDay ? ArchiveFirstDay : SpanFirstDay;
        DateOnly ClampedEnd = SpanLastDay > Today ? Today : SpanLastDay;

        if (ClampedStart > ClampedEnd)
        {
            Problem = "That range holds no pictures. The archive runs from 16 June 1995 up to today.";
            return false;
        }

        StartDate = ClampedStart;
        EndDate = ClampedEnd;
        return true;
    }

    public static string Date2ApiStr(DateOnly Value)
        => Value.ToString(ApiDateFormat, CultureInfo.InvariantCulture);

    // Day-first, which is how the range is written back to the user on screen. Kept separate
    // from Date2ApiStr so changing what people read can never change what is sent.
    public static string Date2DisplayStr(DateOnly Value)
        => Value.ToString(DisplayDateFormat, CultureInfo.InvariantCulture);

    // Years the pickers should offer: the archive's first year through the current one.
    public static int[] SelectableYears()
    {
        int CurrentYear = DateTime.Now.Year;
        int YearCount = CurrentYear - ArchiveFirstDay.Year + 1;

        int[] RetYears = new int[YearCount];
        for (int Offset = 0; Offset < YearCount; Offset++)
        {
            RetYears[Offset] = CurrentYear - Offset;
        }

        return RetYears;
    }
}
