using System.Diagnostics;
using System.Text.Json;

// Local-only test Auth/Data API backed by the real migration in an ephemeral PostgreSQL cluster.
string root = Environment.GetEnvironmentVariable("CLOUD_REVIEW_ROOT") ?? "";
if (!Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "cloud-review-marker.txt")))
    throw new InvalidOperationException("Isolated review marker required.");
string psql = Path.Combine(root, "PostgreSQL", "pgsql", "bin", "psql.exe");
var builder = WebApplication.CreateBuilder(args); builder.WebHost.UseUrls("http://127.0.0.1:18443");
var app = builder.Build();
app.MapGet("/auth/v1/user", async (HttpContext context) =>
{
    if (!Identity(context, out var id)) return Results.Unauthorized();
    await Sql("insert into auth.users(id,email,email_confirmed_at) values ('" + id + "','review@example.com',now()) on conflict(id) do nothing;");
    return Results.Ok(new { id, email = "review@example.com", email_confirmed_at = "2026-10-08T00:00:00Z" });
});
app.MapPost("/rest/v1/rpc/game_social", async (HttpContext context) =>
{
    if (!Identity(context, out var id)) return Results.Unauthorized();
    try
    {
        using var json = await JsonDocument.ParseAsync(context.Request.Body);
        var command = json.RootElement;
        string Literal(string name)
        {
            if (!command.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return "null";
            string text = property.GetString() ?? ""; if (text.Contains('\0') || text.Length > 254) throw new ArgumentException();
            return "'" + text.Replace("'", "''") + "'";
        }
        await Sql("insert into auth.users(id,email,email_confirmed_at) values ('" + id + "','review@example.com',now()) on conflict(id) do nothing;");
        string result = await Sql("begin; set local role authenticated; select set_config('request.jwt.claim.sub','" + id + "',true); select public.game_social(" + Literal("operation") + "," + Literal("target") + "," + Literal("nickname") + "); commit;");
        string state = result.Split('\n').First(line => line.TrimStart().StartsWith('{'));
        return Results.Content(state, "application/json");
    }
    catch (Exception) { return Results.BadRequest(new { error = "rejected" }); }
});
app.MapGet("/health", () => Results.Ok(new { isolated = true }));
app.Run();
bool Identity(HttpContext context, out Guid id)
{
    id = Guid.Empty;
    if (!System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress!)) return false;
    try
    {
        string header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ")) return false;
        var jwt = header[7..].Split('.'); if (jwt.Length != 3 || jwt[2] != "isolated_review") return false;
        string encoded = jwt[1].Replace('-', '+').Replace('_', '/').PadRight((jwt[1].Length + 3) / 4 * 4, '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(encoded));
        return Guid.TryParse(claims.RootElement.GetProperty("sub").GetString(), out id) && claims.RootElement.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    catch (Exception) { return false; }
}
async Task<string> Sql(string query)
{
    var info = new ProcessStartInfo(psql) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8, StandardInputEncoding = new System.Text.UTF8Encoding(false) };
    info.Environment["PGCLIENTENCODING"] = "UTF8";
    foreach (var arg in new[] { "-h", "127.0.0.1", "-p", "15432", "-U", "postgres", "-d", "postgres", "-At", "-v", "ON_ERROR_STOP=1", "-f", "-" }) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(query); process.StandardInput.Close();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    try { await process.WaitForExitAsync(limit.Token); } catch { process.Kill(true); throw; }
    string result = await stdout; await stderr;
    if (process.ExitCode != 0) throw new InvalidOperationException("Test SQL rejected."); return result;
}
