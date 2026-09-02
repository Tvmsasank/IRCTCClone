using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Controllers
{
    public class RefreshLogoutMiddleware
    {
        private readonly RequestDelegate _next;

        public RefreshLogoutMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Headers["Cache-Control"] == "max-age=0")
            {
                context.Session.Clear();

                await context.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);

                context.Response.Redirect("/Home/Index");
                return;
            }

            await _next(context);
        }
    }
}
