using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NasaApodApp.Models;

// The API key the application authenticates with, loaded from ApodApiConfig.json next to the
// executable. The key is kept out of the source tree so the repository can be made public.
public sealed class ApodApiSettings
{
    // NASA's public sandbox key. Works without registration but allows only 30 requests per
    // hour, so it is a fallback for a first run, not the intended way to use the app.
    public const string DemoApiKey = "DEMO_KEY";

    private const string ConfigFileName = "ApodApiConfig.json";

    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; set; } = DemoApiKey;

    // True when the app is running on the shared sandbox key rather than a personal one —
    // the slide screen shows a hint in that case so the low rate limit is not a surprise.
    [JsonIgnore]
    public bool IsDemoKey => string.Equals(this.ApiKey, DemoApiKey, StringComparison.Ordinal);

    public static ApodApiSettings LoadFromAppFolder()
    {
        string AppFolder = AppContext.BaseDirectory;
        string ConfigPath = Path.Combine(AppFolder, ConfigFileName);

        if (!File.Exists(ConfigPath))
        {
            return new ApodApiSettings();
        }

        try
        {
            string ConfigText = File.ReadAllText(ConfigPath);
            ApodApiSettings? ParsedSettings = JsonSerializer.Deserialize<ApodApiSettings>(ConfigText);

            if (ParsedSettings is null || string.IsNullOrWhiteSpace(ParsedSettings.ApiKey))
            {
                return new ApodApiSettings();
            }

            return ParsedSettings;
        }
        catch (JsonException)
        {
            // A malformed config file must not stop the app from starting — fall back to the
            // sandbox key and let the user correct the file.
            return new ApodApiSettings();
        }
        catch (IOException)
        {
            return new ApodApiSettings();
        }
    }
}
