using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// Loopback-only adversarial transport fixture, never used by the production relay.
string root = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(root, "Project/security-isolated-marker.txt"))) throw new InvalidOperationException("Isolated marker required");
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:18465"); builder.Logging.ClearProviders();
var app = builder.Build(); app.UseWebSockets();
app.MapGet("/health", () => "isolated");
foreach (string scenario in new[] { "host", "malformed", "client", "flood", "memory" })
    app.Map("/" + scenario, async context =>
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var buffer = new byte[8192];
        try
        {
            await socket.ReceiveAsync(buffer, timeout.Token); // Dummy auth frame contains no real credential.
            if (scenario == "client") await Send(0, Encoding.UTF8.GetBytes(new string('s', 16000)));
            else if (scenario == "memory")
            {
                for (int i = 0; i < 6; i++) await Send(0, Encoding.UTF8.GetBytes(new string('q', 1024 * 1024)));
            }
            else if (scenario == "flood")
            {
                for (int i = 0; i < 300; i++) await Send(1, Encoding.UTF8.GetBytes("queued"));
                await Send(2, Encoding.UTF8.GetBytes("healthy"));
            }
            else
            {
                await Send(1, scenario == "host" ? Encoding.UTF8.GetBytes(new string('x', 4097)) : new byte[] { 0xff, 0xfe });
                await Send(1, Encoding.UTF8.GetBytes("ignored-after-kick"));
                await Send(2, Encoding.UTF8.GetBytes("healthy"));
            }
            while (socket.State == WebSocketState.Open)
            {
                var frame = await socket.ReceiveAsync(buffer, timeout.Token);
                if (frame.MessageType == WebSocketMessageType.Close) break;
                if (frame.MessageType != WebSocketMessageType.Text) continue;
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
                if (doc.RootElement.GetProperty("kind").GetString() == "kick")
                    await File.WriteAllTextAsync(Path.Combine(root, "relay-kick-" + scenario + ".txt"), doc.RootElement.GetProperty("peer").GetInt32().ToString());
            }
        }
        catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException) { }
        async Task Send(int peer, byte[] plain)
        {
            using var memory = new MemoryStream(); byte[] prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, peer); memory.Write(prefix);
            using (var deflate = new DeflateStream(memory, CompressionLevel.Fastest, true)) deflate.Write(plain);
            await socket.SendAsync(memory.ToArray(), WebSocketMessageType.Binary, true, timeout.Token);
        }
    });
await app.RunAsync();
