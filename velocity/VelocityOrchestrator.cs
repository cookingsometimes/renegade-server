using System.Reflection;
using RenegadeServer.Logging;

namespace RenegadeServer.Velocity;

public class VelocityOrchestrator
{
    private readonly Service _log;
    private readonly string _velocityDir;
    private Assembly? _velocityAssembly;
    private object? _velApi;
    private bool _initialized;
    private string _currentVersion = "";
    private readonly List<Action<string, object>> _eventListeners = new();

    private Type? _velApiType;
    private Type? _statesType;

    public VelocityOrchestrator(Service log, string velocityDir)
    {
        _log = log;
        _velocityDir = velocityDir;
        _currentVersion = ReadVersion();
    }

    private string ReadVersion()
    {
        var path = Path.Combine(_velocityDir, "version.txt");
        if (!File.Exists(path)) return "unknown";
        return File.ReadAllText(path).Trim();
    }

    public string GetVersion() => _currentVersion;
    public string GetVelocityDir() => _velocityDir;
    public bool IsInitialized() => _initialized;
    public bool IsAvailable() => File.Exists(Path.Combine(_velocityDir, "VelocityApi.dll"));

    public void LoadDll()
    {
        if (_initialized) return;

        var dllPath = Path.Combine(_velocityDir, "VelocityApi.dll");
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("VelocityApi.dll not found", dllPath);

        _log.Log("info", "Velocity", $"Loading DLL: {dllPath}");

        _velocityAssembly = Assembly.LoadFrom(dllPath);
        _velApiType = _velocityAssembly.GetType("VelocityAPI.VelAPI");
        _statesType = _velocityAssembly.GetType("VelocityAPI.VelocityStates");

        if (_velApiType == null)
            throw new Exception("Type VelocityAPI.VelAPI not found in assembly");

        _velApi = Activator.CreateInstance(_velApiType);
        if (_velApi == null)
            throw new Exception("Failed to create VelocityAPI.VelAPI instance");

        _initialized = true;
        _log.Log("info", "Velocity", $"DLL loaded, version: {_currentVersion}");
        OnEvent("status_change", GetStatus());
    }

    public void StartCommunication()
    {
        if (!_initialized || _velApi == null || _velApiType == null) return;
        try
        {
            var prevCwd = Environment.CurrentDirectory;
            Environment.CurrentDirectory = _velocityDir;
            var method = _velApiType.GetMethod("StartCommunication");
            method?.Invoke(_velApi, null);
            Environment.CurrentDirectory = prevCwd;
            _log.Log("info", "Velocity", "Communication started");
        }
        catch (Exception ex)
        {
            Environment.CurrentDirectory = _velocityDir;
            _log.Log("error", "Velocity", $"StartCommunication failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    public void StopCommunication()
    {
        if (!_initialized || _velApi == null || _velApiType == null) return;
        try
        {
            var prevCwd = Environment.CurrentDirectory;
            Environment.CurrentDirectory = _velocityDir;
            var method = _velApiType.GetMethod("StopCommunication");
            method?.Invoke(_velApi, null);
            Environment.CurrentDirectory = prevCwd;
            _log.Log("info", "Velocity", "Communication stopped");
        }
        catch (Exception ex)
        {
            _log.Log("error", "Velocity", $"StopCommunication failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    public async Task<string> Attach(int pid)
    {
        if (!_initialized || _velApi == null || _velApiType == null)
            return "not_initialized";

        try
        {
            var method = _velApiType.GetMethod("Attach");
            if (method == null) return "method_not_found";

            var task = (Task)method.Invoke(_velApi, new object[] { pid })!;
            await task;

            var resultProp = task.GetType().GetProperty("Result");
            var result = resultProp?.GetValue(task);
            var stateName = result?.ToString() ?? "unknown";

            _log.Log("info", "Velocity", $"Attach PID {pid}: {stateName}");
            OnEvent("velocity_status", new { pid, status = stateName });
            return stateName;
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            _log.Log("error", "Velocity", $"Attach failed: {msg}");
            OnEvent("velocity_error", new { pid, error = msg });
            return "error";
        }
    }

    public string Execute(string script)
    {
        if (!_initialized || _velApi == null || _velApiType == null)
            return "not_initialized";

        try
        {
            var method = _velApiType.GetMethod("Execute");
            if (method == null) return "method_not_found";

            var result = method.Invoke(_velApi, new object[] { script });
            var stateName = result?.ToString() ?? "unknown";

            _log.Log("info", "Velocity", $"Execute: {stateName}");
            OnEvent("velocity_executed", new { status = stateName });
            return stateName;
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            _log.Log("error", "Velocity", $"Execute failed: {msg}");
            OnEvent("velocity_error", new { error = msg });
            return "error";
        }
    }

    public bool IsAttached(int pid)
    {
        if (!_initialized || _velApi == null || _velApiType == null) return false;
        try
        {
            var method = _velApiType.GetMethod("IsAttached");
            if (method == null) return false;
            var result = method.Invoke(_velApi, new object[] { pid });
            return result is bool b && b;
        }
        catch { return false; }
    }

    public List<int> GetInjectedPids()
    {
        if (!_initialized || _velApi == null || _velApiType == null) return new();
        try
        {
            var field = _velApiType.GetField("injected_pids");
            if (field == null) return new();
            var list = field.GetValue(_velApi);
            if (list is System.Collections.IList iList)
                return iList.Cast<int>().ToList();
            return new();
        }
        catch { return new(); }
    }

    public string GetVelocityState()
    {
        if (!_initialized || _velApi == null || _velApiType == null) return "not_initialized";
        try
        {
            var field = _velApiType.GetField("VelocityStatus");
            if (field == null) return "unknown";
            var val = field.GetValue(_velApi);
            return val?.ToString() ?? "unknown";
        }
        catch { return "error"; }
    }

    public object GetStatus() => new
    {
        available = IsAvailable(),
        initialized = _initialized,
        version = _currentVersion,
        state = GetVelocityState(),
        injectedPids = GetInjectedPids(),
    };

    public void OnEvent(string type, object data)
    {
        foreach (var l in _eventListeners) l(type, data);
    }

    public Action SubscribeToEvents(Action<string, object> cb)
    {
        _eventListeners.Add(cb);
        return () => _eventListeners.Remove(cb);
    }

    public void Stop()
    {
        StopCommunication();
        _initialized = false;
        _velApi = null;
        _velocityAssembly = null;
        OnEvent("status_change", GetStatus());
    }
}
