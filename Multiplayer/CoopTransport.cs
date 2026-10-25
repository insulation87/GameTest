using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Length-prefixed UTF-8 frames. Socket workers never access Unity objects.
public sealed class CoopTransport : ICoopTransport
{
    public sealed class Event { public int peer; public string text, error, roomCode; public bool connected, disconnected, fatal; }
    public const int MaximumFrameBytes = 2 * 1024 * 1024;
    readonly ConcurrentQueue<Event> events = new ConcurrentQueue<Event>();
    readonly ConcurrentDictionary<int, int> pendingMessages = new ConcurrentDictionary<int, int>();
    readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
    readonly object gate = new object();
    TcpListener listener;
    volatile bool disposed;
    long receivedBytes;
    int nextPeer;
    public int BoundPort { get; private set; }

    sealed class Peer
    {
        public int id;
        public TcpClient client;
        public readonly BlockingCollection<byte[]> outgoing = new BlockingCollection<byte[]>(64);
        public long queuedBytes;
        public int closed;
    }
    public bool TryPoll(out Event item)
    {
        if (!events.TryDequeue(out item)) return false;
        if (item.text != null && item.peer > 0) pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1));
        if (item.disconnected) pendingMessages.TryRemove(item.peer, out _);
        Interlocked.Add(ref receivedBytes, -(item.text?.Length * 2L ?? 0)); return true;
    }
    void Emit(Event item)
    {
        if (disposed) return;
        if (item.text != null && item.peer > 0 && pendingMessages.AddOrUpdate(item.peer, 1, (_, count) => count + 1) > 64)
        { pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1)); Disconnect(item.peer); return; }
        long size = item.text?.Length * 2L ?? 0;
        long total = Interlocked.Add(ref receivedBytes, size);
        if (!item.disconnected && (events.Count >= 256 || total > 8 * 1024 * 1024))
        {
            Interlocked.Add(ref receivedBytes, -size);
            if (item.text != null && item.peer > 0) pendingMessages.AddOrUpdate(item.peer, 0, (_, count) => Math.Max(0, count - 1));
            Disconnect(item.peer); return;
        }
        events.Enqueue(item);
    }
    public void Listen(int port)
    {
        if (disposed || listener != null) throw new InvalidOperationException();
        listener = new TcpListener(IPAddress.Any, port);
        try { listener.Start(4); BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port; }
        catch { listener = null; throw; }
        Worker(() =>
        {
            while (!disposed)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { break; }
                lock (gate)
                {
                    // One extra handshake slot lets the host explain that a four-player room is full.
                    if (disposed || peers.Count >= 4 || !LanAccess.IsLocal(((IPEndPoint)client.Client.RemoteEndPoint).Address)) { client.Close(); continue; }
                    Add(client, ++nextPeer);
                }
            }
        });
    }
    public void Connect(string host, int port)
    {
        if (disposed) throw new ObjectDisposedException(nameof(CoopTransport));
        Worker(() =>
        {
            var client = new TcpClient();
            try
            {
                if (!IPAddress.TryParse(host, out var target) || !LanAccess.IsLocal(target))
                    throw new InvalidOperationException("同網路房間請輸入房主的區域 IP；跨網路請使用網際網路房間。");
                var pending = client.ConnectAsync(target, port);
                if (!pending.Wait(8000)) throw new TimeoutException("連線逾時");
                pending.GetAwaiter().GetResult();
                lock (gate) { if (disposed) { client.Close(); return; } Add(client, 0); }
            }
            catch (Exception ex) { client.Close(); Emit(new Event { peer = 0, disconnected = true, error = ex.GetBaseException().Message }); }
        });
    }
    void Add(TcpClient client, int id)
    {
        client.NoDelay = true;
        client.SendTimeout = 5000;
        client.ReceiveTimeout = 15000;
        var peer = new Peer { id = id, client = client };
        peers.Add(id, peer);
        Emit(new Event { peer = id, connected = true });
        Worker(() => ReadLoop(peer));
        Worker(() => WriteLoop(peer));
    }
    public bool Send(int id, string text)
    {
        if (disposed || text == null) return false;
        byte[] payload = Encoding.UTF8.GetBytes(text);
        if (payload.Length == 0 || payload.Length > MaximumFrameBytes) return false;
        using (var buffer = new MemoryStream())
        {
            using (var compressor = new DeflateStream(buffer, CompressionLevel.Fastest, true)) compressor.Write(payload, 0, payload.Length);
            payload = buffer.ToArray();
        }
        Peer peer;
        lock (gate) if (!peers.TryGetValue(id, out peer)) return false;
        if (Interlocked.Add(ref peer.queuedBytes, payload.Length) > 8 * 1024 * 1024)
        { Interlocked.Add(ref peer.queuedBytes, -payload.Length); Disconnect(id); return false; }
        try
        {
            if (peer.outgoing.TryAdd(payload)) return true;
        }
        catch (InvalidOperationException) { }
        Interlocked.Add(ref peer.queuedBytes, -payload.Length);
        Disconnect(id);
        return false;
    }
    void ReadLoop(Peer peer)
    {
        try
        {
            var stream = peer.client.GetStream();
            byte[] header = new byte[4];
            while (!disposed && Volatile.Read(ref peer.closed) == 0)
            {
                ReadExactly(stream, header);
                int count = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                if (count <= 0 || count > (peer.id > 0 ? CoopClientPolicy.MaximumWireBytes : MaximumFrameBytes))
                    throw new InvalidDataException("無效的連線資料長度");
                byte[] body = new byte[count];
                ReadExactly(stream, body);
                using (var encoded = new MemoryStream(body))
                using (var decoder = new DeflateStream(encoded, CompressionMode.Decompress))
                using (var decoded = new MemoryStream())
                {
                    byte[] chunk = new byte[8192]; int read;
                    while ((read = decoder.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        if (decoded.Length + read > (peer.id > 0 ? CoopClientPolicy.MaximumMessageBytes : MaximumFrameBytes))
                            throw new InvalidDataException("連線資料超出上限");
                        decoded.Write(chunk, 0, read);
                    }
                    Emit(new Event { peer = peer.id, text = new UTF8Encoding(false, true).GetString(decoded.ToArray()) });
                }
            }
        }
        catch (Exception ex) { Close(peer, ex.Message); }
    }
    static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int count = stream.Read(buffer, offset, buffer.Length - offset);
            if (count == 0) throw new EndOfStreamException("對方已離開連線");
            offset += count;
        }
    }
    void WriteLoop(Peer peer)
    {
        try
        {
            var stream = peer.client.GetStream();
            foreach (byte[] body in peer.outgoing.GetConsumingEnumerable())
            {
                Interlocked.Add(ref peer.queuedBytes, -body.Length);
                byte[] header = { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length };
                stream.Write(header, 0, 4); stream.Write(body, 0, body.Length);
            }
        }
        catch (Exception ex) { Close(peer, ex.Message); }
    }
    public void Disconnect(int id)
    {
        Peer peer;
        lock (gate) if (!peers.TryGetValue(id, out peer)) return;
        Close(peer, "連線已關閉");
    }
    public void DisconnectAfterSend(int id)
    {
        Worker(() => { Thread.Sleep(150); Disconnect(id); });
    }
    void Close(Peer peer, string error)
    {
        if (Interlocked.Exchange(ref peer.closed, 1) != 0) return;
        lock (gate) peers.Remove(peer.id);
        peer.outgoing.CompleteAdding(); peer.client.Close();
        Emit(new Event { peer = peer.id, disconnected = true, error = error });
    }
    static void Worker(ThreadStart work) => new Thread(work) { IsBackground = true, Name = "Coop Socket" }.Start();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        listener?.Stop();
        Peer[] copy;
        lock (gate) { copy = new List<Peer>(peers.Values).ToArray(); }
        foreach (var peer in copy) Close(peer, null);
        while (TryPoll(out _)) { }
    }
}
