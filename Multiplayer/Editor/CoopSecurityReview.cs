using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit batch entry point. Never opens or edits the user's scenes or save identity.
[InitializeOnLoad]
public static class CoopSecurityReview
{
    const string Pending = "CoopSecurityReview.Pending";
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static readonly List<string> editorDiagnostics = new List<string>();
    static readonly List<Probe> probes = new List<Probe>();
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static double next, deadline;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static CoopActor Healthy => Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).First(a => !a.Local);
    static CoopSecurityReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Application.dataPath, "../security-isolated-marker.txt")))
            throw new InvalidOperationException("Marked isolated batch project required.");
        foreach (string scenario in new[] { "host", "malformed", "flood" }) File.Delete(Path.Combine(Output, "relay-kick-" + scenario + ".txt"));
        File.Delete(Path.Combine(Output, "security-playmode-result.txt")); File.Delete(Path.Combine(Output, "security-live.txt"));
        string hello = JsonUtility.ToJson(Hello());
        var parsedHello = JsonUtility.FromJson<CoopPacket>(hello);
        File.WriteAllText(Path.Combine(Output, "hello-diagnostic.txt"), hello + "\nPolicy valid: " + CoopClientPolicy.Valid(parsedHello, false) +
            "\nSnapshot: " + (parsedHello.snapshot != null) + "; mesh: " + (parsedHello.mesh != null));
        PlayerSettings.companyName = "CodexIsolatedReview";
        PlayerSettings.productName = "CoopSecurityReview";
        CoopModeReview.BuildFixtures(); LoginSceneSetup.PlayFromCurrentScene();
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); results.Clear(); errors.Clear(); editorDiagnostics.Clear(); steps.Clear(); steps.Push(Checks());
        deadline = EditorApplication.timeSinceStartup + 180;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search")) { editorDiagnostics.Add(message + "\n" + stack); return; }
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }
    static void Tick()
    {
        try
        {
            foreach (var probe in probes) probe.Drain();
            if (EditorApplication.timeSinceStartup < next) return;
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Security Play Mode review timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                next = EditorApplication.timeSinceStartup + (step.Current is float delay ? delay : .02); return;
            }
            Check(errors.Count == 0, "No runtime errors/exceptions: " + string.Join("\n", errors)); Finish(0);
        }
        catch (Exception ex) { results.Add("FAIL " + ex); Finish(1); }
    }
    static void Finish(int code)
    {
        File.WriteAllText(Path.Combine(Output, "security-playmode-result.txt"), (code == 0 ? "PASS\n" : "FAIL\n") + string.Join("\n", results) + "\n" + string.Join("\n", errors));
        File.WriteAllLines(Path.Combine(Output, "security-editor-diagnostics.txt"), editorDiagnostics);
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        foreach (var probe in probes) probe.Dispose(); probes.Clear(); CoopSession.Leave(); EditorApplication.Exit(code);
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        results.Add("PASS " + message); File.WriteAllLines(Path.Combine(Output, "security-live.txt"), results);
    }
    static IEnumerator Wait(Func<bool> ready, string label, double seconds = 10)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (!ready()) { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException(label); yield return .02f; }
    }
    sealed class Probe : IDisposable
    {
        public readonly CoopTransport transport = new CoopTransport();
        public bool connected, disconnected, welcome, rejected, snapshot;
        public string lastError;
        public void Drain()
        {
            while (transport.TryPoll(out var item))
            {
                connected |= item.connected; disconnected |= item.disconnected;
                if (item.error != null) lastError = item.error;
                if (item.text == null) continue;
                var packet = JsonUtility.FromJson<CoopPacket>(item.text);
                welcome |= packet.kind == "welcome"; rejected |= packet.kind == "reject"; snapshot |= packet.kind == "state";
            }
        }
        public void Send(CoopPacket packet) => Send(JsonUtility.ToJson(packet));
        public void Send(string json) { if (!transport.Send(0, json)) throw new Exception("Probe could not send"); }
        public void Dispose() => transport.Dispose();
    }
    static Probe Connect()
    {
        var probe = new Probe(); probes.Add(probe); probe.transport.Connect("127.0.0.1", CoopSession.Instance.ListeningPort); return probe;
    }
    static CoopPacket Hello(string password = "security") => new CoopPacket { kind = "hello", scene = "Map1", character = "medic", password = password };
    static IEnumerator Join(Probe probe)
    {
        yield return Wait(() => probe.connected, "TCP connect"); probe.Send(Hello());
        yield return Wait(() => probe.welcome || probe.disconnected, "Hello/welcome handshake");
        if (!probe.welcome) throw new Exception("Handshake closed: " + probe.lastError);
    }
    static IEnumerator Reject(string json, bool join, string label)
    {
        using (var probe = Connect())
        {
            if (join) yield return Join(probe); else yield return Wait(() => probe.connected, "TCP connect");
            probe.Send(json); yield return Wait(() => probe.disconnected, label);
            yield return Wait(() => CoopSession.Instance.PlayerCount == 2, "Remove only attacking actor");
            Check(CoopSession.Mode == CoopMode.Host && CoopSession.Instance.SceneReady, label + "; host and healthy peer remain online");
        }
    }
    static IEnumerator Checks()
    {
        Check(EditorApplication.isPlaying, "Tests execute inside Unity Play Mode");
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Guest enters supported LAN flow without real credentials");
        Check(CoopSession.StartHost("Map1", "breaker", 0, "security", out var error), "Host starts fixture Map1: " + error);
        yield return Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading, "Fixture readiness", 30);
        using (var healthy = Connect())
        {
            yield return Join(healthy); yield return Wait(() => healthy.snapshot, "Host snapshot");
            Check(CoopSession.Instance.PlayerCount == 2, "Valid client joins and receives welcome plus world snapshot");
            var actor = Healthy; Vector3 before = actor.transform.position;
            for (int i = 0; i < 30; i++)
            {
                healthy.Send(new CoopPacket { kind = "input", input = new CoopInputFrame { move = Vector2.up, yaw = 0, pitch = 0 } });
                yield return 1f / 30;
            }
            Check(!healthy.disconnected && Vector3.Distance(before, actor.transform.position) > .2f, "30 Hz legal input moves the remote actor under host simulation");
            healthy.Send(new CoopPacket { kind = "command", action = "slot", value = 0 });
            healthy.Send(new CoopPacket { kind = "upgrade", value = 0 }); yield return .1f;
            Check(!healthy.disconnected, "Legal inventory command and upgrade request remain compatible");

            yield return Reject("{bad json", false, "Malformed JSON is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "input", input = new CoopInputFrame() }), false, "Gameplay before handshake is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "state", snapshot = new CoopSnapshot() }), true, "Client-forged world state is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "mesh", mesh = new CoopMesh() }), true, "Client-forged mesh is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "input", input = new CoopInputFrame { move = new Vector2(100, 0) } }), true, "Out-of-range movement is disconnected");
            yield return Reject("{\"version\":1,\"kind\":\"input\",\"input\":{\"yaw\":1e40}}", true, "Non-finite numeric input is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "input", input = new CoopInputFrame { held = 16384 } }), true, "Undefined button bits are disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "command", action = "grantMoney", value = int.MaxValue }), true, "Unknown command is disconnected");
            yield return Reject(JsonUtility.ToJson(new CoopPacket { kind = "upgrade", value = -1 }), true, "Invalid upgrade index is disconnected");
            yield return Reject(JsonUtility.ToJson(Hello()), true, "Duplicate handshake is disconnected");
            using (var wrong = Connect())
            {
                yield return Wait(() => wrong.connected, "Wrong password connect"); wrong.Send(Hello("wrong"));
                yield return Wait(() => wrong.disconnected, "Wrong password rejection");
                Check(wrong.rejected && CoopSession.Instance.PlayerCount == 2, "Wrong room password retains friendly rejection and spawns no actor");
            }
            yield return RawReject(false);
            yield return RawReject(true);
            using (var flood = Connect())
            {
                yield return Join(flood);
                for (int batch = 0; batch < 10 && !flood.disconnected; batch++)
                {
                    for (int i = 0; i < 30; i++)
                        if (!flood.transport.Send(0, JsonUtility.ToJson(new CoopPacket { kind = "input", input = new CoopInputFrame() }))) break;
                    yield return .03f;
                }
                yield return Wait(() => flood.disconnected, "Flood rejection");
                yield return Wait(() => CoopSession.Instance.PlayerCount == 2, "Flood actor cleanup");
                Check(!healthy.disconnected && CoopSession.Instance.SceneReady, "Input flood disconnects attacker while the normal client stays connected");
            }
            healthy.Send(new CoopPacket { kind = "input", input = new CoopInputFrame { yaw = 70 } }); yield return .1f;
            Check(!healthy.disconnected && Mathf.Abs(Healthy.Frame.yaw - 70) < .1f, "Healthy peer's input still applies after all attack cases");
        }
        CoopSession.Leave(); yield return .1f;
        yield return ReceiveMemoryLimit();
        yield return Relay("host"); yield return Relay("malformed"); yield return Relay("flood"); yield return Relay("client"); yield return Relay("memory");
    }
    static IEnumerator RawReject(bool inflate)
    {
        using (var raw = new TcpClient())
        {
            raw.Connect("127.0.0.1", CoopSession.Instance.ListeningPort); var stream = raw.GetStream();
            byte[] bytes;
            if (inflate)
            {
                using (var buffer = new MemoryStream())
                {
                    using (var encoder = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true))
                    { var text = Encoding.UTF8.GetBytes(new string('x', CoopClientPolicy.MaximumMessageBytes + 1)); encoder.Write(text, 0, text.Length); }
                    bytes = buffer.ToArray();
                }
            }
            else bytes = new byte[CoopClientPolicy.MaximumWireBytes + 1];
            int size = bytes.Length;
            stream.Write(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size }, 0, 4);
            if (inflate) stream.Write(bytes, 0, bytes.Length); // Oversized header must be refused without waiting for its body.
            yield return Wait(() => raw.Client.Poll(0, SelectMode.SelectRead) && raw.Available == 0, "Reject raw oversized frame");
            Check(CoopSession.Instance.PlayerCount == 2, inflate ? "Deflate expansion beyond 4 KiB is rejected" : "Oversized wire length is rejected before reading body");
        }
    }
    static IEnumerator ReceiveMemoryLimit()
    {
        using (var server = new CoopTransport())
        using (var client = new CoopTransport())
        {
            server.Listen(0); client.Connect("127.0.0.1", server.BoundPort); int peer = 0;
            yield return Wait(() => { while (server.TryPoll(out var item)) if (item.connected) peer = item.peer; return peer > 0; }, "Queue fixture connect");
            Check(server.Send(peer, new string('s', 16000)), "Host may still send world data larger than the client input limit");
            bool large = false;
            yield return Wait(() => { while (client.TryPoll(out var item)) large |= item.text?.Length == 16000; return large; }, "Large host message");
            string frame = new string('q', 1024 * 1024);
            int sent = 0; for (int i = 0; i < 6; i++) if (server.Send(peer, frame)) sent++;
            Check(sent >= 5, "At least five 1 MiB frames are sent to exercise the receive memory limit");
            yield return 1f;
            long queued = (long)typeof(CoopTransport).GetField("receivedBytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);
            bool closed = false; while (client.TryPoll(out var item)) closed |= item.disconnected;
            Check(queued <= 8 * 1024 * 1024 && closed, "Unconsumed LAN receive payload stays within 8 MiB and closes overloaded peer");
        }
    }
    static IEnumerator Relay(string scenario)
    {
        using (var relay = new CloudRelayTransport(new Uri("ws://127.0.0.1:18465/" + scenario), "isolated-review-token", scenario != "client" && scenario != "memory", null))
        {
            bool received = false, fatal = false;
            if (scenario == "flood" || scenario == "memory") yield return 1f; // Deliberately pause consumption to test backlog limits.
            long queued = (long)typeof(CloudRelayTransport).GetField("receivedBytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relay);
            yield return Wait(() =>
            {
                while (relay.TryPoll(out var item))
                {
                    fatal |= item.fatal;
                    received |= scenario == "client" ? item.peer == 0 && item.text?.Length > 4096 : item.peer == 2 && item.text == "healthy";
                }
                return received || fatal;
            }, "Relay " + scenario);
            if (scenario == "memory")
            {
                Check(fatal && queued <= 8 * 1024 * 1024, "Unconsumed cloud receive payload stays within 8 MiB and fails safely");
                yield break;
            }
            Check(received && !fatal, "Relay " + scenario + ": " + (scenario == "client" ? "large host snapshots remain accepted" : "bad peer is isolated and healthy peer data survives"));
            if (scenario != "client")
            {
                yield return Wait(() => File.Exists(Path.Combine(Output, "relay-kick-" + scenario + ".txt")), "Relay kick acknowledgement");
                Check(File.ReadAllText(Path.Combine(Output, "relay-kick-" + scenario + ".txt")) == "1", "Relay " + scenario + " kicks only offending peer 1");
            }
        }
    }
}
