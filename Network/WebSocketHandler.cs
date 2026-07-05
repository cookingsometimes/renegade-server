using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RenegadeServer.Logging;
using RenegadeServer.Xeno;

namespace RenegadeServer.Network;

public class WebSocketHandler
{
    private readonly Service _log;
    private readonly Orchestrator _orch;
    private readonly List<(WebSocket ws, HashSet<string> channels, CancellationTokenSource cts)> _clients = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WebSocketHandler(Service log, Orchestrator orch)
    {
        _log = log;
        _orch = orch;

        _orch.SubscribeToEvents((type, data) =>
        {
            var msg = JsonSerializer.Serialize(new { type, data }, _jsonOptions);
            Broadcast(type, msg);
        });

        _log.OnLog(entry =>
        {
            var msg = JsonSerializer.Serialize(new { type = "log", data = entry }, _jsonOptions);
            Broadcast("log", msg);
        });
    }

    private void Broadcast(string channel, string msg)
    {
        lock (_clients)
        {
            foreach (var (ws, channels, _) in _clients)
            {
                if (ws.State != WebSocketState.Open) continue;
                if (channels.Contains("*") || channels.Contains(channel))
                {
                    try { _ = ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None); }
                    catch { }
                }
            }
        }
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        var wsCtx = await context.AcceptWebSocketAsync(null);
        var ws = wsCtx.WebSocket;
        var channels = new HashSet<string>();
        var cts = new CancellationTokenSource();

        lock (_clients) { _clients.Add((ws, channels, cts)); }

        var initMsg = JsonSerializer.Serialize(new { type = "status_change", data = _orch.GetStatus() }, _jsonOptions);
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(initMsg), WebSocketMessageType.Text, true, cts.Token); }
        catch { }

        var buf = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buf, 0, result.Count);
                    try
                    {
                        var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("type", out var t))
                        {
                            var type = t.GetString();
                            if (type == "subscribe" && doc.RootElement.TryGetProperty("channels", out var chs))
                                foreach (var ch in chs.EnumerateArray()) channels.Add(ch.GetString() ?? "");
                            else if (type == "unsubscribe" && doc.RootElement.TryGetProperty("channels", out var chs2))
                                foreach (var ch in chs2.EnumerateArray()) channels.Remove(ch.GetString() ?? "");
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        lock (_clients)
        {
            _clients.RemoveAll(c => c.ws == ws);
        }
        try { cts.Cancel(); await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
        catch { }
    }
}
