using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartCommute.Server.Data;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

var builder = WebApplication.CreateBuilder(args);

// Local, git-ignored overrides (Neon connection string, JWT secret). See appsettings.Local.example.json.
// Environment variables and command-line arguments are added again so they still win over the file.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Hosts like Render tell the app which port to listen on through PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ---- Database (Neon PostgreSQL) ----
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
}
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No database connection string. Copy appsettings.Local.example.json to appsettings.Local.json and paste " +
        "your Neon connection string, or set the ConnectionStrings__DefaultConnection environment variable.");
}
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(ConnectionStringHelper.ToNpgsql(connectionString), npgsql => npgsql.EnableRetryOnFailure(3))
    .UseSnakeCaseNamingConvention());

// ---- Authentication (JWT) ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Secret.Length < 32)
{
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters. Set it in appsettings.Local.json or the Jwt__Secret environment variable.");
}
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<RewardOptions>(builder.Configuration.GetSection("Rewards"));
builder.Services.Configure<GeoOptions>(builder.Configuration.GetSection("Geo"));
builder.Services.Configure<GoogleMapsOptions>(builder.Configuration.GetSection("GoogleMaps"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role",
        };
        // Keep 401/403 bodies in the same JSON shape as every other error.
        options.Events = new JwtBearerEvents
        {
            // A valid signature is not enough: the token's session must still exist and not be signed out.
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst("jti")?.Value, out var sessionId))
                {
                    context.Fail("Token has no session.");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                var active = await db.UserSessions.AnyAsync(s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now);
                if (!active)
                {
                    context.Fail("Session has ended.");
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                var message = context.AuthenticateFailure is null
                    ? "Please sign in to continue."
                    : "Your session has expired. Please sign in again.";
                await context.Response.WriteAsJsonAsync(new ErrorResponse(message));
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new ErrorResponse("You are not allowed to do that."));
            },
        };
    });
builder.Services.AddAuthorization();

// ---- Rate limiting for endpoints that are easy to abuse (password/PIN guessing, SOS spam) ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
        new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse("Too many attempts. Please wait a minute and try again."), ct));
    // Counted per client IP and per endpoint, so signing up does not use up the login allowance.
    options.AddPolicy("strict", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Connection.RemoteIpAddress}|{context.Request.Path}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Google Maps requests are billed, so each caller gets a modest number per minute.
    options.AddPolicy("maps", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("id")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

// ---- CORS for the web client ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin =>
    {
        if (allowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        // In development accept the web client on any local port: Vite moves to 5174, 5175...
        // when 5173 is busy, and the page may be opened as localhost or 127.0.0.1.
        return isDevelopment && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
    })
    .AllowAnyHeader()
    .AllowAnyMethod()));

// ---- MVC ----
builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Return the first validation message in the shared { success, message, error } shape.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid request." : e.ErrorMessage)
                .FirstOrDefault() ?? "Invalid request.";
            return new BadRequestObjectResult(new ErrorResponse(message));
        };
    });

builder.Services.AddHttpClient<GeoService>((services, client) =>
{
    var geo = builder.Configuration.GetSection("Geo").Get<GeoOptions>() ?? new GeoOptions();
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(geo.UserAgent);
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<FriendCodeGenerator>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddSingleton<BusRouteCache>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GoogleRoutesService>(client => client.Timeout = TimeSpan.FromSeconds(10));

// The route list is a few hundred KB of JSON; compressed it is a small fraction of that.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Fastest);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "That changed while you were updating it. Please try again."),
        BadHttpRequestException bad => (bad.StatusCode, "Invalid request."),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong on the server."),
    };
    if (status >= 500)
    {
        app.Logger.LogError(error, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
    }
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ErrorResponse(message));
}));

// Serve uploaded verification images. The folder is created up front because the static file
// middleware ignores a web root that does not exist when the app starts.
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Check the Neon connection, then apply pending migrations and seed starter data.
// If the database is unreachable the server still starts, so the status page at "/" can report it.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Each step reports its own failure, so a migration or seeding problem is not mistaken for a bad connection.
    // Nothing here drops or resets data: migrations only add, and the seeder only inserts missing routes.
    var step = "connect";
    try
    {
        var serverTime = await db.Database.SqlQueryRaw<DateTime>("SELECT NOW() AS \"Value\"").SingleAsync();
        Console.WriteLine($"✅ Neon PostgreSQL Database Connected! (server time {serverTime:yyyy-MM-dd HH:mm:ss} UTC)");

        step = "migrate";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();
        Console.WriteLine(pending.Count == 0
            ? "✅ Database tables are up to date."
            : $"✅ Applied {pending.Count} migration(s): {string.Join(", ", pending)}");

        step = "seed";
        await DbSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
        // Load the routes into memory now, so the first visitor does not wait for the database.
        step = "cache";
        var routes = await app.Services.GetRequiredService<BusRouteCache>().AllAsync();
        Console.WriteLine($"✅ {routes.Count} bus routes in the database (loaded into memory).");
    }
    catch (Exception ex)
    {
        var reason = ex.GetBaseException().Message;
        switch (step)
        {
            case "connect":
                Console.WriteLine($"❌ Neon PostgreSQL Database Connection Failed: {reason}");
                Console.WriteLine("   Check ConnectionStrings:DefaultConnection in appsettings.Local.json.");
                break;
            case "migrate":
                Console.WriteLine($"❌ Database migration failed: {reason}");
                Console.WriteLine("   The database was left as it was. Fix the error above and restart the server.");
                break;
            case "cache":
                Console.WriteLine($"❌ Loading bus routes into memory failed: {reason}. They will load on the first request.");
                break;
            default:
                Console.WriteLine($"❌ Seeding starter data failed: {reason}");
                break;
        }
    }
}

try
{
    await app.RunAsync();
}
catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
{
    // Usually another copy of the server is still running. Explain that instead of printing a stack trace.
    Console.WriteLine($"❌ Could not start: {ex.Message}");
    Console.WriteLine("   Another program (often an earlier 'dotnet run') is already using this port.");
    Console.WriteLine("   Stop it with Ctrl+C in its terminal, or find it with: netstat -ano | findstr :3000");
    Environment.ExitCode = 1;
}
