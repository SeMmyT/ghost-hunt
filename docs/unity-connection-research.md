# Connecting to a Running Unity Instance from an External CLI Tool

**Use case**: Sending commands from a CLI tool (Claude Code / terminal) to a running Unity game -- spawning ghosts, changing maze parameters, triggering game events, modifying GameState at runtime.

**Date**: 2026-03-17

---

## Table of Contents

1. [Unity Remote Debugging (Mono Soft Debugger)](#1-unity-remote-debugging)
2. [Unity Editor Command Line (`-executeMethod`)](#2-unity-editor-command-line)
3. [Unity Profiler Connection](#3-unity-profiler-connection)
4. [Unity Test Framework / UTR](#4-unity-test-framework--utr)
5. [Custom WebSocket Server in Unity](#5-custom-websocket-server-in-unity)
6. [Custom TCP Server in Unity](#6-custom-tcp-server-in-unity)
7. [File-Based IPC (FileSystemWatcher)](#7-file-based-ipc-filesystemwatcher)
8. [Photon Fusion 2 Integration](#8-photon-fusion-2-integration)
9. [Recommendation](#9-recommendation)

---

## 1. Unity Remote Debugging

### How it works

Unity uses the **Mono Soft Debugger protocol** over TCP. When a Unity player or Editor is running with script debugging enabled, it listens on a dynamically assigned TCP port. IDEs (Rider, VS Code, Visual Studio) discover this port automatically via UDP multicast announcements on the local network.

The protocol supports:
- Setting breakpoints
- Inspecting/modifying variables
- Evaluating expressions
- Stepping through code

### Port discovery

- The port is **dynamically assigned** at startup (not configurable)
- iOS debugging uses port **56000** by convention
- The Editor broadcasts its presence via UDP so IDEs can find it
- For remote players, you specify IP + port manually in the IDE

### Enabling in builds

The player must be built with:
- **Development Build** checked
- **Script Debugging** checked
- Optionally: **Wait For Managed Debugger** (pauses at startup until debugger attaches)

CLI flag: `-wait-for-managed-debugger`

### Pros

- No custom code needed in Unity
- Can inspect and modify any variable at runtime
- Works with standard IDE tooling

### Cons

- **Not scriptable from CLI** -- the Mono debug protocol is complex binary, no simple CLI client exists
- Designed for interactive IDE use, not automation
- Dynamically assigned ports make scripted connection unreliable
- Only works in Development Builds (not release)
- High latency for variable evaluation (~50-200ms per expression)
- **Cannot call arbitrary methods** without hitting a breakpoint first

### Verdict for our use case

**Not suitable.** The debug protocol is designed for IDE-driven interactive debugging. There's no practical way to send "spawn a ghost at position X" from a terminal. You'd need to implement a Mono debug protocol client from scratch, which is a massive undertaking for minimal benefit.

---

## 2. Unity Editor Command Line (`-executeMethod`)

### How it works

Unity can be launched from the command line with flags that execute static methods:

```bash
# Windows
"C:\Program Files\Unity\Hub\Editor\6000.x\Editor\Unity.exe" \
  -quit -batchmode \
  -projectPath "C:\path\to\ghost-hunt" \
  -executeMethod MyEditorScript.DoSomething

# macOS/Linux
/path/to/Unity -quit -batchmode \
  -projectPath ~/codeprojects/ghost-hunt \
  -executeMethod MyEditorScript.DoSomething
```

### Key flags

| Flag | Purpose |
|------|---------|
| `-batchmode` | No GUI, no dialogs, minimal logging |
| `-quit` | Exit after method completes |
| `-executeMethod` | Run a static method in an `Editor/` script |
| `-logFile -` | Send log output to stdout (Windows) |
| `-timestamps` | Prefix log lines with timestamp + thread ID |
| `-quitTimeout N` | Seconds to wait for async ops before force-quit (default 300) |

### Script requirements

The method must be:
- In a script inside an `Editor/` folder
- `public static void`
- In a class (not nested)

```csharp
// Assets/Editor/CLICommands.cs
using UnityEditor;
using UnityEngine;

public static class CLICommands
{
    public static void SpawnGhost()
    {
        // Parse custom args
        string[] args = System.Environment.GetCommandLineArgs();
        // Find your custom args after the standard ones
        // e.g., -spawnPos 10,0,5

        Debug.Log("Ghost spawned from CLI!");
        // ... do work ...
    }
}
```

### Custom argument passing

```bash
Unity -batchmode -executeMethod CLICommands.SpawnGhost -spawnPos 10,0,5 -ghostType Chaser
```

Parse with `System.Environment.GetCommandLineArgs()` inside the method.

### Pros

- No extra dependencies
- Built into Unity
- Can run any Editor script logic
- Exit code reflects success/failure

### Cons

- **Launches a new Unity process every time** -- 15-60 second startup
- Cannot connect to an **already running** Editor instance
- Only one Unity instance can have a project open at a time (file locks)
- Editor-only (not available in builds)
- `-batchmode` means no scene rendering -- can't see the effect live
- Blocks the calling terminal until completion

### Verdict for our use case

**Not suitable for live interaction.** Good for CI/CD builds and one-shot automation, but the 15-60s startup per command makes it unusable for interactive game control. You can't send commands to an already-running Editor this way.

---

## 3. Unity Profiler Connection

### How it works

The Unity Profiler connects to players over TCP. The profiler port range is **54998-55511**. For WebGL builds, the connection uses WebSocket (but only outbound from the browser, no incoming).

Data exposed:
- CPU frame time breakdown (per-system, per-method)
- GPU rendering stats
- Memory allocations (managed + native)
- Physics timing
- Audio stats
- Custom profiler counters (via `ProfilerCounter<T>`)

### External tool API

Unity provides a low-level native plugin profiler API:
- `IUnityProfiler` -- add instrumentation events from native plugins
- `IUnityProfilerCallbacks` -- intercept profiler events and redirect to external tools

Headers are at: `<UnityInstallPath>/Editor/Data/PluginAPI/`

### Pros

- Access to detailed performance data
- Custom counters can expose game-specific metrics
- Works in Development Builds

### Cons

- **Read-only** -- you can observe, but cannot send commands back
- Profiler protocol is undocumented and internal
- Only exposes performance metrics, not game state
- Requires Development Build
- No command execution capability

### Verdict for our use case

**Not suitable.** The profiler is observation-only. It can't spawn ghosts or change maze parameters. Useful for performance monitoring but not for game control.

---

## 4. Unity Test Framework / UTR

### How it works

The Unity Test Framework (UTF) integrates NUnit for Edit Mode and Play Mode tests. Tests can be run from CLI:

```bash
Unity -runTests -batchmode \
  -projectPath ~/codeprojects/ghost-hunt \
  -testPlatform PlayMode \
  -testResults ~/results.xml
```

**UTR (Unified Test Runner)** is Unity's internal CI test orchestrator. It provides:
- A single entry point for all test types
- REST API for test execution data
- Database storage for results

**GameDriver** is a third-party tool that enables remote test execution against a running Unity player:
- Connects to a running build via TCP
- Sends input simulation commands
- Queries game object state
- Integrates with UTF for CI/CD

### Pros

- Structured test execution
- CI/CD integration
- GameDriver can connect to running builds

### Cons

- UTF requires launching a new Unity process (same problem as `-executeMethod`)
- UTR is internal to Unity -- not publicly available
- GameDriver is a paid commercial product ($$$)
- Test framework is designed for assertions, not live game control
- High latency for test setup/teardown

### Verdict for our use case

**Not suitable for live control.** GameDriver is the closest fit but it's a paid dependency and designed for QA automation, not creative game direction from a terminal.

---

## 5. Custom WebSocket Server in Unity

### How it works

Embed a WebSocket server inside a MonoBehaviour. It listens on a configurable port and accepts JSON commands from any WebSocket client (browser, CLI tool, Python script, Node.js).

This is the **CUDLR pattern** (Console for Unity Debugging and Logging Remotely) -- a deprecated but well-proven approach that embedded an HTTP/WebSocket server directly in the Unity player.

### Option A: Zero-dependency server

[unity-websocket-server](https://github.com/shaunabanana/unity-websocket-server) -- a minimal, no-dependency WebSocket server for Unity.

```csharp
// Assets/Scripts/Debug/DevConsoleServer.cs
using UnityEngine;
using System;
using System.Collections.Concurrent;

public class DevConsoleServer : WebSocketServer
{
    // Thread-safe queue for dispatching to main thread
    private ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

    public override void OnOpen(WebSocketConnection connection)
    {
        Debug.Log($"[DevConsole] Client connected: {connection.id}");
        connection.Send("{\"status\":\"connected\",\"game\":\"ghost-hunt\"}");
    }

    public override void OnMessage(WebSocketMessage message)
    {
        // Parse JSON command
        var cmd = JsonUtility.FromJson<DevCommand>(message.data);

        // Dispatch to main thread (Unity API is not thread-safe)
        _mainThreadQueue.Enqueue(() => ExecuteCommand(cmd, message.connection));
    }

    public override void OnClose(WebSocketConnection connection)
    {
        Debug.Log($"[DevConsole] Client disconnected: {connection.id}");
    }

    private void Update()
    {
        // Process queued commands on main thread
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            action.Invoke();
        }
    }

    private void ExecuteCommand(DevCommand cmd, WebSocketConnection conn)
    {
        switch (cmd.type)
        {
            case "spawn_ghost":
                SpawnGhost(cmd, conn);
                break;
            case "set_maze_seed":
                SetMazeSeed(cmd, conn);
                break;
            case "trigger_surge":
                TriggerSurge(cmd, conn);
                break;
            case "set_phase":
                SetPhase(cmd, conn);
                break;
            case "get_state":
                SendGameState(conn);
                break;
            default:
                conn.Send($"{{\"error\":\"unknown command: {cmd.type}\"}}");
                break;
        }
    }

    private void SpawnGhost(DevCommand cmd, WebSocketConnection conn)
    {
        // Access GameManager, spawn ghost at position
        var gm = FindFirstObjectByType<GhostHunt.Network.GameManager>();
        // ... spawn logic ...
        conn.Send("{\"result\":\"ghost_spawned\"}");
    }

    // ... other command handlers ...
}

[Serializable]
public struct DevCommand
{
    public string type;
    public string[] args;
    public float x, y, z;
    public int intValue;
    public string stringValue;
}
```

### Option B: websocket-sharp (battle-tested library)

```csharp
// Using websocket-sharp (drop DLL into Assets/Plugins/)
using WebSocketSharp;
using WebSocketSharp.Server;
using UnityEngine;
using System.Collections.Concurrent;

public class GameConsoleService : WebSocketBehavior
{
    public static ConcurrentQueue<(string msg, string clientId)> IncomingMessages
        = new ConcurrentQueue<(string, string)>();

    protected override void OnMessage(MessageEventArgs e)
    {
        IncomingMessages.Enqueue((e.Data, ID));
    }

    protected override void OnOpen()
    {
        Send("{\"status\":\"connected\"}");
    }
}

public class DevConsoleWS : MonoBehaviour
{
    private WebSocketServer _server;
    [SerializeField] private int port = 9090;

    void Start()
    {
        _server = new WebSocketServer(port);
        _server.AddWebSocketService<GameConsoleService>("/console");
        _server.Start();
        Debug.Log($"[DevConsole] WebSocket server on ws://localhost:{port}/console");
    }

    void Update()
    {
        while (GameConsoleService.IncomingMessages.TryDequeue(out var msg))
        {
            ProcessCommand(msg.msg);
        }
    }

    void OnDestroy()
    {
        _server?.Stop();
    }

    private void ProcessCommand(string json)
    {
        // Parse and execute
    }
}
```

### CLI client (any language)

```bash
# Using websocat (Rust CLI tool, install via cargo)
echo '{"type":"spawn_ghost","x":10,"y":0,"z":5}' | websocat ws://localhost:9090/console

# Using Python
python3 -c "
import asyncio, websockets, json, sys
async def send():
    async with websockets.connect('ws://localhost:9090/console') as ws:
        await ws.send(json.dumps({'type': sys.argv[1]}))
        print(await ws.recv())
asyncio.run(send())
" spawn_ghost

# Using Node.js
node -e "
const ws = new (require('ws'))('ws://localhost:9090/console');
ws.on('open', () => ws.send(JSON.stringify({type:'spawn_ghost',x:10,y:0,z:5})));
ws.on('message', d => { console.log(d.toString()); ws.close(); });
"
```

### Shell wrapper for ergonomic CLI

```bash
#!/usr/bin/env bash
# ghost-cmd.sh -- send commands to running Ghost Hunt instance
PORT=${GHOST_HUNT_PORT:-9090}
CMD=${1:?Usage: ghost-cmd <command> [args...]}
shift

case $CMD in
  spawn)   JSON="{\"type\":\"spawn_ghost\",\"x\":${1:-0},\"y\":${2:-0},\"z\":${3:-0}}" ;;
  phase)   JSON="{\"type\":\"set_phase\",\"stringValue\":\"$1\"}" ;;
  surge)   JSON="{\"type\":\"trigger_surge\"}" ;;
  seed)    JSON="{\"type\":\"set_maze_seed\",\"intValue\":$1}" ;;
  state)   JSON="{\"type\":\"get_state\"}" ;;
  *)       JSON="{\"type\":\"$CMD\"}" ;;
esac

echo "$JSON" | websocat -1 "ws://localhost:$PORT/console"
```

### Works in

| Environment | Works? | Notes |
|-------------|--------|-------|
| Editor (Play Mode) | Yes | Full access to all Unity APIs |
| Standalone Build (Dev) | Yes | Must include server code |
| Standalone Build (Release) | Yes* | Strip with `#if DEVELOPMENT_BUILD \|\| UNITY_EDITOR` |
| WebGL | No | Browser sandbox prevents inbound connections |
| Mobile | Yes | Connect over local WiFi, need firewall exception |
| Quest VR | Yes | Same network, use Quest IP |

### Latency

- **Local (same machine)**: <1ms round-trip
- **LAN (same network)**: 1-5ms round-trip
- **WiFi to Quest**: 5-20ms round-trip

### Pros

- Works in both Editor and builds
- Bidirectional -- can push events back to CLI
- JSON protocol is human-readable and debuggable
- WebSocket clients exist in every language
- Can conditionally compile out for release builds
- Persistent connection (no reconnect overhead per command)
- Browser DevTools can connect too (useful for web dashboard)

### Cons

- Must add server code to project
- WebSocket handshake is HTTP-based (slight overhead vs raw TCP)
- websocket-sharp DLL adds ~100KB to build
- Zero-dep server is less battle-tested
- Must handle thread safety (Unity API is main-thread only)
- Port conflicts possible (use configurable port)

---

## 6. Custom TCP Server in Unity

### How it works

A simpler alternative to WebSocket. Embed a `TcpListener` in a MonoBehaviour that accepts newline-delimited JSON commands.

```csharp
// Assets/Scripts/Debug/TcpConsoleServer.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class TcpConsoleServer : MonoBehaviour
{
    [SerializeField] private int port = 9091;

    private TcpListener _listener;
    private Thread _listenThread;
    private ConcurrentQueue<(string cmd, StreamWriter writer)> _commandQueue
        = new ConcurrentQueue<(string, StreamWriter)>();
    private volatile bool _running;

    void Start()
    {
        _running = true;
        _listenThread = new Thread(ListenLoop) { IsBackground = true };
        _listenThread.Start();
        Debug.Log($"[TcpConsole] Listening on port {port}");
    }

    private void ListenLoop()
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();

        while (_running)
        {
            if (!_listener.Pending())
            {
                Thread.Sleep(10);
                continue;
            }

            var client = _listener.AcceptTcpClient();
            // Handle each client in its own thread
            var thread = new Thread(() => HandleClient(client))
            { IsBackground = true };
            thread.Start();
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            writer.WriteLine("{\"status\":\"connected\",\"game\":\"ghost-hunt\"}");

            string line;
            while (_running && (line = reader.ReadLine()) != null)
            {
                _commandQueue.Enqueue((line, writer));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TcpConsole] Client error: {e.Message}");
        }
        finally
        {
            client?.Close();
        }
    }

    void Update()
    {
        while (_commandQueue.TryDequeue(out var item))
        {
            var result = ExecuteCommand(item.cmd);
            try { item.writer.WriteLine(result); }
            catch { /* client disconnected */ }
        }
    }

    private string ExecuteCommand(string json)
    {
        try
        {
            var cmd = JsonUtility.FromJson<DevCommand>(json);
            // ... dispatch commands ...
            return "{\"ok\":true}";
        }
        catch (Exception e)
        {
            return $"{{\"error\":\"{e.Message}\"}}";
        }
    }

    void OnDestroy()
    {
        _running = false;
        _listener?.Stop();
    }
}
```

### CLI client

```bash
# Dead simple -- just netcat
echo '{"type":"spawn_ghost","x":10,"y":0,"z":5}' | nc localhost 9091

# Interactive session
nc localhost 9091
> {"type":"get_state"}
< {"phase":"Hunt","collectibles":42,"ghostScore":0}
> {"type":"trigger_surge"}
< {"ok":true}

# Or socat for more control
socat - TCP:localhost:9091
```

### IL2CPP compatibility warning

`TcpListener` has known issues with IL2CPP builds:
- Socket constructor can throw `ArgumentException` on some platforms
- Async socket operations (`BeginAccept`, `BeginReceive`) have historically been broken
- Workaround: use synchronous operations on background threads (as shown above)
- Modern Unity 6 has fixed many of these, but test on target platforms

### Works in

| Environment | Works? | Notes |
|-------------|--------|-------|
| Editor (Play Mode) | Yes | |
| Standalone (Mono) | Yes | Fully supported |
| Standalone (IL2CPP) | Mostly | Test on target; sync ops more reliable than async |
| WebGL | No | No socket access in browser |
| Mobile | Yes | Local WiFi, need firewall exception |
| Quest VR | Yes | Same network |

### Latency

- **Local**: <1ms (Nagle's algorithm may add up to 200ms -- disable with `client.NoDelay = true`)
- **LAN**: 1-5ms

### Pros

- Zero dependencies -- `System.Net.Sockets` is built into .NET
- Simpler protocol than WebSocket (no HTTP upgrade handshake)
- `nc` / `netcat` is available everywhere -- no client tool needed
- Lower overhead than WebSocket
- Easy to pipe commands from shell scripts

### Cons

- No framing built-in (must implement message boundaries, e.g., newline-delimited)
- No browser client support (browsers can't open raw TCP)
- Thread management is manual (background threads, cleanup)
- Less tooling ecosystem than WebSocket
- Binary protocol if you need it requires more work
- Nagle's algorithm gotcha (set `NoDelay = true`)

---

## 7. File-Based IPC (FileSystemWatcher)

### How it works

An unconventional but dead-simple approach: Unity watches a command file. The CLI tool writes JSON commands to it. Unity picks up changes via `FileSystemWatcher` or polling.

```csharp
// Assets/Scripts/Debug/FileCommandWatcher.cs
using UnityEngine;
using System.IO;

public class FileCommandWatcher : MonoBehaviour
{
    private string _commandFile;
    private float _lastCheck;
    private string _lastContents = "";

    void Start()
    {
        _commandFile = Path.Combine(Application.dataPath, "..", "dev-commands.json");
        // Create empty file if missing
        if (!File.Exists(_commandFile))
            File.WriteAllText(_commandFile, "");
    }

    void Update()
    {
        // Poll every 100ms
        if (Time.unscaledTime - _lastCheck < 0.1f) return;
        _lastCheck = Time.unscaledTime;

        if (!File.Exists(_commandFile)) return;

        var contents = File.ReadAllText(_commandFile);
        if (contents == _lastContents || string.IsNullOrWhiteSpace(contents)) return;

        _lastContents = contents;
        // Clear the file after reading
        File.WriteAllText(_commandFile, "");

        // Process command
        ExecuteCommand(contents);
    }

    private void ExecuteCommand(string json)
    {
        Debug.Log($"[FileCmd] Received: {json}");
        // Parse and dispatch...
    }
}
```

CLI side:

```bash
echo '{"type":"spawn_ghost","x":10,"y":0,"z":5}' > dev-commands.json
```

### Pros

- Absurdly simple -- both sides just read/write files
- No networking, no ports, no firewall issues
- Works in Editor and builds
- Cross-platform
- No dependencies whatsoever

### Cons

- **One-way** -- no response back to CLI (unless you add a response file)
- Polling adds 100ms+ latency
- `FileSystemWatcher` is unreliable on some platforms (known Unity bugs)
- Race conditions on file read/write
- Doesn't scale to multiple clients
- Feels hacky

### Verdict

**Viable as a quick prototype** or fallback when networking is blocked. Not recommended for production use.

---

## 8. Photon Fusion 2 Integration

### How it works in Ghost Hunt

Ghost Hunt uses Photon Fusion 2 in Host Mode. The host is authoritative for all game state (`GameState`, `PlayerState`, `RoleData`). The existing architecture already has:

- `GameManager` (host-authoritative, manages phases and scoring)
- `GameEventResolver` (processes events by priority: teleport > collectible > catch > role swap)
- `LobbyManager` (player connections, role assignment)
- All game mechanics as `NetworkBehaviour` components (WallPhase, HauntingSurge, QuadrantLockdown, etc.)

### Option: CLI tool as a Photon client

A CLI tool could join the Fusion session as a special "admin" client:

```csharp
// Admin client joins with a special role
public enum PlayerRole
{
    // ... existing roles ...
    Admin = 99  // CLI tool, no avatar, no rendering
}
```

The admin client would send RPCs to the host:

```csharp
[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
public void RPC_AdminCommand(string commandJson)
{
    if (/* verify sender is Admin role */)
    {
        var cmd = JsonUtility.FromJson<DevCommand>(commandJson);
        // Execute on host...
    }
}
```

### Headless Fusion client

Photon Fusion supports headless builds. You could build a console-only admin client:

```bash
# Launch headless admin client
./GhostHuntAdmin -batchmode -nographics -session MyRoom -role Admin
```

But this requires building a separate Unity executable for the admin tool.

### Dedicated server with admin channel

For production, Fusion 2 supports dedicated server mode:
- Server has State Authority, no local player
- Hosting providers: Multiplay, Hathora, Edgegap, GameLift
- Each provider has its own orchestration API
- Photon does **not** provide server orchestration

You could add a TCP/WebSocket admin port alongside the Fusion session on the server.

### Pros

- Uses existing network infrastructure
- Admin commands go through the same authority model
- Works across the network (not just localhost)
- Consistent with the host-authoritative architecture

### Cons

- Requires a full Unity runtime for the CLI client (heavy)
- Building a headless admin client is significant work
- Photon licensing costs per CCU (admin client counts as a connection)
- Much higher complexity than a simple WebSocket server
- Can't easily pipe shell commands -- need a full Fusion client

### Verdict for our use case

**Not practical for CLI-driven control.** The overhead of running a full Unity/Fusion client just to send admin commands is enormous. However, the **hybrid approach** is promising: keep Fusion for gameplay networking, add a lightweight WebSocket/TCP server on the host for admin commands. The admin server talks directly to `GameManager` on the same process.

---

## 9. Recommendation

### Best approach: Custom TCP Server + WebSocket Server (layered)

For the Ghost Hunt use case, implement **two layers**:

#### Layer 1: TCP Server (development, immediate)

For quick CLI interaction during development:

```
CLI (netcat/socat) --> TCP:9091 --> TcpConsoleServer --> GameManager/GameState
```

- Zero dependencies
- Works with `nc`, `echo | nc`, shell scripts
- Perfect for piping commands from Claude Code
- Implement first, takes 30 minutes

#### Layer 2: WebSocket Server (richer tooling, later)

For browser dashboards, persistent connections, and bi-directional event streaming:

```
Browser/Tool --> WS:9090 --> DevConsoleServer --> GameManager/GameState
                                              <-- Event stream (state changes, catches, etc.)
```

- Add when you want a web-based game master dashboard
- Browser can show live maze state, player positions
- websocket-sharp is proven and adds minimal footprint

#### Architecture sketch

```
                    +------------------+
                    |   Ghost Hunt     |
                    |   Unity Process  |
                    |                  |
 nc localhost:9091  |  +------------+  |
 ------------------>|  | TCP Server |  |
                    |  +-----+------+  |
                    |        |         |
                    |        v         |
                    |  +------------+  |
 ws://...:9090  --->|  | Command    |  |   +------------------+
                    |  | Dispatcher |------>| GameManager      |
                    |  +-----+------+  |   | GameEventResolver|
                    |        |         |   | MazeGenerator    |
                    |        v         |   | GameplayQuirks   |
                    |  +------------+  |   +------------------+
                    |  | Photon     |  |
                    |  | Fusion 2   |  |   (gameplay networking
                    |  | (Host)     |  |    stays separate)
                    |  +------------+  |
                    +------------------+
```

#### Command protocol (shared between TCP and WS)

```json
// Request (newline-delimited JSON for TCP, standard message for WS)
{"type":"spawn_ghost","role":"Chaser","x":10,"y":0,"z":5}
{"type":"set_phase","phase":"Frightened"}
{"type":"set_maze_seed","seed":42}
{"type":"trigger_surge"}
{"type":"delete_wall","wallX":5,"wallY":12}
{"type":"get_state"}
{"type":"list_players"}
{"type":"set_speed","player":0,"speed":2.5}

// Response
{"ok":true,"data":{"phase":"Hunt","collectibles":42,"ghostScore":0}}
{"ok":false,"error":"unknown command: foo"}
```

#### Conditional compilation

```csharp
// Only include in development
#if UNITY_EDITOR || DEVELOPMENT_BUILD
[DefaultExecutionOrder(-100)]
public class DevConsoleBootstrap : MonoBehaviour
{
    void Awake()
    {
        gameObject.AddComponent<TcpConsoleServer>();
        // gameObject.AddComponent<DevConsoleServer>(); // WS, add later
    }
}
#endif
```

#### Why not the other approaches?

| Approach | Rejected because |
|----------|-----------------|
| Remote Debugger | No CLI client, can't call methods, IDE-only |
| `-executeMethod` | Launches new process, 15-60s startup, can't reach running instance |
| Profiler | Read-only, no command execution |
| Test Framework | Requires new process, designed for assertions not control |
| File IPC | One-way, polling latency, race conditions |
| Photon admin client | Requires full Unity runtime, heavy, licensing cost |

### Implementation priority

1. **Now**: `TcpConsoleServer` MonoBehaviour (30 min, zero deps, `nc` for CLI)
2. **Soon**: Command dispatcher with handlers for each game system
3. **Later**: WebSocket layer for browser dashboard
4. **Maybe**: Record/replay command sequences for automated testing

### Key files to create

```
Assets/Scripts/Debug/
  TcpConsoleServer.cs       -- TCP listener, thread management
  DevCommand.cs             -- Command struct + response struct
  CommandDispatcher.cs      -- Routes commands to game systems
  DevConsoleBootstrap.cs    -- Auto-setup, conditional compilation
```

All four files interact with existing code through `GameManager`, `GameEventResolver`, `MazeGenerator`, and the gameplay quirk components -- no modifications to existing game code needed.
