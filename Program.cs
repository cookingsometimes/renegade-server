using System.Net;
using RenegadeServer.Logging;
using RenegadeServer.Network;
using RenegadeServer.Xeno;

var port = 3420;
string? dataDir = null;
string? xenoDir = null;
string? versionsDir = null;
string? logDir = null;
bool cleanOld = false;

for (int i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (a == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    else if (a == "--data-dir" && i + 1 < args.Length) dataDir = args[++i];
    else if (a == "--xeno-dir" && i + 1 < args.Length) xenoDir = args[++i];
    else if (a == "--versions-dir" && i + 1 < args.Length) versionsDir = args[++i];
    else if (a == "--log-dir" && i + 1 < args.Length) logDir = args[++i];
    else if (a == "--clean") cleanOld = true;
    else if (a == "--help" || a == "-h")
    {
        Console.WriteLine("RenegadeServer - Xeno DLL bridge");
        Console.WriteLine("");
        Console.WriteLine("Usage: RenegadeServer.exe [options]");
        Console.WriteLine("");
        Console.WriteLine("Options:");
        Console.WriteLine("  --port <port>          HTTP port (default: 3420)");
        Console.WriteLine("  --data-dir <path>      Base data directory (default: %APPDATA%/renegade)");
        Console.WriteLine("  --xeno-dir <path>      Directory containing Xeno.dll (default: {data-dir}/xeno)");
        Console.WriteLine("  --versions-dir <path>  Directory for downloaded versions (default: {data-dir}/xeno-versions)");
        Console.WriteLine("  --log-dir <path>       Directory for log files (default: {data-dir}/logs)");
        Console.WriteLine("  --clean                Remove old Xeno versions on startup");
        Console.WriteLine("  --help, -h             Show this help");
        return;
    }
}

var defaultDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "renegade");
dataDir ??= defaultDataDir;
versionsDir ??= Path.Combine(dataDir, "xeno-versions");
xenoDir ??= Path.Combine(dataDir, "xeno");
logDir ??= Path.Combine(dataDir, "logs");

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(versionsDir);
Directory.CreateDirectory(xenoDir);
Directory.CreateDirectory(logDir);

var logService = new Service(logDir);
var orchestrator = new Orchestrator(logService, xenoDir, versionsDir);

if (cleanOld) orchestrator.CleanupOldVersions();

if (orchestrator.IsDownloaded())
{
    try
    {
        orchestrator.InitDll();
        logService.Log("info", "Server", $"Xeno version: {orchestrator.GetVersion()}");
    }
    catch (Exception ex) { logService.Log("error", "Server", $"DLL init failed: {ex.Message}"); }
}
else
{
    logService.Log("info", "Server", "Xeno not found. Download it from the Renegade app.");
}

var wsHandler = new WebSocketHandler(logService, orchestrator);
var router = new HttpRouter(orchestrator, logService, wsHandler, port);

var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
listener.Start();

Console.WriteLine($"RENEGADE_SERVER_PORT:{port}");
Console.WriteLine($"DATA_DIR:{dataDir}");
Console.WriteLine($"XENO_DIR:{xenoDir}");
Console.WriteLine($"VERSIONS_DIR:{versionsDir}");
Console.WriteLine($"LOG_DIR:{logDir}");

logService.Log("info", "Server", $"Listening on port {port}");
logService.Log("info", "Server", $"Data dir: {dataDir}");
logService.Log("info", "Server", $"Xeno dir: {xenoDir}");

_ = Task.Run(async () =>
{
    while (true)
    {
        var context = await listener.GetContextAsync();
        var isWs = context.Request.Headers["Upgrade"]?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true;
        if (isWs)
            _ = Task.Run(() => wsHandler.HandleAsync(context));
        else
            _ = Task.Run(() => router.HandleAsync(context));
    }
});

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (OperationCanceledException) { }
listener.Stop();
