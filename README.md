# RenegadeServer

Standalone C# .NET 8 server for the [Renegade](https://github.com/cookingsometimes/Renegade) Electron wrapper. Loads Xeno.dll and exposes HTTP + WebSocket APIs.

## Features

- **DLL Bridge** — P/Invoke wrapper for Xeno.dll (Initialize, GetClients, Attach, Execute, SetSetting)
- **HTTP API** — 14 endpoints: health, version, clients, attach, execute, settings, logs, config, download status, versions
- **WebSocket** — Real-time events (client changes, log entries, download progress) with channel subscriptions
- **Self-contained** — Single .exe, no .NET runtime required

## Download

Pre-built binaries available in [Releases](https://github.com/cookingsometimes/RenegadeServer/releases).

## Build

```bash
dotnet publish RenegadeServer.csproj -c Release --self-contained -r win-x64 -p:PublishSingleFile=true
```

## CLI Flags

```
--port <number>       HTTP port (default: 8443)
--host <ip>           Bind address (default: 127.0.0.1)
--xeno-path <path>    Custom Xeno.dll path
--no-console          Disable console logging
--log-dir <path>      Custom log directory
--max-logs <number>   Max log files (default: 50)
```

## API Reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Server health check |
| `/version` | GET | Server version |
| `/clients` | GET | List Roblox clients |
| `/attach` | POST | Attach to all clients |
| `/execute` | POST | Execute script (requires `pids`) |
| `/settings` | GET/POST | Xeno settings |
| `/logs` | GET | Log entries |
| `/config` | GET/POST | Server config |
| `/download/status` | GET | Download progress |
| `/downloaded` | GET | Downloaded versions |
| `/versions` | GET | Available versions |
| `/ws` | WebSocket | Real-time event stream |

## License

MIT
