using IRCTCClone.Data;
using IRCTCClone.Models;
using IRCTCClone.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IRCTCClone.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _connectionString;
        private readonly EmailService _emailService;

        public AccountController(IConfiguration configuration, EmailService emailService)
        { 
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _emailService = emailService;
        }

        // -------------------- LOGIN (GET) --------------------
        [EnableRateLimiting("LoginLimiter")]
        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new ViewModels());
        }

        // -------------------- LOGIN (POST) --------------------
        [EnableRateLimiting("LoginLimiter")]
        [HttpPost]
        public async Task<IActionResult> Login(ViewModels model, string returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.ReturnUrl = returnUrl;
                    return View(model);
                }

                // ================= CAPTCHA VALIDATION =================
                string sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");

                Console.WriteLine("LOGIN SESSION ID = " + HttpContext.Session.Id);
                Console.WriteLine("SESSION CAPTCHA = " + sessionCaptcha);
                Console.WriteLine("USER CAPTCHA = " + model.CaptchaInput);

                if (string.IsNullOrEmpty(sessionCaptcha) || model.CaptchaInput?.ToUpper() != sessionCaptcha.ToUpper())
                {
                    TempData["Error"] = "Invalid captcha";
                    ViewBag.ReturnUrl = returnUrl;
                    return View(model);
                }

                // ======================================================


                bool validUser = false;
                string fullName = "";
                string passwordHashFromDb = "";
                int failedAttempts = 0;
                bool isLocked = false;
                DateTime? lockTime = null;
                string emailFromDb = "";

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("sp_CheckUserLogin", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Username", model.Username);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                passwordHashFromDb = reader["PasswordHash"].ToString();
                                fullName = reader["FullName"].ToString();
                                failedAttempts = reader["FailedAttempts"] != DBNull.Value
                                    ? Convert.ToInt32(reader["FailedAttempts"])
                                    : 0;

                                isLocked = reader["IsLocked"] != DBNull.Value
                                    ? Convert.ToBoolean(reader["IsLocked"])
                                    : false;

                                lockTime = reader["LockTime"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["LockTime"])
                                    : (DateTime?)null;
                                validUser = passwordHashFromDb == HashPassword(model.Password);
                                emailFromDb = reader["Email"].ToString();
                            }
                            else
                            {
                                TempData["Error"] = "User not found";
                                ViewBag.ReturnUrl = returnUrl;
                                return View(model);
                            }
                        }
                    }
                }

                // =================== LOCK CHECK ===================
                if (isLocked && lockTime.HasValue &&
                    DateTime.Now < lockTime.Value.AddMinutes(15))
                {
                    TempData["Error"] =
                        "Account Locked. Try again after 15 minutes.";

                    ViewBag.ReturnUrl = returnUrl;
                    return View(model);
                }

                // Unlock after the time expires
                if (isLocked && lockTime.HasValue && DateTime.Now >= lockTime.Value.AddMinutes(15))
                {
                    ResetAttempts(emailFromDb);
                }

                // ==================================================
                // ================== PASSWORD CHECK ===============
                if (passwordHashFromDb != HashPassword(model.Password))
                {
                    failedAttempts++;

                    if (failedAttempts >= 5)
                    {
                        LockUser(emailFromDb);
                        TempData["Error"] = "Account locked after 5 failed attempts.";
                    }
                    else
                    {
                        UpdateFailedAttempts(emailFromDb, failedAttempts);
                        int remaining = 5 - failedAttempts;
                        TempData["Error"] = $"Invalid credentials. {remaining} attempts remaining.";
                    }

                    ViewBag.ReturnUrl = returnUrl;
                    return View(model);
                }

                // --- claims sign-in (use user's email or username in the claim) ---
                // ✅ Claims login
                // ================= CLAIMS LOGIN =================
                // ================= SUCCESS ======================
                ResetAttempts(emailFromDb);

                // clear captcha after validation
                HttpContext.Session.Remove("CAPTCHA");

                HttpContext.Session.SetString("username", fullName);

                var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.NameIdentifier, emailFromDb),
                new Claim(ClaimTypes.Email, emailFromDb),
                new Claim(ClaimTypes.Role, "User")
            };

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

                var principal = new ClaimsPrincipal(identity);

                var sessionToken = Guid.NewGuid();

                HttpContext.Session.SetString("SessionToken", sessionToken.ToString());

                HttpContext.Session.SetString("UserEmail",emailFromDb);

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spUpsertUserSession", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@UserId", emailFromDb);
                        cmd.Parameters.AddWithValue("@SessionToken", sessionToken);

                        cmd.ExecuteNonQuery();
                    }
                }

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = false,
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(60)
                    });


                // safe returnUrl redirect
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");

            }

            catch (Exception ex)
            {
                ModelState.AddModelError("", "Authentication failed due to system exception: " + ex.Message);
                return View(model);
            }
        }

        /*------------------------------------ UPDATE FAILED ATTEMPT --------------------------------------*/
        void UpdateFailedAttempts(string email, int attempts)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand("UPDATE Usrs SET FailedAttempts = @attempts WHERE Email = @Email", conn);

            cmd.Parameters.AddWithValue("@attempts", attempts);
            cmd.Parameters.AddWithValue("@Email", email);

            cmd.ExecuteNonQuery();
        }

        /*--------------------------------------- LOCK USER ---------------------------------------*/
        void LockUser(string email)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand("UPDATE Usrs SET IsLocked = 1, LockTime = GETDATE() WHERE Email = @Email", conn);

            cmd.Parameters.AddWithValue("@Email", email);

            cmd.ExecuteNonQuery();
        }

        /*----------------------------------- RESET ATTEMPTS -------------------------------------*/
        void ResetAttempts(string email)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using var cmd = new SqlCommand("UPDATE Usrs SET FailedAttempts = 0, IsLocked = 0, LockTime = NULL WHERE Email = @Email", conn);

            cmd.Parameters.AddWithValue("@Email", email);

            cmd.ExecuteNonQuery();
        }


        // -------------------- LOGOUT --------------------
/*        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            *//*
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();*//*

            // 🔥 1. Clear session
            HttpContext.Session.Clear();

            // 🔥 2. Sign out authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // 🔥 3. Delete cookies manually (extra safety)
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }
            return RedirectToAction("Index", "Home");
        }
*/
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            // 1. Clear session
            HttpContext.Session.Clear();

            // 2. Sign out authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // 3. Delete all cookies (VERY IMPORTANT for Cloudflare)
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }

            // 4. Prevent browser cache
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

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
            try
            {
                if (!ModelState.IsValid) return View(model);

                if (string.IsNullOrWhiteSpace(model.Username))
                {
                    ModelState.AddModelError("", "Username is required.");
                    return View(model);
                }

                if (!Regex.IsMatch(model.Username, @"^[a-zA-Z0-9_]+$"))
                {
                    ModelState.AddModelError("", "Invalid username format.");
                    return View(model);
                }

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
                        cmd.Parameters.AddWithValue("@Username", model.Username);

                        var result = cmd.ExecuteScalar();

                        if (result != null && result.ToString() == "-1")
                        {
                            ModelState.AddModelError("", "Email already registered.");
                            return View(model);
                        }

                        if (result.ToString() == "-2")
                        {
                            ModelState.AddModelError("", "Username already exists.");
                            return View(model);
                        }
                    }
                }

                TempData["Success"] = "Registration successful! Please login.";
                return RedirectToAction("Register");

            }

            catch (Exception ex)
            {
                ModelState.AddModelError("", "Database registration failed: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult CheckUsername(string username)
        {
            bool exists = false;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spCheckUsernameExists", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@Username", username);

                    exists = (int)cmd.ExecuteScalar() > 0;
                }
            }

            return Json(new { exists });
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

        // -------------------- CHANGE PASSWORD ------------------
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string newPassword, string enteredOTP)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            // STEP 1 → SEND OTP
            if (string.IsNullOrEmpty(enteredOTP))
            {
                string otp = GenerateOTP();
                string hashedOtp = HashOTP(otp);

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spRequestOtp", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@UserId", email);
                        cmd.Parameters.AddWithValue("@OtpHash", hashedOtp);
                        cmd.Parameters.AddWithValue("@Purpose", "CHANGE_PASSWORD");

                        // ✅ ADD THIS
                        var expiryParam = new SqlParameter("@ExpiryTime", SqlDbType.DateTime)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(expiryParam);

                        cmd.ExecuteNonQuery();
                    }
                }

                await SendOTPEmail(email, otp);

                await SendPasswordChangedEmail(email);

                return Json(new { success = true, step = "OTP_SENT" });
            }

            // STEP 2 → VERIFY OTP + CHANGE PASSWORD
            bool isValid = VerifyOTP(email, enteredOTP, "CHANGE_PASSWORD");

            if (!isValid)
            {
                return Json(new { success = false, message = "Invalid OTP" });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("sp_UpdatePassword", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@PasswordHash", HashPassword(newPassword));

                    cmd.ExecuteNonQuery();
                }
            }

            return Json(new { success = true });
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

        // GENERATE OTP
        public static string GenerateOTP()
        {
            Random rnd = new Random();
            return rnd.Next(100000, 999999).ToString(); // 6-digit
        }

        //HASH OTP
        public static string HashOTP(string otp)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(otp));
            return Convert.ToBase64String(bytes);
        }

        // GENERATE EMAIL
        private async Task SendOTPEmail(string userEmail, string otp)
        {
            string fullname = User.FindFirst(ClaimTypes.Name)?.Value;

            string subject = "IRCTC – Change Password OTP Verification";

            string body = $@"
            <div style='font-family: Arial, sans-serif; font-size:14px; color:#000;'>

                <p><strong>Dear {fullname},</strong></p>

                <p>Your request to change Password in your IRCTC user profile has been received.</p>

                <p>Please submit Email Id Verification OTP:</p>

                <h2 style='color:#d9534f; letter-spacing:2px;'>{otp}</h2>

                <p>This OTP is valid for <strong>1 minute</strong>.</p>

                <br/>

                <p><strong>*************************** Information ************************************</strong></p>

                <p>
                For any enquiries or information regarding your transaction, do not provide your 
                credit/debit card details by any means. All your queries can be handled based on 
                transaction details only.
                </p>

                <p>
                We do not store your password/OTP/card information in any form.
                </p>

                <p><strong>*****************************************************************************</strong></p>

                <br/>

                <p>Warm Regards,<br/>
                <strong>Customer Care</strong><br/>
                Internet Ticketing</p>

                <hr/>

                <p style='font-size:12px; color:#555;'>
                This is a system generated mail. Please do not reply to this Email ID.<br/>
                If you have any query:<br/>
                (1) Call our 24-hour Customer Care or<br/>
                (2) Visit: https://equery.irctc.co.in
                </p>

            </div>";

            await _emailService.SendEmail(userEmail, subject, body);
        }


        // PASSWORD CHANGE MAIL
        private async Task SendPasswordChangedEmail(string userEmail)
        {
            string fullname = User.FindFirst(ClaimTypes.Name)?.Value;

            string subject = "IRCTC – Password Changed Successfully";

            string body = $@"
            <div style='font-family: Arial, sans-serif; font-size:14px; color:#000;'>

                <p><strong>Dear {fullname},</strong></p>

                <p>Your password has been <strong>successfully changed</strong> for your IRCTC account.</p>

                <p>If you made this change, no further action is required.</p>

                <p style='color:red;'><strong>
                    If you did NOT change your password, please contact customer care immediately.
                </strong></p>

                <br/>

                <p>Warm Regards,<br/>
                <strong>Customer Care</strong><br/>
                Internet Ticketing</p>

                <hr/>

                <p style='font-size:12px; color:#555;'>
                This is a system generated mail. Please do not reply.<br/>
                For queries visit: https://equery.irctc.co.in
                </p>

            </div>";

            await _emailService.SendEmail(userEmail, subject, body);
        }


        // VERIFY OTP
        private bool VerifyOTP(string userId, string enteredOtp, string purpose)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string enteredOtpHash = HashOTP(enteredOtp);

                using (var cmd = new SqlCommand("spVerifyOTP", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@EnteredOtpHash", enteredOtpHash);
                    cmd.Parameters.AddWithValue("@Purpose", purpose);

                    var isValidParam = new SqlParameter("@IsValid", SqlDbType.Bit)
                    {
                        Direction = ParameterDirection.Output
                    };

                    cmd.Parameters.Add(isValidParam);

                    cmd.ExecuteNonQuery();

                    return isValidParam.Value != DBNull.Value && (bool)isValidParam.Value;
                }
            }

        }

        [HttpPost]
        public async Task<IActionResult> ForceLogout()
        {
            HttpContext.Session.Clear();

            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return Ok();
        }
    }
}
