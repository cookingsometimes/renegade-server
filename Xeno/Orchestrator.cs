using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using RenegadeServer.Logging;

namespace RenegadeServer.Xeno;

public class Orchestrator
{
    private readonly Service _log;
    private readonly string _xenoDir;
    private readonly string _versionsDir;
    private string? _dllPath;
    private string _currentVersion = "";
    private bool _initialized;
    private bool _autoDownloading;
    private string _downloadState = "idle";
    private int _downloadProgress;
    private string _downloadError = "";
    private readonly List<Action<string, object>> _eventListeners = new();

    private const string SUMI_API = "https://sumi-api.netlify.app/api/v0/rblx/executors/dl/xeno";
    private const int MAX_RETRIES = 3;

    public Orchestrator(Service log, string xenoDir, string versionsDir)
    {
        _log = log;
        _xenoDir = xenoDir;
        _versionsDir = versionsDir;
        _dllPath = FindDll();
    }

    private string? FindDll()
    {
        var direct = Path.Combine(_xenoDir, "Xeno.dll");
        if (File.Exists(direct)) return direct;

        if (Directory.Exists(_versionsDir))
        {
            foreach (var dir in Directory.GetDirectories(_versionsDir).OrderByDescending(d => d))
            {
                var candidate = Path.Combine(dir, "Xeno.dll");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public bool IsDownloaded() => _dllPath != null && File.Exists(_dllPath);
    public bool IsInitialized() => _initialized;
    public string GetActiveVersion() => _currentVersion;
    public string GetXenoDir() => _xenoDir;
    public string GetVersionsDir() => _versionsDir;
    public string GetDataDir() => Path.GetDirectoryName(_versionsDir) ?? _versionsDir;

    public string[] ListVersions()
    {
        if (!Directory.Exists(_versionsDir)) return Array.Empty<string>();
        return Directory.GetDirectories(_versionsDir)
            .Select(d => Path.GetFileName(d))
            .ToArray();
    }

    public void InitDll()
    {
        if (_initialized) return;
        if (_dllPath == null) throw new Exception("Xeno.dll not found");

        NativeLibrary.SetDllImportResolver(typeof(Orchestrator).Assembly, (name, assembly, _) =>
        {
            if (name == "Xeno") return NativeLibrary.Load(_dllPath);
            return IntPtr.Zero;
        });

        Bridge.Initialize(false);
        _initialized = true;
        _currentVersion = Bridge.GetVersion();
        _log.Log("info", "Orchestrator", $"DLL initialized, version: {_currentVersion}");
    }

    public string GetVersion() => _initialized ? Bridge.GetVersion() : _currentVersion;
    public string GetClients() => _initialized ? Bridge.GetClientsJson() : "[]";
    public void Attach() { if (_initialized) Bridge.Attach(); }
    public void Execute(string script, int[] pids) { if (_initialized) Bridge.Execute(script, pids); }
    public void SetSetting(int id, int val) { if (_initialized) Bridge.SetSetting(id, val); }

    public object GetStatus() => new
    {
        status = "ok",
        mode = "dll",
        version = GetVersion(),
        dllLoaded = _initialized,
        initialized = _initialized,
        clients = _initialized ? Bridge.GetClientsJson() : "[]",
    };

    public async Task DownloadXeno()
    {
        if (_autoDownloading) { while (_autoDownloading) await Task.Delay(500); return; }
        _autoDownloading = true;

        try
        {
            SetDownloadState("fetching_url");
            var (url, version) = await GetDownloadUrl();
            var versionDir = Path.Combine(_versionsDir, version);
            var zipPath = Path.Combine(versionDir, "Xeno.zip");

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    SetDownloadState("downloading", 0);
                    _log.Log("info", "Download", $"Attempt {attempt}/{MAX_RETRIES} - Version: {version}");

                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    var bytes = await http.GetByteArrayAsync(url);
                    Directory.CreateDirectory(versionDir);
                    await File.WriteAllBytesAsync(zipPath, bytes);

                    SetDownloadState("extracting", 100);
                    await ExtractXeno(zipPath, versionDir);

                    _dllPath = Path.Combine(versionDir, "Xeno.dll");
                    _currentVersion = version;
                    SetDownloadState("idle", 100);
                    _log.Log("info", "Download", $"Complete: {version}");
                    return;
                }
                catch (Exception ex)
                {
                    _log.Log("error", "Download", $"Attempt {attempt} failed: {ex.Message}");
                    if (attempt < MAX_RETRIES) await Task.Delay(2000 * attempt);
                    else throw;
                }
            }
        }
        finally { _autoDownloading = false; }
    }

    public async Task<bool> EnsureXeno()
    {
        if (IsDownloaded()) return true;
        try { await DownloadXeno(); return IsDownloaded(); }
        catch { return false; }
    }

    private async Task<(string url, string version)> GetDownloadUrl()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var json = await http.GetStringAsync(SUMI_API);
        var doc = JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        foreach (var hit in hits.EnumerateArray())
        {
            if (hit.GetProperty("handler").GetString() == "relativeZipPath")
            {
                var file = hit.GetProperty("file").GetString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(file, @"v([\d.]+)");
                var ver = match.Success ? match.Groups[1].Value : "unknown";
                return (hit.GetProperty("url").GetString()!, ver);
            }
        }
        throw new Exception("No ZIP hit in API response");
    }

    private async Task ExtractXeno(string zipPath, string targetDir)
    {
        var tempDir = Path.Combine(targetDir, "_temp_extract");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        _log.Log("info", "Download", $"Extracting to: {tempDir}");
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir, true));

        var extracted = Directory.GetDirectories(tempDir)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "Xeno.dll")))
            ?? throw new Exception("No Xeno.dll found in archive");

        foreach (var file in Directory.GetFiles(extracted))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(extracted))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));

        Directory.Delete(tempDir, true);
        try { File.Delete(zipPath); } catch { }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public async Task<object> CheckForUpdates(string? currentVersion)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var v = currentVersion ?? _currentVersion;
            var json = await http.GetStringAsync($"{SUMI_API}?myversion={v}");
            var doc = JsonDocument.Parse(json);
            return new
            {
                needsUpdate = doc.RootElement.TryGetProperty("needsUpdate", out var nu) && nu.GetBoolean(),
                latestVersion = doc.RootElement.TryGetProperty("latestVersion", out var lv) ? lv.GetString() ?? v : v,
            };
        }
        catch { return new { needsUpdate = false, latestVersion = _currentVersion }; }
    }

    public void CleanupOldVersions()
    {
        if (!Directory.Exists(_versionsDir)) return;
        var current = Path.GetFullPath(_xenoDir);
        int removed = 0;
        foreach (var dir in Directory.GetDirectories(_versionsDir))
        {
            if (string.Equals(Path.GetFullPath(dir), current, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, true); removed++; } catch { }
        }
        if (removed > 0) _log.Log("info", "Orchestrator", $"Cleaned {removed} old version(s)");
    }

    public void OnEvent(string type, object data)
    {
        foreach (var l in _eventListeners) l(type, data);
    }

    public Action SubscribeToEvents(Action<string, object> cb)
    {
        _eventListeners.Add(cb);
        return () => _eventListeners.Remove(cb);
    }

    private void SetDownloadState(string state, int progress = 0)
    {
        _downloadState = state;
        _downloadProgress = progress;
        OnEvent("download_progress", GetDownloadState());
    }

    public object GetDownloadState() => new
    {
        state = _downloadState,
        progress = _downloadProgress,
        error = _downloadError,
    };

    public bool IsAutoDownloading() => _autoDownloading;

    public void Stop()
    {
        _initialized = false;
        OnEvent("status_change", GetStatus());
    }
}
