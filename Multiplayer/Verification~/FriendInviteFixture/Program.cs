using System.Diagnostics;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(root, "Project/friend-invite-isolated-marker.txt"))) throw new Exception("Isolated project required");
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:18474"); builder.Logging.ClearProviders();
var app = builder.Build();
app.MapGet("/health", () => "isolated");
app.MapGet("/auth/v1/user", (HttpContext ctx) => Identity(ctx, out var id) ?
    Results.Ok(new { id, email = "isolated@example.com", email_confirmed_at = "2026-10-25T00:00:00Z" }) : Results.Unauthorized());
foreach (string rpc in new[] { "game_social", "game_room_invites" })
    app.MapPost("/rest/v1/rpc/" + rpc, async (HttpContext ctx) =>
    {
        if (!Identity(ctx, out var id)) return Results.Unauthorized();
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            string Literal(string name)
            {
                if (!doc.RootElement.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return "null";
                string text = value.GetString()!;
                if (text.Length > 254 || text.Contains('\0')) throw new Exception();
                return "'" + text.Replace("'", "''") + "'";
            }
            string arguments = Literal("operation") + "," + Literal("target") + "," +
                (rpc == "game_social" ? Literal("nickname") : Literal("room_code") + "," + Literal("room_password"));
            string result = await Sql("begin;set local role authenticated;select set_config('request.jwt.claim.sub','" + id + "',true);select public." + rpc + "(" + arguments + ");commit;");
            return Results.Content(result.Split('\n').First(line => line.TrimStart().StartsWith('{')), "application/json");
        }
        catch { return Results.BadRequest(new { error = "rejected" }); }
    });
await app.RunAsync();
bool Identity(HttpContext ctx, out Guid id)
{
    id = Guid.Empty;
    if (!System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!)) return false;
    try
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ")) return false;
        string[] jwt = header[7..].Split('.');
        if (jwt.Length != 3 || jwt[2] != "isolated_review") return false;
        using var doc = JsonDocument.Parse(Convert.FromBase64String(jwt[1].Replace('-', '+').Replace('_', '/').PadRight((jwt[1].Length + 3) / 4 * 4, '=')));
        return Guid.TryParse(doc.RootElement.GetProperty("sub").GetString(), out id) && doc.RootElement.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    catch { return false; }
}
async Task<string> Sql(string query)
{
    var info = new ProcessStartInfo(Path.Combine(root, "PostgreSQL/pgsql/bin/psql.exe"))
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8 };
    info.Environment["PGCLIENTENCODING"] = "UTF8";
    foreach (string arg in new[] { "-h", "127.0.0.1", "-p", "15473", "-U", "postgres", "-d", "postgres", "-At", "-v", "ON_ERROR_STOP=1", "-f", "-" }) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(query); process.StandardInput.Close();
    await process.WaitForExitAsync();
    string result = await output; string details = await error;
    if (process.ExitCode != 0) { Console.WriteLine(details); throw new Exception("SQL rejected"); }
    return result;
}
