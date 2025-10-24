using IrctcClone.Models;
using IrctcClone.Data; // This will now be your ADO.NET Database helper
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;


namespace IrctcClone.Controllers
{
    public class AdminController : Controller
    {
        private readonly string _connectionString;

        public AdminController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // ===============================================
        // ADMIN LOGIN SYSTEM
        // ===============================================

        // GET: /Admin/Login
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AdminLogin()
        {
            return View();
        }

        // POST: /Admin/Login
        [AllowAnonymous]
        [HttpPost]
        public IActionResult AdminLogin(string username, string password)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var query = "SELECT COUNT(*) FROM Admins WHERE Username = @Username AND Password = @Password";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Username", username);
                    cmd.Parameters.AddWithValue("@Password", password);

                    int count = (int)cmd.ExecuteScalar();
                    if (count > 0)
                    {
                        HttpContext.Session.SetString("AdminUser", username);
                        return RedirectToAction("Dashboard", "Admin");
                    }
                    else
                    {
                        ViewBag.Error = "Invalid username or password!";
                        return View();
                    }
                }
            }
        }

        // GET: /Admin/Logout
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Adminlogin", "Admin");
        }

        // GET: /Admin/Dashboard
        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetString("AdminUser") == null)
                return RedirectToAction("AdminLogin");

            return View();
        }

        // GET: /Admin/Index
        public IActionResult Index()
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var query = @"
                    SELECT t.Id, t.Number, t.Name, t.FromStationId, t.ToStationId, t.Departure, t.Arrival, t.Duration,
                           f.Code AS FromCode, f.Name AS FromName,
                           tt.Code AS ToCode, tt.Name AS ToName
                    FROM Trains t
                    INNER JOIN Stations f ON t.FromStationId = f.Id
                    INNER JOIN Stations tt ON t.ToStationId = tt.Id
                    ORDER BY t.Name";

                using (var cmd = new SqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        trains.Add(new Train
                        {
                            Id = reader.GetInt32(0),
                            Number = reader.GetInt32(1),
                            Name = reader.GetString(2),
                            FromStationId = reader.GetInt32(3),
                            ToStationId = reader.GetInt32(4),
                            Departure = reader.GetTimeSpan(5),
                            Arrival = reader.GetTimeSpan(6),
                            Duration = reader.GetTimeSpan(7),
                            FromStation = new Station
                            {
                                Id = reader.GetInt32(3),
                                Code = reader.GetString(8),
                                Name = reader.GetString(9)
                            },
                            ToStation = new Station
                            {
                                Id = reader.GetInt32(4),
                                Code = reader.GetString(10),
                                Name = reader.GetString(11)
                            }
                        });
                    }
                }
            }

            return View(trains);
        }

        // View all stations
        public IActionResult StationList()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var query = "SELECT Id, Code, Name FROM Stations ORDER BY Name";
                using (var cmd = new SqlCommand(query, conn))
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

        // GET: /Admin/Create
        [HttpGet]
        public IActionResult CreateStation()
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

            ViewBag.Stations = stations;
            return View();
        }

        // POST: Create Station
        [HttpPost]
        public IActionResult CreateStation(Station station)
        {
            if (!ModelState.IsValid) return View(station);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var insert = "INSERT INTO Stations (Code, Name) VALUES (@Code, @Name)";
                using (var cmd = new SqlCommand(insert, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", station.Code);
                    cmd.Parameters.AddWithValue("@Name", station.Name);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("StationList");
        }

        // GET: Edit Station
        [HttpGet]
        public IActionResult EditStation(int id)
        {
            Station station = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var query = "SELECT Id, Code, Name FROM Stations WHERE Id = @Id";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            station = new Station
                            {
                                Id = reader.GetInt32(0),
                                Code = reader.GetString(1),
                                Name = reader.GetString(2)
                            };
                        }
                    }
                }
            }

            if (station == null)
                return NotFound();

            return View(station);
        }

        // POST: Edit Station
        [HttpPost]
        public IActionResult EditStation(Station station)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var update = "UPDATE Stations SET Code = @Code, Name = @Name WHERE Id = @Id";
                using (var cmd = new SqlCommand(update, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", station.Code);
                    cmd.Parameters.AddWithValue("@Name", station.Name);
                    cmd.Parameters.AddWithValue("@Id", station.Id);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("StationList");
        }

        // DELETE: Station
        [HttpPost]
        public IActionResult DeleteStation(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var delete = "DELETE FROM Stations WHERE Id = @Id";
                using (var cmd = new SqlCommand(delete, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("StationList");
        }


        // POST: /Admin/CreateTrain
        [HttpPost]
        public IActionResult CreateTrain(Train train, List<string> classCodes, List<decimal> fares, List<int> seats)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Insert Train
                var insertTrain = @"
                    INSERT INTO Trains (Number, Name, FromStationId, ToStationId, Departure, Arrival, Duration)
                    VALUES (@Number, @Name, @FromStationId, @ToStationId, @Departure, @Arrival, @Duration);
                    SELECT SCOPE_IDENTITY();";

                using (var cmd = new SqlCommand(insertTrain, conn))
                {
                    cmd.Parameters.AddWithValue("@Number", train.Number);
                    cmd.Parameters.AddWithValue("@Name", train.Name);
                    cmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);
                    cmd.Parameters.AddWithValue("@Departure", train.Departure);
                    cmd.Parameters.AddWithValue("@Arrival", train.Arrival);
                    cmd.Parameters.AddWithValue("@Duration", train.Duration);

                    train.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Insert TrainClasses
                for (int i = 0; i < classCodes.Count; i++)
                {
                    var insertClass = @"
                        INSERT INTO TrainClasses (TrainId, Code, Fare, SeatsAvailable)
                        VALUES (@TrainId, @Code, @Fare, @SeatsAvailable)";

                    using (var cmd = new SqlCommand(insertClass, conn))
                    {
                        cmd.Parameters.AddWithValue("@TrainId", train.Id);
                        cmd.Parameters.AddWithValue("@Code", classCodes[i]);
                        cmd.Parameters.AddWithValue("@Fare", fares[i]);
                        cmd.Parameters.AddWithValue("@SeatsAvailable", seats[i]);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("Index");
        }
    }
}




