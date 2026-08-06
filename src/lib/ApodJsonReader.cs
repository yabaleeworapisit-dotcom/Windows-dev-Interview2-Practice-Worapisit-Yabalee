using System.Text.Json;
using NasaApodApp.Models;

namespace NasaApodLib;

// Reads the response body from ApodApiClient into ApodEntry objects.
//
// The APOD endpoint answers in two different shapes: a JSON array when a date range was
// requested, and a bare JSON object when only one day matched the range. Both are accepted
// here so a one-day month (June 1995, or the first of a month that has only started) does
// not fail to parse.
public static class ApodJsonReader
{
    private static readonly JsonSerializerOptions ReaderOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ApodEntry[] Json2Entries(string ResponseBody)
    {
        ArgumentNullException.ThrowIfNull(ResponseBody);

        string TrimmedBody = ResponseBody.TrimStart();
        if (TrimmedBody.Length == 0)
        {
            return [];
        }

        try
        {
            bool IsArrayResponse = TrimmedBody[0] == '[';

            ApodEntry[] ParsedEntries = IsArrayResponse
                ? JsonSerializer.Deserialize<ApodEntry[]>(ResponseBody, ReaderOptions) ?? []
                : SingleEntry2Array(ResponseBody);

            // NASA returns the range oldest-first; keeping that order means slide 1 is the
            // first of the month, which is what someone browsing a month expects.
            ApodEntry[] RetEntries = [.. ParsedEntries.OrderBy(Entry => Entry.Date, StringComparer.Ordinal)];

            return RetEntries;
        }
        catch (JsonException ParseFailure)
        {
            throw new ApodApiException(
                "The answer from NASA was not in the expected format and could not be read.",
                ParseFailure);
        }
    }

    private static ApodEntry[] SingleEntry2Array(string ResponseBody)
    {
        ApodEntry? OneEntry = JsonSerializer.Deserialize<ApodEntry>(ResponseBody, ReaderOptions);

        return OneEntry is null ? [] : [OneEntry];
    }
}
