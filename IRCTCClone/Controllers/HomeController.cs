using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;   // ✅ Add this
using IRCTCClone.Models;  // For Station, Train, etc.
using IRCTCClone.Data;

namespace IRCTCClone.Controllers
{
    public class HomeController : Controller
    {
        private readonly string _connectionString;

        public HomeController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index()
        {
            // ✅ Simply call the static method in the model
            var stations = Station.GetAll(_connectionString);
            return View(stations);
        }

    }
}
