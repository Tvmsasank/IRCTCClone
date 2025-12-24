using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using System.Drawing;
    using System.Drawing.Imaging;

    [ApiExplorerSettings(IgnoreApi = true)]
    public class CaptchaController : Controller
    {
        [HttpGet]
        [Route("captcha")]
        public IActionResult Generate()
        {
            string captchaText = GenerateRandomText(5);
            HttpContext.Session.SetString("CAPTCHA", captchaText);

            using var bmp = new Bitmap(120, 40);
            using var g = Graphics.FromImage(bmp);
            using var ms = new MemoryStream();

            g.Clear(Color.White);

            using var font = new Font("Arial", 20, FontStyle.Bold);
            g.DrawString(captchaText, font, Brushes.Black, 10, 5);

            var rnd = new Random();
            for (int i = 0; i < 8; i++)
            {
                g.DrawLine(Pens.Gray,
                    rnd.Next(120), rnd.Next(40),
                    rnd.Next(120), rnd.Next(40));
            }

            bmp.Save(ms, ImageFormat.Png);
            return File(ms.ToArray(), "image/png");
        }

        private string GenerateRandomText(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rnd = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[rnd.Next(s.Length)]).ToArray());
        }
    }

}
