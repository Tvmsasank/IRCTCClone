using IrctcClone.Helpers;
using IRCTCClone.Models;  // For Station, Train, etc.
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    public class HomeController : Controller
    {
        private readonly string _connectionString;

        public HomeController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index(string id)
        {

            var stations = Station.GetAll(_connectionString);
            var (minDate, maxDate) = TatkalHelper.GetTatkalDateRange();

            ViewBag.TatkalMinDate = minDate.ToString("yyyy-MM-dd");
            ViewBag.TatkalMaxDate = maxDate.ToString("yyyy-MM-dd");

            // 🔴 LOGIN CONTROL (09:45 & 10:45)
            ViewBag.ForceLogin = TatkalHelper.ShouldForceLogin();

            // 🟡 AUTO TATKAL (09:50 & 10:50)
            ViewBag.AutoTatkal = TatkalHelper.ShouldAutoSelectTatkal();

            // existing
            ViewBag.IsLoggedIn = User.Identity.IsAuthenticated;

            return View(stations);
        }
    }
}
