namespace PBZ.Core.Suite;

// Looks in UE4SS.log for a PBZSuite script error since the mod last started. Such an error stops the rest of
// main.lua, while loops registered before it keep running, so the suite can look half alive (telemetry fine, a
// module silently missing). The manager shows the error instead of a vague "waiting".
public static class SuiteLogCheck
{
    private const string StartMarker = "Mod 'PBZSuite' has enabled.txt, starting mod.";
    private const string ErrorMarker = "Error executing script:";

    // The first error line (and the one after it, which usually holds the message), or null if the last start was clean.
    public static string? LastStartError(string logText)
    {
        var start = logText.LastIndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        // Other mods (the probe) can log their own errors in between; skip those.
        for (var err = logText.IndexOf(ErrorMarker, start, StringComparison.Ordinal); err >= 0;
             err = logText.IndexOf(ErrorMarker, err + ErrorMarker.Length, StringComparison.Ordinal))
        {
            var lineEnd = logText.IndexOf('\n', err);
            var nextEnd = lineEnd < 0 ? -1 : logText.IndexOf('\n', lineEnd + 1);
            var text = logText[err..(nextEnd < 0 ? logText.Length : nextEnd)];
            if (text.Contains("PBZSuite", StringComparison.OrdinalIgnoreCase)) return text.Replace("\r", "").Trim();
        }
        return null;
    }

    // Reads the tail of the log without locking it (UE4SS keeps it open while the game runs).
    public static string? ReadTail(string logFile, int maxBytes = 512 * 1024)
    {
        try
        {
            using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > maxBytes) fs.Seek(-maxBytes, SeekOrigin.End);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
