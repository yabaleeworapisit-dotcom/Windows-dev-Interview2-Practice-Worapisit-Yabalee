using System.Globalization;

namespace NasaApodApp.Common;

// Layer names used as the source and destination of a logged hop.
public static class Layers
{
    public const string Service = "Service";

    public const string NasaApi = "NasaApi";
}

// Identifiers for each request flow, so one transaction can be followed across layers in the
// log even when several are in flight at once.
public static class ApiId
{
    public const string FetchRange = "API-001";

    public const string LoadPicture = "API-002";

    public const string LoadVideo = "API-003";
}

// Writes structured lines shaped [HH:mm:ss.ff][apiId][source->destination] text.
//
// Every line goes to two places. Debug.WriteLine is what Visual Studio's Output window shows
// while debugging a windowed application; Console.WriteLine is what appears when the program
// is started from a terminal. Neither needs a console to be created, and writing to Console
// when none is attached goes nowhere harmlessly, so no check guards it.
public static class LayerLogger
{
    // Sized so a single month prints in full: September 2022 comes back as 36,457 characters.
    // A span of many months runs past this and is cut with a notice, because at that point the
    // log has stopped being something a person reads.
    private const int MaxBodyCharacters = 50000;

    public static void Log(string apiId, string srcLayer, string destLayer, string text)
    {
        string Line = $"[{NowText()}][{apiId}][{srcLayer}->{destLayer}] {text}";

        System.Diagnostics.Debug.WriteLine(Line);
        Console.WriteLine(Line);
    }

    // For payloads worth reading on their own: fenced by a titled rule so a response body
    // does not run into the surrounding single-line entries.
    public static void LogBlock(string apiId, string title, string body)
    {
        string Header = $"[{NowText()}][{apiId}] {title}";
        string Rule = new('-', 100);

        string Shown = body.Length > MaxBodyCharacters
            ? string.Concat(
                body.AsSpan(0, MaxBodyCharacters),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}... [cut here — {1} of {2} characters shown]",
                    Environment.NewLine,
                    MaxBodyCharacters,
                    body.Length))
            : body;

        string Block = string.Join(Environment.NewLine, Rule, Header, Rule, Shown, Rule);

        System.Diagnostics.Debug.WriteLine(Block);
        Console.WriteLine(Block);
    }

    private static string NowText()
        => DateTime.Now.ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture);
}
