using System.Globalization;
using System.Net;
using System.Net.Http;
using NasaApodApp.Common;

namespace NasaApodLib;

// Raised when the API answers but the answer is not usable — a rejected key, an exhausted
// rate limit, or a date the archive does not cover. The message is written for the person
// using the app, so the view model can show it without rewording.
public sealed class ApodApiException : Exception
{
    public ApodApiException(string message)
        : base(message)
    {
    }

    public ApodApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// The single place that talks HTTP to NASA. It builds the request address, sends it, and
// hands back the raw response body — reading the body into objects is ApodJsonReader's job.
public sealed class ApodApiClient : IDisposable
{
    private const string ApodEndpoint = "https://api.nasa.gov/planetary/apod";

    private const int ApiTimeoutSeconds = 60;

    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public ApodApiClient(string ApiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);

        this.apiKey = ApiKey;
        this.httpClient = new HttpClient
        {
            // Sixty rather than thirty. The call itself takes about four seconds on this link,
            // so thirty looked like room to spare — until a picture or video download ran
            // beside it and the same call measured thirty-seven. Those downloads are now
            // stopped before a request goes out, and this is the margin for the times when
            // the network is simply slow.
            Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds),
        };
    }

    // Example of the address this produces:
    // https://api.nasa.gov/planetary/apod?api_key=your-api-key&start_date=2022-09-01&end_date=2022-09-30&thumbs=True
    //
    // thumbs=True is always sent. It costs nothing on picture days — the response is
    // byte-for-byte the same — and on video days it adds thumbnail_url, which is the only
    // way such a day gets anything to show on its slide.
    //
    // Deliberately not sent: date and count, which the API rejects alongside a start/end
    // pair, and hd, which its documentation states is ignored outright.
    public string BuildRangeUrl(DateOnly StartDate, DateOnly EndDate)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}?api_key={1}&start_date={2}&end_date={3}&thumbs=True",
            ApodEndpoint,
            Uri.EscapeDataString(this.apiKey),
            ApodDateRange.Date2ApiStr(StartDate),
            ApodDateRange.Date2ApiStr(EndDate));

    public async Task<string> FetchRangeJsonAsync(DateOnly StartDate, DateOnly EndDate, CancellationToken CancelToken)
    {
        string RequestUrl = this.BuildRangeUrl(StartDate, EndDate);

        // Logged whole, key included, so the line can be pasted straight into a browser or
        // curl to compare what the app asked for against what the API answers on its own.
        // The key therefore reaches the console and the debug output verbatim.

        // Full URL:
        LayerLogger.Log(ApiId.FetchRange, Layers.Service, Layers.NasaApi, $"GET {RequestUrl}"); 

        HttpResponseMessage Response;
        try
        {
            Response = await this.httpClient.GetAsync(new Uri(RequestUrl), CancelToken).ConfigureAwait(false);
        }
        catch (HttpRequestException NetworkFailure)
        {
            throw new ApodApiException(
                "Could not reach api.nasa.gov. Check the network connection and try again.",
                NetworkFailure);
        }
        catch (TaskCanceledException Timeout) when (!CancelToken.IsCancellationRequested)
        {
            throw new ApodApiException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The request to api.nasa.gov timed out after {0} seconds. Anything already loaded is still on screen.",
                    ApiTimeoutSeconds),
                Timeout);
        }

        using (Response)
        {
            string ResponseBody = await Response.Content.ReadAsStringAsync(CancelToken).ConfigureAwait(false);

            LayerLogger.Log(
                ApiId.FetchRange,
                Layers.NasaApi,
                Layers.Service,
                $"HTTP {(int)Response.StatusCode} {Response.StatusCode}, {ResponseBody.Length} characters, "
                + $"rate limit {ReadRateLimit(Response)}");

            LayerLogger.LogBlock(ApiId.FetchRange, "Response body from api.nasa.gov", ResponseBody);

            if (!Response.IsSuccessStatusCode)
            {
                throw new ApodApiException(DescribeFailure(Response.StatusCode));
            }

            return ResponseBody;
        }
    }

    public void Dispose() => this.httpClient.Dispose();

    // NASA reports the hourly allowance in response headers. Worth logging: an exhausted key
    // is the most common reason a request that used to work stops working.
    private static string ReadRateLimit(HttpResponseMessage Response)
    {
        bool HasLimit = Response.Headers.TryGetValues("X-RateLimit-Limit", out IEnumerable<string>? LimitValues);
        bool HasRemaining = Response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? RemainingValues);

        if (!HasLimit || !HasRemaining)
        {
            return "(not reported)";
        }

        return $"{RemainingValues!.FirstOrDefault()} of {LimitValues!.FirstOrDefault()} left this hour";
    }

    private static string DescribeFailure(HttpStatusCode StatusCode)
    {
        string RetMessage = StatusCode switch
        {
            HttpStatusCode.Forbidden =>
                "NASA rejected the API key. Check ApodApiConfig.json next to the program.",
            HttpStatusCode.TooManyRequests =>
                "The hourly request limit for this API key is used up. Wait an hour, or use a personal key instead of DEMO_KEY.",
            HttpStatusCode.BadRequest =>
                "NASA could not read the requested date range. Pick a month between June 1995 and today.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "NASA answered with HTTP {0} ({1}).",
                (int)StatusCode,
                StatusCode),
        };

        return RetMessage;
    }
}
