using IrctcClone.BackgroundServices;
using IrctcClone.Controllers;
using IrctcClone.Infrastructure.Messaging;
using IrctcClone.Services;
using IRCTCClone.Data;
using IRCTCClone.E_D;
using IRCTCClone.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

#region SERVICES

builder.Services.AddSignalR();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<FareService>();
builder.Services.AddScoped<PassengerAllocationService>();
builder.Services.AddSingleton<RabbitMqConnection>();
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<EmailConsumer>();
builder.Services.AddHostedService<ChartPreparationNotificationService>();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<StationResolverService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetRequiredService<IMemoryCache>();

    return new StationResolverService(
        config.GetConnectionString("DefaultConnection"),
        cache
    );
});
builder.Services.AddDistributedMemoryCache();
/*builder.Services.AddMemoryCache();*/

builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddTransient<EmailService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<RagService>();
builder.Services.AddScoped<AiSearchService>();

// SESSION
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(5);

    options.Cookie.Name = ".IRCTC.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    // CRITICAL
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
});

// AUTH COOKIE
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.ReturnUrlParameter = "returnUrl";
    options.Cookie.Name = ".IRCTC.Auth";

    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;

/*    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
*/
    // ✅ ONLY THIS BLOCK
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/Admin"))
        {
            ctx.Response.Redirect("/Admin/AdminLogin");
        }
        else
        {
            var returnUrl = ctx.Request.Path + ctx.Request.QueryString;
            var encoded = Uri.EscapeDataString(returnUrl);

            ctx.Response.Redirect($"/Account/Login?returnUrl={encoded}");
        }

        return Task.CompletedTask;
    };
});

// RATE LIMITER (IP based — NEVER session based)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string key;

        if (context.Session.GetString("AadhaarVerified") == "true" &&
            context.User.Identity?.IsAuthenticated == true)
        {
            key = context.User.Identity.Name!; // Use username as key for authenticated users
        }

        else
        {
            var realIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();

            if (string.IsNullOrEmpty(realIp))
                realIp = context.Connection.RemoteIpAddress?.ToString();
            key = realIp ?? "anonymous";
        }

        return RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0
            });
    });

    options.AddFixedWindowLimiter("DefaultPolicy", o =>
    {
        o.PermitLimit = 30;
        o.Window = TimeSpan.FromSeconds(10);
    });

    options.AddSlidingWindowLimiter("LoginLimiter", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromSeconds(10);
        o.SegmentsPerWindow = 1;
        o.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("SearchLimiter", o =>
    {
        o.PermitLimit = 15;
        o.Window = TimeSpan.FromSeconds(20);
    });

    options.AddFixedWindowLimiter("BookingLimiter", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
    });

    options.AddFixedWindowLimiter("StationLimiter", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromSeconds(5);
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "1";
        context.HttpContext.Response.StatusCode = 429;
    };

    options.RejectionStatusCode = 429;
});

#endregion

var app = builder.Build();

var forwardOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
};

// Trust Cloudflare tunnel
forwardOptions.KnownNetworks.Clear();
forwardOptions.KnownProxies.Clear();
forwardOptions.RequireHeaderSymmetry = false;
forwardOptions.ForwardLimit = null;

app.UseForwardedHeaders(forwardOptions);

app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseExceptionHandler("/Error/500");

// IMPORTANT: nothing before this
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseMiddleware<IRCTCClone.Infrastructure.SingleSessionMiddleware>();

/*app.UseMiddleware<NoCacheMiddleware>();
*/
/*app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";

    await next();
});*/

app.UseRateLimiter();

//app.UseMiddleware<RefreshLogoutMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TrainHub>("/trainHub");

app.MapRazorPages();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
