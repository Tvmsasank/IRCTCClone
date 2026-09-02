using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;

namespace IRCTCClone.Infrastructure
{
    public class SingleSessionMiddleware
    {
        private readonly RequestDelegate _next;

        public SingleSessionMiddleware(
            RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(
            HttpContext context,
            IConfiguration configuration)
        {
            var email =
                context.Session.GetString("UserEmail");

            var currentToken =
                context.Session.GetString("SessionToken");

            if (!string.IsNullOrEmpty(email))
            {
                using var conn =
                    new SqlConnection(
                        configuration.GetConnectionString("DefaultConnection"));

                await conn.OpenAsync();

                var cmd =
                    new SqlCommand(
                        @"SELECT SessionToken
                          FROM UserSessions
                          WHERE UserId=@UserId",
                        conn);

                cmd.Parameters.AddWithValue(
                    "@UserId",
                    email);

                var dbToken =
                    Convert.ToString(
                        await cmd.ExecuteScalarAsync());

                if (dbToken != currentToken)
                {
                    context.Session.Clear();

                    await context.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);

                    context.Response.Redirect("/");

                    return;
                }
            }

            await _next(context);
        }
    }
}