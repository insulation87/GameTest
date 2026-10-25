using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Workers process plain data only; gameplay consumes events on Unity's main thread.
public sealed class CloudRelayTransport : ICoopTransport
{
    [Serializable] sealed class Control { public string kind, token, role, code, error; public int peer; }
    sealed class Frame { public byte[] bytes; public WebSocketMessageType type; }
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly CancellationTokenSource cancel = new CancellationTokenSource();
    readonly BlockingCollection<Frame> outgoing = new BlockingCollection<Frame>(128);
    readonly ConcurrentQueue<CoopTransport.Event> events = new ConcurrentQueue<CoopTransport.Event>();
    readonly ConcurrentDictionary<int, int> pendingMessages = new ConcurrentDictionary<int, int>();
    readonly HashSet<int> rejectedPeers = new HashSet<int>(); // Used only by the receive worker.
    readonly bool isHost;
    long queuedBytes, receivedBytes;
    int disposed, failed;
    string token;
    public int BoundPort => 0;
    public bool TryPoll(out CoopTransport.Event item)
    {
        if (!events.TryDequeue(out item)) return false;
        if (isHost && item.text != null) pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1));
        if (item.disconnected) pendingMessages.TryRemove(item.peer, out _);
        Interlocked.Add(ref receivedBytes, -(item.text?.Length * 2L ?? 0)); return true;
    }
    public CloudRelayTransport(Uri endpoint, string accessToken, bool host, string roomCode)
    {
        token = accessToken; isHost = host;
        string auth = JsonUtility.ToJson(new Control { kind = "auth", token = accessToken, role = host ? "host" : "client", code = roomCode });
        Task.Run(async () =>
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
                {
                    timeout.CancelAfter(10000); socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    await socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                }
                Queue(Encoding.UTF8.GetBytes(auth), WebSocketMessageType.Text);
                var writer = Task.Run(WriteLoop);
                await ReadLoop().ConfigureAwait(false); Fail("中繼連線已中斷。"); await writer.ConfigureAwait(false);
            }
            catch (Exception) { Fail("無法連線網際網路房間，請確認網路與房間服務。"); }
        });
    }
    public void SetAccessToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value == token) return;
        token = value; ControlMessage(new Control { kind = "renew", token = value });
    }
    void ControlMessage(Control control) => Queue(Encoding.UTF8.GetBytes(JsonUtility.ToJson(control)), WebSocketMessageType.Text);
    bool Queue(byte[] bytes, WebSocketMessageType type)
    {
        if (Volatile.Read(ref disposed) != 0 || cancel.IsCancellationRequested) return false;
        long total = Interlocked.Add(ref queuedBytes, bytes.Length); bool added = false;
        if (total <= 8 * 1024 * 1024)
            try { added = outgoing.TryAdd(new Frame { bytes = bytes, type = type }); } catch (InvalidOperationException) { }
        if (added) return true;
        Interlocked.Add(ref queuedBytes, -bytes.Length); Fail("連線過慢，請重新加入房間。"); return false;
    }
    public bool Send(int peer, string text)
    {
        if (text == null || peer < 0) return false;
        byte[] plain = Encoding.UTF8.GetBytes(text);
        if (plain.Length == 0 || plain.Length > CoopTransport.MaximumFrameBytes) return false;
        using (var buffer = new MemoryStream())
        {
            byte[] prefix = { (byte)(peer >> 24), (byte)(peer >> 16), (byte)(peer >> 8), (byte)peer }; buffer.Write(prefix, 0, 4);
            using (var encoder = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true)) encoder.Write(plain, 0, plain.Length);
            return Queue(buffer.ToArray(), WebSocketMessageType.Binary);
        }
    }
    async Task WriteLoop()
    {
        try
        {
            foreach (var frame in outgoing.GetConsumingEnumerable(cancel.Token))
            {
                Interlocked.Add(ref queuedBytes, -frame.bytes.Length);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
                {
                    timeout.CancelAfter(10000);
                    await socket.SendAsync(new ArraySegment<byte>(frame.bytes), frame.type, true, timeout.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception) { Fail("中繼連線已中斷。"); }
    }
    async Task ReadLoop()
    {
        var buffer = new byte[8192];
        while (!cancel.IsCancellationRequested)
        {
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (message.Length + result.Count > CoopTransport.MaximumFrameBytes + 4096) throw new InvalidDataException();
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                byte[] data = message.ToArray();
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (data.Length > 8192) throw new InvalidDataException();
                    var control = JsonUtility.FromJson<Control>(Encoding.UTF8.GetString(data));
                    if (control == null) throw new InvalidDataException();
                    if (control.kind == "ready" && !string.IsNullOrEmpty(control.code)) Emit(new CoopTransport.Event { roomCode = control.code });
                    else if (control.kind == "connected") Emit(new CoopTransport.Event { peer = control.peer, connected = true });
                    else if (control.kind == "disconnected")
                    { rejectedPeers.Remove(control.peer); Emit(new CoopTransport.Event { peer = control.peer, disconnected = true, error = "玩家離開房間" }); }
                    else if (control.kind == "error") { Fail(Translate(control.error)); return; }
                }
                else
                {
                    if (data.Length <= 4) throw new InvalidDataException();
                    int peer = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                    if (peer < 0) throw new InvalidDataException();
                    if ((isHost && peer == 0) || (!isHost && peer != 0)) throw new InvalidDataException();
                    if (isHost && rejectedPeers.Contains(peer)) continue;
                    try
                    {
                        if (isHost && data.Length - 4 > CoopClientPolicy.MaximumWireBytes) throw new InvalidDataException();
                        using (var encoded = new MemoryStream(data, 4, data.Length - 4))
                        using (var decoder = new DeflateStream(encoded, CompressionMode.Decompress))
                        using (var plain = new MemoryStream())
                        {
                            int count;
                            while ((count = decoder.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (plain.Length + count > (isHost ? CoopClientPolicy.MaximumMessageBytes : CoopTransport.MaximumFrameBytes))
                                    throw new InvalidDataException();
                                plain.Write(buffer, 0, count);
                            }
                            Emit(new CoopTransport.Event { peer = peer, text = new UTF8Encoding(false, true).GetString(plain.ToArray()) });
                        }
                    }
                    catch (Exception ex) when (isHost && (ex is InvalidDataException || ex is IOException || ex is DecoderFallbackException))
                    {
                        // A bad client must not tear down the host's relay connection and everyone else's room.
                        rejectedPeers.Add(peer); Disconnect(peer);
                    }
                }
            }
        }
    }
    static string Translate(string error)
    {
        switch (error)
        {
            case "auth": return "登入已失效，請重新登入。";
            case "missing": return "找不到此房間，請確認房間代碼。";
            case "full": return "房間已滿（最多 4 人）。";
            case "duplicate": return "此帳號已在房間中。";
            case "host-left": return "房主已離開房間。";
            case "busy": return "房間服務忙碌中，請稍後再試。";
            default: return "房間連線已中斷，請重新加入。";
        }
    }
    void Emit(CoopTransport.Event item)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (isHost && item.text != null && pendingMessages.AddOrUpdate(item.peer, 1, (_, count) => count + 1) > 64)
        {
            pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1));
            rejectedPeers.Add(item.peer); Disconnect(item.peer); return;
        }
        long size = item.text?.Length * 2L ?? 0;
        long total = Interlocked.Add(ref receivedBytes, size);
        if (events.Count >= 256 || total > 8 * 1024 * 1024)
        {
            Interlocked.Add(ref receivedBytes, -size);
            if (isHost && item.text != null)
            {
                pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1));
                rejectedPeers.Add(item.peer); Disconnect(item.peer);
            }
            else Fail("連線資料過多，請重新加入房間。");
            return;
        }
        events.Enqueue(item);
    }
    void Fail(string error)
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref failed, 1) != 0) return;
        events.Enqueue(new CoopTransport.Event { fatal = true, disconnected = true, error = error });
        cancel.Cancel(); outgoing.CompleteAdding(); socket.Abort();
    }
    public void Disconnect(int peer) => ControlMessage(new Control { kind = "kick", peer = peer });
    public void DisconnectAfterSend(int peer) => Task.Run(async () => { await Task.Delay(250).ConfigureAwait(false); Disconnect(peer); });
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancel.Cancel(); outgoing.CompleteAdding(); socket.Abort(); socket.Dispose();
        while (TryPoll(out _)) { }
    }
}
