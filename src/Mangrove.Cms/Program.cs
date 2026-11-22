using System.Threading.RateLimiting;
using Mangrove.Cms.Data;
using Mangrove.Cms.Models;
using Mangrove.Cms.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Storage -------------------------------------------------------------
// Everything that must survive an upgrade lives in App_Data (DB + key ring) and wwwroot/uploads.
var dataDir = builder.Configuration["Mangrove:DataDirectory"];
dataDir = string.IsNullOrWhiteSpace(dataDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data")
    : Path.GetFullPath(dataDir, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDir);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = $"Data Source={Path.Combine(dataDir, "mangrove.db")};Cache=Shared";

builder.Services.AddDbContext<CmsDbContext>(o => o.UseSqlite(connectionString));

// Persist the Data Protection key ring so logins and antiforgery tokens survive restarts.
builder.Services.AddDataProtection()
    .SetApplicationName("Mangrove.Cms")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

// ---- Auth ----------------------------------------------------------------
builder.Services.AddScoped<StampValidatingCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.AccessDeniedPath = "/account/denied";
        o.Cookie.Name = "mangrove.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.EventsType = typeof(StampValidatingCookieEvents);
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Administrator)));
    o.AddPolicy(Policies.Editor, p => p.RequireAuthenticatedUser()
        .RequireRole(nameof(UserRole.Administrator), nameof(UserRole.Editor)));
    o.AddPolicy(Policies.Author, p => p.RequireAuthenticatedUser()
        .RequireRole(nameof(UserRole.Administrator), nameof(UserRole.Editor), nameof(UserRole.Author)));
});

// Brute-force protection for login, registration and comment posting.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("forms", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---- App services ---------------------------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<ContentRenderer>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SlugService>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PostQueryService>();
builder.Services.AddScoped<TableService>();

// Emit Bangla, Arabic, CJK… as real characters instead of &#x…; entities.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddRouting(o => o.LowercaseUrls = true);
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.Configure<RazorViewEngineOptions>(o => o.ViewLocationExpanders.Add(new ThemeViewLocationExpander()));
// Each file is limited to MediaService.MaxBytes; one request (e.g. a notice with several files) to MaxRequestBytes.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = MediaService.MaxRequestBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MediaService.MaxRequestBytes);

// Running behind nginx / a reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---- Database: apply migrations on start ----------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSql($"PRAGMA journal_mode=WAL;");
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// First run: send every request to the setup wizard until an administrator exists.
// Also picks the active theme for this request.
app.Use(async (ctx, next) =>
{
    var settings = ctx.RequestServices.GetRequiredService<SettingsService>();
    var installed = await settings.GetBoolAsync(SettingKeys.Installed);
    var path = ctx.Request.Path;

    if (!installed && !path.StartsWithSegments("/setup") && !path.StartsWithSegments("/css")
        && !path.StartsWithSegments("/js") && !path.StartsWithSegments("/themes") && !path.StartsWithSegments("/error"))
    {
        ctx.Response.Redirect("/setup");
        return;
    }
    if (installed && path.StartsWithSegments("/setup"))
    {
        ctx.Response.Redirect("/");
        return;
    }

    var theme = await settings.GetAsync(SettingKeys.Theme);
    ctx.Items[ThemeService.HttpContextKey] = string.IsNullOrWhiteSpace(theme) ? "Mangrove" : theme;
    await next();
});

app.MapAreaControllerRoute("admin", "Admin", "admin/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllers(); // attribute-routed public site, account and setup

app.Run();

public partial class Program { }
