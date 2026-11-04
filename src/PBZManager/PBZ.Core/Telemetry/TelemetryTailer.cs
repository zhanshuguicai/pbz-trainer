namespace PBZ.Core.Telemetry;

// Follows the newest telemetry-*.jsonl in the PBZSuite runtime folder while the game appends to it.
// Only complete lines are returned; a partial last line is kept until its newline arrives.
public sealed class TelemetryTailer
{
    private readonly string _runtimeDir;
    private string? _file;
    private long _offset;
    private string _pending = "";

    public TelemetryTailer(string runtimeDir) => _runtimeDir = runtimeDir;

    public string? CurrentFile => _file;

    public IReadOnlyList<Snapshot> ReadNew()
    {
        var result = new List<Snapshot>();
        var newest = FindNewest();
        if (newest is null) return result;
        if (_file is null)
        {
            // Start at the end of whatever exists: old sessions are history, not live state.
            _file = newest;
            _offset = SafeLength(newest);
            _pending = "";
            return result;
        }
        if (!string.Equals(newest, _file, StringComparison.OrdinalIgnoreCase))
        {
            Drain(_file, result);
            _file = newest;
            _offset = 0;
            _pending = "";
        }
        Drain(_file, result);
        return result;
    }

    private void Drain(string path, List<Snapshot> into)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _offset) { _offset = 0; _pending = ""; }
            fs.Seek(_offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var text = _pending + reader.ReadToEnd();
            _offset = fs.Length;
            var lines = text.Split('\n');
            _pending = lines[^1];
            for (var i = 0; i < lines.Length - 1; i++)
                if (Snapshot.TryParse(lines[i].TrimEnd('\r')) is { } s) into.Add(s);
        }
        catch (IOException)
        {
            // The game may be rotating the file; try again on the next poll.
        }
    }

    // Ordered by name, not timestamps: while the game holds the file open, Windows does not refresh the
    // directory entry's size or write time. Session ids start with 8 hex digits of os.time(), then the part number.
    private string? FindNewest()
    {
        if (!Directory.Exists(_runtimeDir)) return null;
        return Directory.GetFiles(_runtimeDir, "telemetry-*.jsonl")
            .Select(path => (path, key: SortKey(Path.GetFileNameWithoutExtension(path))))
            .Where(x => x.key is not null)
            .OrderByDescending(x => x.key!.Value.session, StringComparer.Ordinal)
            .ThenByDescending(x => x.key!.Value.part)
            .Select(x => x.path)
            .FirstOrDefault();
    }

    internal static (string session, int part)? SortKey(string name)
    {
        var bits = name.Split('-');
        if (bits.Length != 3 || bits[0] != "telemetry" || !int.TryParse(bits[2], out var part)) return null;
        return (bits[1], part);
    }

    private static long SafeLength(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return fs.Length;
        }
        catch (IOException) { return 0; }
    }
}
