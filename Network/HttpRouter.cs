using System.Net;
using System.Text;
using System.Text.Json;
using RenegadeServer.Logging;
using RenegadeServer.Xeno;

namespace RenegadeServer.Network;

public class HttpRouter
{
    private readonly Orchestrator _orch;
    private readonly Service _log;
    private readonly WebSocketHandler _ws;
    private readonly int _port;

    public HttpRouter(Orchestrator orch, Service log, WebSocketHandler ws, int port)
    {
        _orch = orch;
        _log = log;
        _ws = ws;
        _port = port;
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            if (path.StartsWith("/api/")) path = path[4..];

            var body = await ReadBodyAsync(req);
            var query = req.QueryString;

            string respText = "";
            int status = 200;

            if (path == "/health" && method == "GET")
            {
                respText = Json(_orch.GetStatus());
            }
            else if (path == "/version" && method == "GET")
            {
                respText = Json(new { version = _orch.GetVersion() });
            }
            else if (path == "/clients" && method == "GET")
            {
                respText = Json(new { clients = _orch.GetClients() });
            }
            else if (path == "/attach" && method == "POST")
            {
                _orch.Attach();
                respText = Json(new { success = true });
            }
            else if (path == "/execute" && method == "POST")
            {
                var doc = JsonDocument.Parse(body);
                var script = doc.RootElement.TryGetProperty("script", out var s) ? s.GetString() ?? "" : "";
                var pids = doc.RootElement.TryGetProperty("pids", out var p)
                    ? p.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                    : Array.Empty<int>();

                if (string.IsNullOrEmpty(script))
                {
                    respText = Json(new { error = "script is required" });
                    status = 400;
                }
                else if (pids.Length == 0)
                {
                    respText = Json(new { error = "pids is required" });
                    status = 400;
                }
                else
                {
                    _orch.Execute(script, pids);
                    respText = Json(new { success = true });
                }
            }
            else if (path == "/settings" && method == "POST")
            {
                var doc = JsonDocument.Parse(body);
                var id = doc.RootElement.TryGetProperty("settingID", out var idEl) ? idEl.GetInt32() : 0;
                var val = doc.RootElement.TryGetProperty("value", out var valEl) ? valEl.GetInt32() : 0;
                _orch.SetSetting(id, val);
                respText = Json(new { success = true });
            }
            else if (path == "/init" && method == "POST")
            {
                _orch.InitDll();
                respText = Json(new { success = true });
            }
            else if (path == "/stop" && method == "POST")
            {
                _orch.Stop();
                respText = Json(new { success = true });
            }
            else if (path == "/download" && method == "POST")
            {
                try
                {
                    await _orch.DownloadXeno();
                    respText = Json(new { success = true });
                }
                catch (Exception ex)
                {
                    respText = Json(new { success = false, error = ex.Message });
                    status = 500;
                }
            }
            else if (path == "/ensure-xeno" && method == "POST")
            {
                var ok = await _orch.EnsureXeno();
                respText = Json(new { success = ok });
            }
            else if (path == "/download/status" && method == "GET")
            {
                respText = Json(_orch.GetDownloadState());
            }
            else if (path == "/downloaded" && method == "GET")
            {
                respText = Json(new { downloaded = _orch.IsDownloaded() });
            }
            else if (path == "/versions" && method == "GET")
            {
                respText = Json(new { versions = _orch.ListVersions(), active = _orch.GetActiveVersion() });
            }
            else if (path == "/check-updates" && method == "GET")
            {
                var ver = query["version"];
                var result = await _orch.CheckForUpdates(ver);
                respText = Json(result);
            }
            else if (path == "/logs" && method == "GET")
            {
                var limit = 100;
                if (query["limit"] != null) int.TryParse(query["limit"], out limit);
                respText = Json(new { logs = _log.GetLogs(limit) });
            }
            else if (path == "/config" && method == "GET")
            {
                respText = Json(new
                {
                    port = _port,
                    dataDir = _orch.GetDataDir(),
                    xenoDir = _orch.GetXenoDir(),
                    versionsDir = _orch.GetVersionsDir(),
                    version = _orch.GetVersion(),
                    dllLoaded = _orch.IsInitialized(),
                    downloaded = _orch.IsDownloaded(),
                });
            }
            else
            {
                respText = Json(new { error = "Not found" });
                status = 404;
            }

            var buf = Encoding.UTF8.GetBytes(respText);
            resp.StatusCode = status;
            resp.ContentType = "application/json";
            resp.ContentLength64 = buf.Length;
            await resp.OutputStream.WriteAsync(buf);
            resp.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERR] {ex.Message}");
            try
            {
                var resp = context.Response;
                resp.StatusCode = 500;
                var buf = Encoding.UTF8.GetBytes(Json(new { error = ex.Message }));
                resp.ContentType = "application/json";
                resp.ContentLength64 = buf.Length;
                await resp.OutputStream.WriteAsync(buf);
                resp.Close();
            } catch { }
        }
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return "{}";
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string Json(object obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    });
}
