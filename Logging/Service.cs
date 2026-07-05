namespace RenegadeServer.Logging;

public class Service
{
    private readonly List<Entry> _entries = new();
    private readonly List<Action<Entry>> _listeners = new();
    private readonly object _lock = new();
    private readonly string _logDir;
    private int _fileIndex = 0;
    private int _entryCount = 0;
    private const int MAX_ENTRIES_PER_FILE = 50;
    private const int MAX_FILES = 50;

    public Service(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(_logDir);
    }

    public void Log(string level, string source, string message)
    {
        var entry = new Entry
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Source = source,
            Message = message,
        };
        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > 500) _entries.RemoveRange(500, _entries.Count - 500);
            _entryCount++;
            if (_entryCount >= MAX_ENTRIES_PER_FILE)
            {
                _entryCount = 0;
                _fileIndex++;
                if (_fileIndex >= MAX_FILES) _fileIndex = 0;
            }
            WriteLogToFile(entry);
        }
        Console.WriteLine($"[{level.ToUpper()}] [{source}] {message}");
        lock (_lock)
        {
            foreach (var l in _listeners) l(entry);
        }
    }

    private void WriteLogToFile(Entry entry)
    {
        try
        {
            var filePath = Path.Combine(_logDir, $"server-{_fileIndex}.log");
            var line = $"[{entry.Timestamp}] [{entry.Level.ToUpper()}] [{entry.Source}] {entry.Message}\n";
            File.AppendAllText(filePath, line);
        }
        catch { }
    }

    public List<Entry> GetLogs(int limit = 100)
    {
        lock (_lock) { return _entries.Take(limit).ToList(); }
    }

    public Action OnLog(Action<Entry> cb)
    {
        lock (_lock) { _listeners.Add(cb); }
        return () => { lock (_lock) { _listeners.Remove(cb); } };
    }
}
