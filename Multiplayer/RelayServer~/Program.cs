using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("connections", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var authUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
var key = Environment.GetEnvironmentVariable("SUPABASE_PUBLISHABLE_KEY") ?? "";
// Tests use a real HTTP auth fixture, never an authentication bypass in this service.
var review = args.Contains("--isolated-review") && builder.Environment.IsDevelopment();
if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var authority) ||
    !(authority.Scheme == "https" || review && authority.IsLoopback && authority.Scheme == "http") ||
    authority.AbsolutePath != "/" || !string.IsNullOrEmpty(authority.UserInfo) || !string.IsNullOrEmpty(authority.Query) ||
    !string.IsNullOrEmpty(authority.Fragment) || string.IsNullOrWhiteSpace(key))
    throw new InvalidOperationException("Configure the Supabase HTTPS authority and publishable key.");
var service = new RelayService(authority, key);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(15) });
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok", protocol = 1 }));
app.Map("/coop", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    if (!service.ReserveConnection()) { context.Response.StatusCode = 503; return; }
    try
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await service.Run(socket, context.RequestAborted);
    }
    finally { service.ReleaseConnection(); }
}).RequireRateLimiting("connections");
app.Run();

sealed class RelayService(Uri authority, string key)
{
    const int MaxFrame = 2 * 1024 * 1024 + 4096;
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    readonly Dictionary<string, Room> rooms = new();
    readonly object gate = new();
    int connections;
    public bool ReserveConnection()
    {
        if (Interlocked.Increment(ref connections) <= 4096) return true;
        Interlocked.Decrement(ref connections); return false;
    }
    public void ReleaseConnection() => Interlocked.Decrement(ref connections);
    sealed class Room(string code, Peer host)
    {
        public string Code = code;
        public Peer Host = host;
        public Dictionary<int, Peer> Clients = new();
        public int NextPeer;
    }
    sealed class Peer(WebSocket socket, CancellationToken request)
    {
        public readonly WebSocket Socket = socket;
        public readonly CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(request);
        public readonly Channel<(byte[], WebSocketMessageType)> Queue = Channel.CreateBounded<(byte[], WebSocketMessageType)>(128);
        public Guid User;
        public long Expiry;
        public Room? Room;
        public int Id;
        long queued;
        int terminating;
        public bool Send(byte[] bytes, WebSocketMessageType type)
        {
            if (Stop.IsCancellationRequested) return false;
            if (Interlocked.Add(ref queued, bytes.Length) <= 8 * 1024 * 1024 && Queue.Writer.TryWrite((bytes, type))) return true;
            Interlocked.Add(ref queued, -bytes.Length); Stop.Cancel(); return false;
        }
        public void Control(object data) => Send(JsonSerializer.SerializeToUtf8Bytes(data), WebSocketMessageType.Text);
        public async Task Write()
        {
            try
            {
                await foreach (var frame in Queue.Reader.ReadAllAsync(Stop.Token))
                {
                    Interlocked.Add(ref queued, -frame.Item1.Length);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token); timeout.CancelAfter(10000);
                    await Socket.SendAsync(new ArraySegment<byte>(frame.Item1), frame.Item2, true, timeout.Token);
                }
            }
            catch (Exception) { Stop.Cancel(); }
        }
        public void Error(string code)
        {
            if (Interlocked.Exchange(ref terminating, 1) != 0) return;
            Control(new { kind = "error", error = code });
            _ = Task.Run(async () => { await Task.Delay(200); try { Stop.Cancel(); } catch (ObjectDisposedException) { } });
        }
    }
    public async Task Run(WebSocket socket, CancellationToken request)
    {
        var peer = new Peer(socket, request); var writer = peer.Write();
        try
        {
            using var first = CancellationTokenSource.CreateLinkedTokenSource(peer.Stop.Token); first.CancelAfter(12000);
            var frame = await Read(socket, first.Token);
            if (frame.type != WebSocketMessageType.Text || frame.data.Length > 8192) return;
            using var auth = JsonDocument.Parse(frame.data);
            var json = auth.RootElement;
            if (Get(json, "kind") != "auth" || !await Authenticate(peer, Get(json, "token"))) { peer.Error("auth"); await Task.Delay(250); return; }
            string? error = Attach(peer, Get(json, "role"), Get(json, "code"));
            if (error != null) { peer.Error(error); await Task.Delay(250); return; }
            long rateAt = Environment.TickCount64; int messages = 0; long bytes = 0;
            while (!peer.Stop.IsCancellationRequested)
            {
                frame = await Read(socket, peer.Stop.Token);
                if (frame.type == WebSocketMessageType.Close) break;
                if (Environment.TickCount64 - rateAt >= 1000) { rateAt = Environment.TickCount64; messages = 0; bytes = 0; }
                if (++messages > 256 || (bytes += frame.data.Length) > 16 * 1024 * 1024) { peer.Error("busy"); break; }
                if (frame.type == WebSocketMessageType.Text)
                {
                    if (frame.data.Length > 8192) break;
                    using var control = JsonDocument.Parse(frame.data); var message = control.RootElement;
                    if (Get(message, "kind") == "renew")
                    {
                        if (!await Authenticate(peer, Get(message, "token"))) { peer.Error("auth"); break; }
                    }
                    else if (Get(message, "kind") == "kick" && peer.Id == 0 && message.TryGetProperty("peer", out var target) && target.TryGetInt32(out int id))
                    {
                        lock (gate) if (peer.Room != null && peer.Room.Clients.TryGetValue(id, out var client)) client.Error("removed");
                    }
                    continue;
                }
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= peer.Expiry) { peer.Error("auth"); break; }
                if (frame.data.Length <= 4) break;
                int destination = BinaryPrimitives.ReadInt32BigEndian(frame.data);
                Peer? recipient = null;
                lock (gate)
                {
                    var room = peer.Room;
                    if (room != null && rooms.TryGetValue(room.Code, out var current) && current == room)
                    {
                        if (peer.Id == 0) room.Clients.TryGetValue(destination, out recipient);
                        else if (destination == 0) recipient = room.Host;
                    }
                }
                // The service overwrites the sender ID. Clients cannot impersonate host/teammates.
                if (recipient != null)
                {
                    BinaryPrimitives.WriteInt32BigEndian(frame.data, peer.Id);
                    recipient.Send(frame.data, WebSocketMessageType.Binary);
                }
            }
        }
        catch (Exception) { /* No tokens, payloads, or auth responses are logged. */ }
        finally
        {
            Detach(peer); peer.Stop.Cancel(); peer.Queue.Writer.TryComplete(); socket.Abort(); await writer; peer.Stop.Dispose();
        }
    }
    async Task<bool> Authenticate(Peer peer, string token)
    {
        if (token.Length < 16 || token.Length > 8192 || token.Any(char.IsWhiteSpace)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(authority, "auth/v1/user"));
            request.Headers.Add("apikey", key); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, peer.Stop.Token);
            if (!response.IsSuccessStatusCode) return false;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(peer.Stop.Token)); var user = body.RootElement;
            if (!Guid.TryParse(Get(user, "id"), out var id) || string.IsNullOrWhiteSpace(Get(user, "email")) ||
                string.IsNullOrWhiteSpace(Get(user, "email_confirmed_at")) || peer.User != Guid.Empty && peer.User != id) return false;
            // /auth/v1/user validated the JWT; read exp solely to bound the session lifetime.
            var pieces = token.Split('.'); if (pieces.Length != 3) return false;
            string encoded = pieces[1].Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var claims = JsonDocument.Parse(Convert.FromBase64String(encoded));
            if (!claims.RootElement.TryGetProperty("exp", out var expiry) || !expiry.TryGetInt64(out long expires) || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            peer.User = id; peer.Expiry = expires; return true;
        }
        catch (Exception) { return false; }
    }
    string? Attach(Peer peer, string role, string code)
    {
        lock (gate)
        {
            if (role == "host")
            {
                if (rooms.Count >= 1024) return "busy";
                if (rooms.Values.Any(r => r.Host.User == peer.User)) return "duplicate";
                do { code = NewCode(); } while (rooms.ContainsKey(code));
                var room = new Room(code, peer); peer.Room = room; rooms.Add(code, room);
                peer.Control(new { kind = "ready", code }); return null;
            }
            if (role != "client" || !rooms.TryGetValue(code, out var found)) return "missing";
            if (found.Clients.Count >= 3) return "full";
            if (found.Host.User == peer.User || found.Clients.Values.Any(p => p.User == peer.User)) return "duplicate";
            if (found.NextPeer >= 1000000) return "busy";
            peer.Room = found; peer.Id = ++found.NextPeer; found.Clients.Add(peer.Id, peer);
            peer.Control(new { kind = "ready", code }); peer.Control(new { kind = "connected", peer = 0 });
            found.Host.Control(new { kind = "connected", peer = peer.Id }); return null;
        }
    }
    void Detach(Peer peer)
    {
        lock (gate)
        {
            var room = peer.Room; if (room == null) return;
            if (peer.Id == 0)
            {
                rooms.Remove(room.Code); foreach (var client in room.Clients.Values) client.Error("host-left");
            }
            else if (room.Clients.Remove(peer.Id)) room.Host.Control(new { kind = "disconnected", peer = peer.Id });
            peer.Room = null;
        }
    }
    static string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        return new string(RandomNumberGenerator.GetBytes(10).Select(b => alphabet[b & 31]).ToArray());
    }
    static string Get(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    static async Task<(byte[] data, WebSocketMessageType type)> Read(WebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[8192]; using var output = new MemoryStream(); WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) return ([], result.MessageType);
            if (output.Length + result.Count > MaxFrame) throw new InvalidDataException();
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return (output.ToArray(), result.MessageType);
    }
}
