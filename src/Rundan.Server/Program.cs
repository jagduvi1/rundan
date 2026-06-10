using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Rundan.Server;
using Rundan.Server.Data;
using Rundan.Server.Endpoints;
using Rundan.Server.Hubs;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Realtime;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ----------------------------------------------------------
var options = builder.Configuration.GetSection(RundanOptions.SectionName).Get<RundanOptions>()
              ?? new RundanOptions();
builder.Services.AddSingleton(options);

// --- Data: one SQLite file on the App Service persistent /home disk ----------
var dbPath = ResolveDatabasePath(options, builder.Environment);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Foreign Keys=True"));

// Uploaded images live next to the database (on the persistent disk) and are served at /uploads.
var uploadsDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "uploads");
Directory.CreateDirectory(uploadsDir);
builder.Services.AddSingleton(new StoragePaths { UploadsDir = uploadsDir });

// --- Services ---------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JoinCodeGenerator>();
builder.Services.AddSingleton<PushService>();
builder.Services.AddScoped<ScoreboardService>();
builder.Services.AddScoped<EventStandingsService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<BracketService>();
builder.Services.AddScoped<WordGameService>();
builder.Services.AddScoped<ActivityLibraryService>();
builder.Services.AddScoped<SimulationService>();
builder.Services.AddScoped<SlapService>();
builder.Services.AddScoped<QuestionLibraryService>();
builder.Services.AddScoped<LibrarySeeder>();
builder.Services.AddScoped<ScoreboardNotifier>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<MaintenanceService>();

builder.Services.AddSignalR();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RuleViolationExceptionHandler>();

// In production behind App Service, give HTTPS redirection a deterministic target port
// (TLS terminates at the 443 edge) so it never fails to resolve a port. In dev the
// auto-detected Kestrel HTTPS port is used instead.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHttpsRedirection(o => o.HttpsPort = 443);
}

// Azure App Service terminates TLS and forwards the original scheme — honour it so
// HTTPS redirection doesn't loop.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// --- Migrate the database on startup (creates the file + schema on first run) -
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    // WAL improves read concurrency (scoreboard reads while a write is in flight).
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    // The question library is reference data — seed it whenever it's empty (also in production).
    await scope.ServiceProvider.GetRequiredService<LibrarySeeder>().SeedAsync();

    if (options.SeedOnStartup)
    {
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
    }
}

// --- HTTP pipeline ----------------------------------------------------------
app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve the Blazor WebAssembly client (these are public; all data is behind the API gate).
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// Serve admin-uploaded images from the persistent disk at /uploads.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads",
});

// Gate the API and SignalR hub behind the shared access code.
app.UseMiddleware<AccessCodeMiddleware>();

app.MapBootstrapEndpoints();
app.MapUserEndpoints();
app.MapEventEndpoints();
app.MapActivityEndpoints();
app.MapParticipantEndpoints();
app.MapQuestionEndpoints();
app.MapGameplayEndpoints();
app.MapMapPinEndpoints();
app.MapBracketEndpoints();
app.MapWordGameEndpoints();
app.MapSimulationEndpoints();
app.MapQuestionLibraryEndpoints();
app.MapMaintenanceEndpoints();

app.MapHub<ScoreboardHub>(HubRoutes.Scoreboard);

app.MapFallbackToFile("index.html");

app.Run();

// --- Helpers ----------------------------------------------------------------
static string ResolveDatabasePath(RundanOptions options, IWebHostEnvironment env)
{
    if (!string.IsNullOrWhiteSpace(options.DatabasePath))
    {
        var explicitDir = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            Directory.CreateDirectory(explicitDir);
        }

        return options.DatabasePath;
    }

    // On Azure App Service, HOME points at the persistent /home share.
    var home = Environment.GetEnvironmentVariable("HOME");
    var directory = !string.IsNullOrWhiteSpace(home)
        ? Path.Combine(home, "data")
        : Path.Combine(env.ContentRootPath, "App_Data");

    Directory.CreateDirectory(directory);
    return Path.Combine(directory, "rundan.db");
}
