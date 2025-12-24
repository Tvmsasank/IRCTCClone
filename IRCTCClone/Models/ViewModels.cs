using System.ComponentModel.DataAnnotations;

namespace IRCTCClone.Models
{
    public class ViewModels
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
        public string? ReturnUrl { get; set; }
        public string FullName { get; set; } = string.Empty;
        public bool AadhaarVerified { get; set; }   // ✅ NEW
        public string? AadhaarNumber { get; set; }   // ✅ OPTIONAL
                                                     // 🔐 CAPTCHA
        public string CaptchaInput { get; set; }

    }
}
