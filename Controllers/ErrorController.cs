using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace IrctcClone.Controllers
{
    public class ErrorController : Controller
    {
        [Route("Error/{statusCode}")]
        public async Task<IActionResult> HttpStatusCodeHandler(int statusCode)
        {
            ViewBag.StatusCode = statusCode;

            // Force Logout for critical cases
            if (statusCode == 401 || statusCode == 403 || statusCode == 500 || statusCode == 429)
            {
                await HttpContext.SignOutAsync(); //logout
                HttpContext.Session.Clear(); // clear session
            }

            switch (statusCode)
            {
                case 404:
                    ViewBag.ErrorMessage = "Page not found.";
                    break;

                case 429:
                    ViewBag.ErrorMessage = "Too many requests. Please try again later.";
                    break;

                case 500:
                    ViewBag.ErrorMessage = "Internal server error.";
                    break;

                default:
                    ViewBag.ErrorMessage = "Something went wrong. Please try again.";
                    break;
            }

            return View("Error");
        }

        [Route("Error")]
        public IActionResult Error()
        {
            return View("Error");
        }
    }
}
