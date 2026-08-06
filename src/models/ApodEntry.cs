using System.Text.Json.Serialization;

namespace NasaApodApp.Models;

// One "Astronomy Picture of the Day" record, exactly as the NASA APOD API returns it.
// Property names follow the API's snake_case field names through JsonPropertyName so the
// mapping stays readable when the response is compared against the API documentation.
public sealed class ApodEntry
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    // Standard-resolution media address. For media_type "image" this is a picture;
    // for "video" it is an embeddable player address, which is why it is never fed
    // straight into an Image control without checking MediaType first.
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    // High-resolution picture address. The API omits this field for video entries.
    [JsonPropertyName("hdurl")]
    public string? HdUrl { get; set; }

    // Either "image" or "video".
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    // A still frame for a video entry. NASA only fills this in when the request carried
    // thumbs=True, which ApodApiClient always sends — without it a video day has no picture
    // at all to put on its slide.
    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("service_version")]
    public string? ServiceVersion { get; set; }

    // Absent on public-domain entries, which is most NASA-produced imagery.
    [JsonPropertyName("copyright")]
    public string? Copyright { get; set; }

    [JsonIgnore]
    public bool IsImage => string.Equals(this.MediaType, "image", StringComparison.OrdinalIgnoreCase);

    // The address of something that can actually be drawn on screen.
    //
    // For a picture this is Url, the roughly 1024-pixel-wide copy — deliberately not HdUrl.
    // HdUrl is the print-resolution original and is far heavier for no visible gain on screen:
    // across the first eight days of January 2026 the high-resolution files total 15.5 MB
    // against 2.3 MB for these, and one single day is 7.8 MB against 488 KB. The full-size
    // address is still shown in the detail panel for anyone who wants it.
    //
    // For a video this is the thumbnail, because Url points at the video itself.
    [JsonIgnore]
    public string DisplayUrl
        => this.IsImage
            ? (string.IsNullOrWhiteSpace(this.Url) ? (this.HdUrl ?? string.Empty) : this.Url)
            : (this.ThumbnailUrl ?? string.Empty);

    // A video NASA hosts itself, as a file rather than an embedded player. These can be played
    // directly; a YouTube or Vimeo entry cannot, because its Url is a page for a browser to
    // load rather than a stream. NASA only produces thumbnail_url for the embedded kind, which
    // is why a self-hosted video arrives with no thumbnail at all and has to play instead.
    [JsonIgnore]
    public bool IsPlayableVideo
        => !this.IsImage
        && (this.Url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || this.Url.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase)
            || this.Url.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            || this.Url.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase));

    // False for a video whose thumbnail NASA did not supply. Such a day is not necessarily
    // blank — if IsPlayableVideo is true the video itself fills the slide instead.
    [JsonIgnore]
    public bool HasPicture => !string.IsNullOrWhiteSpace(this.DisplayUrl);

    // Nothing to draw and nothing to play: an embedded video with no thumbnail supplied.
    [JsonIgnore]
    public bool HasNothingToShow => !this.HasPicture && !this.IsPlayableVideo;

    // The four fields below can legitimately be absent in a response. These say so in words,
    // so the detail panel never shows an empty row that reads like a loading failure.
    [JsonIgnore]
    public string CopyrightText
        => string.IsNullOrWhiteSpace(this.Copyright) ? "(public domain)" : this.Copyright.Trim();

    [JsonIgnore]
    public string HdUrlText
        => string.IsNullOrWhiteSpace(this.HdUrl) ? "(not supplied)" : this.HdUrl;

    // Three distinct empty cases, kept apart because "(not a video)" on a video entry reads
    // as a defect rather than as an explanation.
    [JsonIgnore]
    public string ThumbnailUrlText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(this.ThumbnailUrl))
            {
                return this.ThumbnailUrl;
            }

            if (this.IsImage)
            {
                return "(not a video)";
            }

            return "(none — NASA only supplies thumbnails for embedded videos, not for its own video files)";
        }
    }

    [JsonIgnore]
    public string ServiceVersionText
        => string.IsNullOrWhiteSpace(this.ServiceVersion) ? "(not supplied)" : this.ServiceVersion;
}
