using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;   // ✅ Add this
using IrctcClone.Models;  // For Station, Train, etc.
using IrctcClone.Data;

namespace IrctcClone.Controllers
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
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT Id, Code, Name FROM Stations ORDER BY Name", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        stations.Add(new Station
                        {
                            Id = reader.GetInt32(0),
                            Code = reader.GetString(1),
                            Name = reader.GetString(2)
                        });
                    }
                }
            }

            return View(stations);
        }
    }
}
