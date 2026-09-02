using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Reflection.PortableExecutable;

    [ApiExplorerSettings(IgnoreApi = true)]
    public class CaptchaController : Controller
    {
        [HttpGet]
        [Route("captcha")]
        public IActionResult Generate()
        {
            string captchaText = GenerateRandomText(5);

            HttpContext.Session.SetString("CAPTCHA", captchaText);

            Console.WriteLine("CAPTCHA CREATED = " + captchaText);
            Console.WriteLine("CAPTCHA SESSION ID = " + HttpContext.Session.Id);

            int width = 150;
            int height = 50;

            using var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            using var ms = new MemoryStream();

            var rnd = new Random();

            // Background
            g.Clear(Color.White);

            // LIGHT noise dots (reduced)
            for (int i = 0; i < 30; i++) // 🔽 reduced from 100
            {
                bmp.SetPixel(rnd.Next(width), rnd.Next(height),
                    Color.LightGray);
            }

            using var font = new Font("Arial", 20, FontStyle.Bold);

            int x = 10;

            // Draw characters (LESS rotation, more readable)
            foreach (char c in captchaText)
            {
                g.ResetTransform();

                float angle = rnd.Next(-10, 10); // 🔽 reduced from ±20
                g.RotateTransform(angle);

                using var brush = new SolidBrush(Color.Black); // 🔥 fixed color (clear)

                g.DrawString(c.ToString(), font, brush, x, rnd.Next(5, 10));

                x += 25;
            }

            g.ResetTransform();

            // LIGHT lines (reduced)
            for (int i = 0; i < 3; i++) // 🔽 reduced from 8
            {
                using var pen = new Pen(Color.LightGray, 1);
                g.DrawLine(pen,
                    rnd.Next(width), rnd.Next(height),
                    rnd.Next(width), rnd.Next(height));
            }

            bmp.Save(ms, ImageFormat.Png);

            Response.Headers["Cache-Control"] = "no-store"; // 🔥 important

            return File(ms.ToArray(), "image/png");
        }

        private string GenerateRandomText(int length)
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789@$&";
            var rnd = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[rnd.Next(s.Length)]).ToArray());
        }

        [HttpPost]
        [Route("Captcha/ValidateCaptcha")]
        public IActionResult ValidateCaptcha(string captcha)
        {
            var sessionCaptcha =
                HttpContext.Session.GetString("CAPTCHA");

            if (string.IsNullOrWhiteSpace(sessionCaptcha))
            {
                return Json(new
                {
                    success = false,
                    message = "Captcha expired"
                });
            }

            bool isValid =
                sessionCaptcha.Equals(
                    captcha?.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            return Json(new
            {
                success = isValid
            });
        }
    }

}
