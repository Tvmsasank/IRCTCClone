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
        public async Task<IActionResult> Login(string returnUrl = null)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                try
                {
                    await HttpContext.SignOutAsync();
                    HttpContext.Session.Clear();
                    foreach (var cookie in Request.Cookies.Keys)
                    {
                        Response.Cookies.Delete(cookie);
                    }
                }
                catch { }
            }

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

                //string sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");

                Console.WriteLine("LOGIN SESSION ID = " + HttpContext.Session.Id);
                //Console.WriteLine("SESSION CAPTCHA = " + sessionCaptcha);
                //Console.WriteLine("USER CAPTCHA = " + model.CaptchaInput);

/*                if (string.IsNullOrEmpty(sessionCaptcha) || model.CaptchaInput?.ToUpper() != sessionCaptcha.ToUpper())
                {
                    TempData["Error"] = "Invalid captcha";
                    ViewBag.ReturnUrl = returnUrl;
                    return View(model);
                }
*/
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

                //HttpContext.Session.Remove("CAPTCHA");

                HttpContext.Session.SetString("username", fullName);
                HttpContext.Session.SetString("irctc_username", model.Username);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, fullName),
                    new Claim(ClaimTypes.NameIdentifier, emailFromDb),
                    new Claim(ClaimTypes.Email, emailFromDb),
                    new Claim("IRCTCUsername", model.Username),
                    new Claim(ClaimTypes.Role, "User")
                };

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

                var principal = new ClaimsPrincipal(identity);

                var sessionToken = Guid.NewGuid();

                HttpContext.Session.SetString("SessionToken", sessionToken.ToString());

                HttpContext.Session.SetString("UserEmail", emailFromDb);

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

                    // Synchronize authentic username with UserProfiles table
                    try
                    {
                        using (var syncCmd = new SqlCommand(@"
                            IF EXISTS (SELECT 1 FROM UserProfiles WHERE UserId = @Email OR Email = @Email)
                            BEGIN
                                UPDATE UserProfiles SET Username = @Username WHERE UserId = @Email OR Email = @Email;
                            END
                            ELSE
                            BEGIN
                                INSERT INTO UserProfiles (UserId, Username, FullName, Email, MobileNumber, DateOfBirth, Gender, Country, Address, IsAadhaarVerified, WalletBalance)
                                VALUES (@Email, @Username, @FullName, @Email, NULL, NULL, NULL, 'India', NULL, 0, 0.00);
                            END;
                            
                            -- Clean up hardcoded fallbacks that may have been previously inserted for other users
                            UPDATE UserProfiles 
                            SET MobileNumber = NULL, DateOfBirth = NULL, Address = NULL, Gender = NULL, IsAadhaarVerified = 0
                            WHERE Email != 'venkatamanishashankt@gmail.com' AND (MobileNumber LIKE '%9000485456%' OR Address LIKE '%House no 3-2-63%');", conn))
                        {
                            syncCmd.Parameters.AddWithValue("@Email", emailFromDb);
                            syncCmd.Parameters.AddWithValue("@Username", model.Username);
                            syncCmd.Parameters.AddWithValue("@FullName", fullName);
                            syncCmd.ExecuteNonQuery();
                        }
                    }
                    catch { }
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
            HttpContext.Session.Remove("FP_OTP");
            HttpContext.Session.Remove("FP_OTP_HASH");
            HttpContext.Session.Remove("FP_EMAIL");
            HttpContext.Session.Remove("FP_USERNAME");
            HttpContext.Session.Remove("FP_EXPIRY");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPasswordVerify([FromBody] ForgotPasswordStep1Request request)
        {
            if (request == null)
            {
                return Json(new { success = false, message = "Invalid request data." });
            }

            // 1. Verify Captcha
            var sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");
            if (string.IsNullOrWhiteSpace(sessionCaptcha) || 
                !sessionCaptcha.Equals(request.Captcha?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Invalid or expired Captcha. Please enter the characters shown in the image." });
            }

            string usernameInput = request.Username?.Trim() ?? "";
            string emailInput = request.Email?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(usernameInput) || string.IsNullOrWhiteSpace(emailInput))
            {
                return Json(new { success = false, message = "IRCTC User Name and Email Id are required." });
            }

            // 2. Query Usrs & UserProfiles for matching account
            string foundUsername = "";
            string foundEmail = "";
            string foundFullName = "";
            string foundMobile = "";

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand(@"
                        SELECT TOP 1 u.Username, u.Email, u.FullName, ISNULL(p.MobileNumber, '') AS MobileNumber
                        FROM Usrs u
                        LEFT JOIN UserProfiles p ON LOWER(LTRIM(RTRIM(p.UserId))) = LOWER(LTRIM(RTRIM(u.Email))) 
                                                 OR LOWER(LTRIM(RTRIM(p.Email))) = LOWER(LTRIM(RTRIM(u.Email)))
                                                 OR LOWER(LTRIM(RTRIM(p.Username))) = LOWER(LTRIM(RTRIM(u.Username)))
                        WHERE (LOWER(LTRIM(RTRIM(u.Username))) = LOWER(LTRIM(RTRIM(@Username))) 
                               OR LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(@Username))))
                          AND (LOWER(LTRIM(RTRIM(u.Email))) = LOWER(LTRIM(RTRIM(@Email)))
                               OR LOWER(LTRIM(RTRIM(p.Email))) = LOWER(LTRIM(RTRIM(@Email))))", conn))
                    {
                        cmd.Parameters.AddWithValue("@Username", usernameInput);
                        cmd.Parameters.AddWithValue("@Email", emailInput);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                foundUsername = reader["Username"]?.ToString()?.Trim() ?? usernameInput;
                                foundEmail = reader["Email"]?.ToString()?.Trim() ?? emailInput;
                                foundFullName = reader["FullName"]?.ToString()?.Trim() ?? foundUsername;
                                foundMobile = reader["MobileNumber"]?.ToString()?.Trim() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error verifying account in ForgotPassword: " + ex.Message);
                return Json(new { success = false, message = "Database error occurred while checking account details." });
            }

            if (string.IsNullOrWhiteSpace(foundEmail))
            {
                return Json(new { success = false, message = "No account found matching this IRCTC User Name and Email Id. Please verify your details." });
            }

            // 3. Mask Email and Mobile
            string maskedEmail = "";
            if (foundEmail.Contains("@"))
            {
                var parts = foundEmail.Split('@');
                var name = parts[0];
                var domain = parts[1];
                if (name.Length <= 2)
                {
                    maskedEmail = name + "***@" + domain;
                }
                else
                {
                    maskedEmail = name.Substring(0, 2) + new string('*', Math.Min(6, name.Length - 2)) + (name.Length > 4 ? name.Substring(name.Length - 2) : "") + "@" + domain;
                }
            }
            else
            {
                maskedEmail = foundEmail;
            }

            string cleanMobile = Regex.Replace(foundMobile, @"[^\d]", "");
            if (cleanMobile.Length > 10 && cleanMobile.StartsWith("91"))
            {
                cleanMobile = cleanMobile.Substring(cleanMobile.Length - 10);
            }
            string maskedMobile = "";
            if (cleanMobile.Length >= 6)
            {
                maskedMobile = "******" + cleanMobile.Substring(cleanMobile.Length - 4);
            }
            else if (cleanMobile.Length > 0)
            {
                maskedMobile = "***" + cleanMobile;
            }
            else
            {
                maskedMobile = "Not registered";
            }

            // 4. Generate OTP
            string otp = GenerateOTP();
            string hashedOtp = HashOTP(otp);

            // Save in session (10 min expiry)
            HttpContext.Session.SetString("FP_OTP", otp);
            HttpContext.Session.SetString("FP_OTP_HASH", hashedOtp);
            HttpContext.Session.SetString("FP_EMAIL", foundEmail);
            HttpContext.Session.SetString("FP_USERNAME", foundUsername);
            HttpContext.Session.SetString("FP_EXPIRY", DateTime.UtcNow.AddMinutes(10).ToString("o"));

            // Also attempt spRequestOtp if available
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("spRequestOtp", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@UserId", foundEmail);
                        cmd.Parameters.AddWithValue("@OtpHash", hashedOtp);
                        cmd.Parameters.AddWithValue("@Purpose", "FORGOT_PASSWORD");

                        var expiryParam = new SqlParameter("@ExpiryTime", SqlDbType.DateTime)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(expiryParam);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Notice spRequestOtp in ForgotPassword: " + ex.Message);
            }

            // 5. Send OTP Email
            try
            {
                await SendOTPEmail(foundEmail, otp, !string.IsNullOrWhiteSpace(foundFullName) ? foundFullName : foundUsername);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Notice SendOTPEmail in ForgotPassword: " + ex.Message);
            }

            Console.WriteLine($"[FORGOT PASSWORD OTP] Username: {foundUsername}, Email: {foundEmail}, OTP: {otp}");

            return Json(new
            {
                success = true,
                username = foundUsername,
                maskedEmail = maskedEmail,
                maskedMobile = maskedMobile,
                message = $"Verification code has been sent to your registered Email id {maskedEmail} and Mobile no. {maskedMobile}."
            });
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPasswordResendOtp()
        {
            var email = HttpContext.Session.GetString("FP_EMAIL");
            var username = HttpContext.Session.GetString("FP_USERNAME");

            if (string.IsNullOrWhiteSpace(email))
            {
                return Json(new { success = false, message = "Session expired. Please start the process again." });
            }

            string otp = GenerateOTP();
            string hashedOtp = HashOTP(otp);

            HttpContext.Session.SetString("FP_OTP", otp);
            HttpContext.Session.SetString("FP_OTP_HASH", hashedOtp);
            HttpContext.Session.SetString("FP_EXPIRY", DateTime.UtcNow.AddMinutes(10).ToString("o"));

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("spRequestOtp", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@UserId", email);
                        cmd.Parameters.AddWithValue("@OtpHash", hashedOtp);
                        cmd.Parameters.AddWithValue("@Purpose", "FORGOT_PASSWORD");

                        var expiryParam = new SqlParameter("@ExpiryTime", SqlDbType.DateTime)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(expiryParam);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch { }

            try
            {
                await SendOTPEmail(email, otp, username ?? "Customer");
            }
            catch { }

            Console.WriteLine($"[RESEND FORGOT PASSWORD OTP] Email: {email}, OTP: {otp}");

            return Json(new { success = true, message = "A new verification OTP has been sent." });
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPasswordReset([FromBody] ForgotPasswordStep2Request request)
        {
            if (request == null)
            {
                return Json(new { success = false, message = "Invalid request data." });
            }

            // 1. Verify Captcha
            var sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");
            if (string.IsNullOrWhiteSpace(sessionCaptcha) || 
                !sessionCaptcha.Equals(request.Captcha?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Invalid or expired Captcha. Please enter the characters shown in the image." });
            }

            // 2. Validate passwords match and strength
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword != request.ConfirmPassword)
            {
                return Json(new { success = false, message = "New Password and Confirm Password do not match." });
            }

            if (request.NewPassword.Length < 8 || request.NewPassword.Length > 20)
            {
                return Json(new { success = false, message = "Password must be between 8 and 15 characters in length." });
            }

            if (!Regex.IsMatch(request.NewPassword, @"[a-z]") ||
                !Regex.IsMatch(request.NewPassword, @"[A-Z]") ||
                !Regex.IsMatch(request.NewPassword, @"[0-9]") ||
                !Regex.IsMatch(request.NewPassword, @"[\W_]"))
            {
                return Json(new { success = false, message = "Password must contain at least one uppercase letter, one lowercase letter, one numeric digit, and one special character." });
            }

            // 3. Verify OTP
            var sessionEmail = HttpContext.Session.GetString("FP_EMAIL");
            var sessionOtp = HttpContext.Session.GetString("FP_OTP");
            var expiryStr = HttpContext.Session.GetString("FP_EXPIRY");

            if (string.IsNullOrWhiteSpace(sessionEmail) || string.IsNullOrWhiteSpace(sessionOtp))
            {
                return Json(new { success = false, message = "OTP session has expired. Please request a new OTP." });
            }

            if (DateTime.TryParse(expiryStr, out var expiryTime) && DateTime.UtcNow > expiryTime)
            {
                return Json(new { success = false, message = "OTP has expired. Please click Resend OTP." });
            }

            bool isOtpValid = false;
            if (sessionOtp == request.Otp?.Trim())
            {
                isOtpValid = true;
            }
            else
            {
                isOtpValid = VerifyOTP(sessionEmail, request.Otp?.Trim() ?? "", "FORGOT_PASSWORD");
            }

            if (!isOtpValid)
            {
                return Json(new { success = false, message = "Invalid OTP. Please enter the correct verification code." });
            }

            // 4. Update Password in Database
            string newPasswordHash = HashPassword(request.NewPassword);
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Try stored procedure sp_UpdatePassword
                    try
                    {
                        using (var cmd = new SqlCommand("sp_UpdatePassword", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@Email", sessionEmail);
                            cmd.Parameters.AddWithValue("@PasswordHash", newPasswordHash);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch { }

                    // Also direct update on Usrs table
                    using (var directCmd = new SqlCommand(@"
                        UPDATE Usrs 
                        SET PasswordHash = @PasswordHash, FailedAttempts = 0, IsLocked = 0, LockTime = NULL 
                        WHERE LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@Email))) 
                           OR LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@Username)))", conn))
                    {
                        directCmd.Parameters.AddWithValue("@PasswordHash", newPasswordHash);
                        directCmd.Parameters.AddWithValue("@Email", sessionEmail);
                        directCmd.Parameters.AddWithValue("@Username", request.Username ?? "");
                        await directCmd.ExecuteNonQueryAsync();
                    }

                    // Also update LastPasswordUpdate on UserProfiles if exists
                    try
                    {
                        using (var profCmd = new SqlCommand(@"
                            UPDATE UserProfiles 
                            SET LastPasswordUpdate = GETDATE() 
                            WHERE LOWER(LTRIM(RTRIM(UserId))) = LOWER(LTRIM(RTRIM(@Email))) 
                               OR LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@Email))) 
                               OR LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@Username)))", conn))
                        {
                            profCmd.Parameters.AddWithValue("@Email", sessionEmail);
                            profCmd.Parameters.AddWithValue("@Username", request.Username ?? "");
                            await profCmd.ExecuteNonQueryAsync();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error updating password in ForgotPasswordReset: " + ex.Message);
                return Json(new { success = false, message = "Failed to update password. Please try again." });
            }

            // 5. Send Password Changed Email notification
            try
            {
                await SendPasswordChangedEmail(sessionEmail, request.Username ?? "Customer");
            }
            catch { }

            // Clear forgot password session
            HttpContext.Session.Remove("FP_OTP");
            HttpContext.Session.Remove("FP_OTP_HASH");
            HttpContext.Session.Remove("FP_EMAIL");
            HttpContext.Session.Remove("FP_USERNAME");
            HttpContext.Session.Remove("FP_EXPIRY");

            return Json(new
            {
                success = true,
                message = "Password updated successfully! You can now login with your new password.",
                redirectUrl = "/Account/Login"
            });
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
        private async Task SendOTPEmail(string userEmail, string otp, string userName = "Customer")
        {
            string fullname = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrWhiteSpace(fullname))
            {
                fullname = userName;
            }

            string subject = "IRCTC – Change Password OTP Verification";

            string body = $@"
            <div style='font-family: Arial, sans-serif; font-size:14px; color:#000;'>

                <p><strong>Dear {fullname},</strong></p>

                <p>Your request to change Password in your IRCTC user profile has been received.</p>

                <p>Please submit Email Id Verification OTP:</p>

                <h2 style='color:#d9534f; letter-spacing:2px;'>{otp}</h2>

                <p>This OTP is valid for <strong>10 minutes</strong>.</p>

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
        private async Task SendPasswordChangedEmail(string userEmail, string userName = "Customer")
        {
            string fullname = User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrWhiteSpace(fullname))
            {
                fullname = userName;
            }

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
