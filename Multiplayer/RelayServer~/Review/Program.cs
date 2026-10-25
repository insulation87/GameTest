using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

string root = Environment.GetEnvironmentVariable("CLOUD_REVIEW_ROOT") ?? "";
if (!File.Exists(Path.Combine(root, "cloud-review-marker.txt"))) throw new InvalidOperationException("Isolated marker required.");
var results = new List<string>();
void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); results.Add(label); Console.WriteLine("PASS " + label); }
try
{
    using var invalid = await Peer.Open("invalid_token_at_least_16_chars", "host", "");
    Check((await invalid.Control()).GetProperty("error").GetString() == "auth", "Invalid account token is refused by backend");
    using var missing = await Peer.Open(Token(Guid.NewGuid()), "client", "23456789AB");
    Check((await missing.Control()).GetProperty("error").GetString() == "missing", "Unknown room is refused");
    Guid hostId = Guid.NewGuid(); using var host = await Peer.Open(Token(hostId), "host", "");
    string code = (await host.Control()).GetProperty("code").GetString()!;
    Check(code.Length == 10 && code.All(c => "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".Contains(c)), "Room code is random and valid");
    using var duplicate = await Peer.Open(Token(hostId), "client", code);
    Check((await duplicate.Control()).GetProperty("error").GetString() == "duplicate", "Duplicate account in same room is refused");
    using var client = await Peer.Open(Token(Guid.NewGuid()), "client", code);
    await client.Control(); await client.Control(); int id = (await host.Control()).GetProperty("peer").GetInt32();
    await client.Binary(0, [42,43,44]); var frame = await host.Read();
    Check(frame.Type == WebSocketMessageType.Binary && BinaryPrimitives.ReadInt32BigEndian(frame.Bytes) == id && frame.Bytes[4] == 42, "Relay stamps trusted client sender ID");
    await host.Binary(id, [51,52,53]); frame = await client.Read();
    Check(BinaryPrimitives.ReadInt32BigEndian(frame.Bytes) == 0 && frame.Bytes[4] == 51, "Relay stamps host ID and preserves payload");
    using var second = await Peer.Open(Token(Guid.NewGuid()), "client", code);
    await second.Control(); await second.Control(); int secondId = (await host.Control()).GetProperty("peer").GetInt32();
    await client.Binary(secondId, [61,62,63]);
    Check(!await second.HasFrame(300) && !await host.HasFrame(100), "Client cannot route data directly to or impersonate another client");
    using var third = await Peer.Open(Token(Guid.NewGuid()), "client", code);
    await third.Control(); await third.Control(); await host.Control();
    using var full = await Peer.Open(Token(Guid.NewGuid()), "client", code);
    Check((await full.Control()).GetProperty("error").GetString() == "full", "Room admits host plus three clients only");
    await client.Send(new { kind = "renew", token = Token(Guid.NewGuid()) });
    Check((await client.Control()).GetProperty("error").GetString() == "auth", "Token renewal cannot switch account identity");
    Check((await host.Control()).GetProperty("kind").GetString() == "disconnected", "Disconnected peer is removed and host notified");
    await host.Send(new { kind = "kick", peer = secondId });
    Check((await second.Control()).GetProperty("error").GetString() == "removed", "Host can remove a peer"); await host.Control();
    host.Dispose();
    Check((await third.Control()).GetProperty("error").GetString() == "host-left", "Host departure terminates room and notifies remaining client");
    using var expired = await Peer.Open(Token(Guid.NewGuid(), -60), "host", "");
    Check((await expired.Control()).GetProperty("error").GetString() == "auth", "Expired account token is refused");
    File.WriteAllText(Path.Combine(root, "relay-protocol-result.txt"), "PASS\n" + string.Join('\n', results));
}
catch (Exception ex) { File.WriteAllText(Path.Combine(root, "relay-protocol-result.txt"), "FAIL " + ex); throw; }
static string Token(Guid id, int lifetime = 3600)
{
    string claims = JsonSerializer.Serialize(new { sub = id, exp = DateTimeOffset.UtcNow.AddSeconds(lifetime).ToUnixTimeSeconds() });
    return "eyJhbGciOiJSRVZJRVcifQ." + Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)).TrimEnd('=').Replace('+','-').Replace('/','_') + ".isolated_review";
}
sealed class Peer : IDisposable
{
    public sealed record Frame(byte[] Bytes, WebSocketMessageType Type);
    readonly ClientWebSocket socket = new();
    readonly CancellationTokenSource stop = new();
    readonly Channel<Frame> inbox = Channel.CreateUnbounded<Frame>();
    public static async Task<Peer> Open(string token, string role, string code)
    {
        var peer = new Peer(); await peer.socket.ConnectAsync(new Uri("ws://127.0.0.1:18444/coop"), peer.stop.Token);
        _ = peer.Receive(); await peer.Send(new { kind = "auth", token, role, code }); return peer;
    }
    async Task Receive()
    {
        try
        {
            var buffer = new byte[8192];
            while (!stop.IsCancellationRequested)
            {
                using var output = new MemoryStream(); WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return; output.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                await inbox.Writer.WriteAsync(new Frame(output.ToArray(), result.MessageType));
            }
        }
        catch (Exception) { }
        finally { inbox.Writer.TryComplete(); }
    }
    public async Task Send(object message) => await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(message)), WebSocketMessageType.Text, true, stop.Token);
    public async Task Binary(int peer, byte[] payload)
    {
        byte[] bytes = new byte[4 + payload.Length]; BinaryPrimitives.WriteInt32BigEndian(bytes, peer); payload.CopyTo(bytes, 4);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, stop.Token);
    }
    public async Task<Frame> Read() { using var timeout = new CancellationTokenSource(10000); return await inbox.Reader.ReadAsync(timeout.Token); }
    public async Task<JsonElement> Control() { var frame = await Read(); using var json = JsonDocument.Parse(frame.Bytes); return json.RootElement.Clone(); }
    public async Task<bool> HasFrame(int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        try { return await inbox.Reader.WaitToReadAsync(timeout.Token); } catch (OperationCanceledException) { return false; }
    }
    public void Dispose() { stop.Cancel(); socket.Abort(); socket.Dispose(); }
}
