using IrctcClone.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace IRCTCClone.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _connectionString;

        public AccountController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public IActionResult Login()
        {
            // Pass ReturnUrl to View via ViewData
       
            return View(new ViewModels());
        }

        [HttpPost]
        public async Task<IActionResult> Login(ViewModels model)
        {
            if (!ModelState.IsValid) return View(model);

            bool validUser = false;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT PasswordHash FROM Usrs WHERE Email=@Email", conn))
                {
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
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, model.Email), new Claim(ClaimTypes.NameIdentifier, model.Email) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            //// Redirect to booking page if present
            //if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            //{
            //    return Redirect(returnUrl);
            //}

            return RedirectToAction("Index", "Home");
        }


        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            // Sign out the user and clear the authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Optionally clear all session data
            HttpContext.Session.Clear();

            // Redirect to Home page
            return RedirectToAction("Index", "Home");
        }


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

                // Check if email exists
                using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Usrs WHERE Email = @Email", conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", model.Email);
                    int exists = (int)checkCmd.ExecuteScalar();
                    if (exists > 0)
                    {
                        ModelState.AddModelError("", "Email already registered.");
                        return View(model);
                    }
                }

                // Insert new user
                using (var cmd = new SqlCommand(
                    "INSERT INTO Usrs (Email, PasswordHash, FullName) VALUES (@Email, @PasswordHash, @FullName)", conn))
                {
                    cmd.Parameters.AddWithValue("@Email", model.Email);
                    cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(model.Password));
                    cmd.Parameters.AddWithValue("@FullName", (model.FullName));
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Registration successful! Please login.";
            return RedirectToAction("Login");
        }

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
