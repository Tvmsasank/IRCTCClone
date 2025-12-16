using IRCTCClone.Data;
using IRCTCClone.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ✅ MVC, Razor, Session
builder.Services.AddControllersWithViews();
builder.Services.AddTransient<EmailService>();
builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ✅ Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
    });

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ✅ ✅ ✅ RATE LIMITER (ONLY ONE BLOCK)
builder.Services.AddRateLimiter(options =>
{
    // ✅ GLOBAL LIMITER (Aadhaar-aware)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string key;

        // Aadhaar verified users -> limit by user
        if (context.Session.GetString("AadhaarVerified") == "true" &&
            context.User.Identity?.IsAuthenticated == true)
        {
            key = context.User.Identity.Name;
        }
        else
        {
            // Non-verified users -> limit by IP
            key = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(10)
            });
    });

    // ✅ Used in controllers where needed
    options.AddFixedWindowLimiter("DefaultPolicy", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromSeconds(10);
    });

    options.AddFixedWindowLimiter("LoginLimiter", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
    });

    options.AddFixedWindowLimiter("SearchLimiter", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromSeconds(20);
    });

    options.AddFixedWindowLimiter("BookingLimiter", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
    });

    options.AddFixedWindowLimiter("StationLimiter", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromSeconds(5);
    });

    options.RejectionStatusCode = 429;
});

builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();

var app = builder.Build();

// ✅ Error Handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// ✅ Session BEFORE auth
app.UseSession();

// ✅ Authentication + Authorization
app.UseAuthentication();
app.UseAuthorization();

// ✅ IRCTC Aadhaar 8AM–10AM booking restriction
/*app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower();

    if (path != null && path.Contains("/booking/confirm"))
    {
        var aadhaar = context.Session.GetString("AadhaarVerified") ?? "false";

        var now = DateTime.Now.TimeOfDay;
        var start = new TimeSpan(8, 0, 0);
        var end = new TimeSpan(10, 0, 0);

        if (now >= start && now <= end && aadhaar != "true")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync(
                "⛔ Booking between 8 AM and 10 AM requires Aadhaar verification."
            );
            return;
        }
    }

    await next();
});*/

// ✅ Rate Limiter AFTER Authentication
app.UseRateLimiter();

// ✅ Mapping Routes
app.MapControllers();  // ❗ Do NOT attach global rate limit here
app.MapRazorPages();

// ✅ Default Route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
