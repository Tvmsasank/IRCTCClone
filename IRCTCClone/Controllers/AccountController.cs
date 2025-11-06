using IRCTCClone.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using Microsoft.AspNetCore.Authorization;

namespace IRCTCClone.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _connectionString;

        public AccountController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // -------------------- LOGIN --------------------
        /*    [HttpGet]
            public IActionResult Login()
            {

                return View(new ViewModels());
            }
    */
        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new ViewModels());
        }

        [HttpPost]
        public async Task<IActionResult> Login(ViewModels model, string returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            bool validUser = false;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_CheckUserLogin", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Email", model.Email);

                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        validUser = result.ToString() == HashPassword(model.Password);
                    }
                }
            }

            if (!validUser)
            {
                ModelState.AddModelError("", "Invalid email or password");
                return View(model);
            }

            // Sign in user
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, model.Email),
                new Claim(ClaimTypes.NameIdentifier, model.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // ✅ Return the user to the original URL (Checkout + parameters)
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            // fallback
            if (string.IsNullOrEmpty(returnUrl))
            {
                return RedirectToAction("Index", "Home");
            }

            return Redirect(returnUrl);

        }


        /* [HttpPost]
         public async Task<IActionResult> Login(ViewModels model)
         {
             if (!ModelState.IsValid) return View(model);

             bool validUser = false;

             using (var conn = new SqlConnection(_connectionString))
             {
                 conn.Open();
                 using (var cmd = new SqlCommand("sp_CheckUserLogin", conn))
                 {
                     cmd.CommandType = CommandType.StoredProcedure;
                     cmd.Parameters.AddWithValue("@Email", model.Email);

                     var result = cmd.ExecuteScalar();
                     if (result != null)
                     {
                         validUser = result.ToString() == HashPassword(model.Password);
                     }
                 }
             }

             if (!validUser)
             {
                 ModelState.AddModelError("", "Invalid email or password");
                 return View(model);
             }

             // Sign in user
             var claims = new List<Claim>
             {
                 new Claim(ClaimTypes.Name, model.Email),
                 new Claim(ClaimTypes.NameIdentifier, model.Email)
             };
             var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
             var principal = new ClaimsPrincipal(identity);

             await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

             return RedirectToAction("Checkout", "Booking" );
         }*/

        // -------------------- LOGOUT --------------------
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        // -------------------- REGISTER --------------------
        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        public IActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                return View(model);
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_RegisterUser", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Email", model.Email);
                    cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(model.Password));
                    cmd.Parameters.AddWithValue("@FullName", model.FullName);

                    var result = cmd.ExecuteScalar();
                    if (result != null && result.ToString() == "-1")
                    {
                        ModelState.AddModelError("", "Email already registered.");
                        return View(model);
                    }
                }
            }

            TempData["Success"] = "Registration successful! Please login.";
            return RedirectToAction("Register");
        }

        // -------------------- FORGOT PASSWORD --------------------
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ForgotPassword(string email, string newPassword)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_UpdatePassword", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(newPassword));

                    var result = cmd.ExecuteScalar();

                    if (result != null && result.ToString() == "-1")
                    {
                        ViewBag.Error = "❌ Email not found!";
                        return View();
                    }

                    ViewBag.Message = "✅ Password updated successfully! You can now login.";
                }
            }

            return View();
        }

        // -------------------- PASSWORD HASH --------------------
        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
