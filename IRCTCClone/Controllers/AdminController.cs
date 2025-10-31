using IRCTCClone.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Data;




namespace IrctcClone.Controllers
{
    public class AdminController : Controller
    {
        private readonly string _connectionString;

        public AdminController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // =====================================================
        // ADMIN LOGIN
        // =====================================================
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AdminLogin()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult AdminLogin(string username, string password)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_AdminLogin", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Username", username);
                    cmd.Parameters.AddWithValue("@Password", password);

                    int count = (int)cmd.ExecuteScalar();
                    if (count > 0)
                    {
                        HttpContext.Session.SetString("AdminUser", username);
                        return RedirectToAction("Dashboard");
                    }
                    else
                    {
                        ViewBag.Error = "Invalid username or password!";
                        return View();
                    }
                }
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("AdminLogin");
        }

        // =====================================================
        // DASHBOARD
        // =====================================================
        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetString("AdminUser") == null)
                return RedirectToAction("AdminLogin");

            return View();
        }

        // =====================================================
        // TRAINS MANAGEMENT
        // =====================================================
        // List all trains
        public IActionResult Index()
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_GetAllTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

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
        }

        // ✅ 1. GET - Create Train
        [HttpGet]
        public IActionResult CreateTrain()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetAllStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1)
                            });
                        }
                    }
                }
            }

            ViewBag.Stations = stations;
            return View();
        }

        // Create Train (POST)
        [HttpPost]
        public IActionResult CreateTrain(Train train, List<string> classCodes, List<decimal> fares, List<int> seats)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1️⃣ Check Duplicate Train
                using (var cmd = new SqlCommand("spCheckDuplicateTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Number", train.Number);
                    cmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);

                    int existingCount = Convert.ToInt32(cmd.ExecuteScalar());
                    if (existingCount > 0)
                    {
                        TempData["ErrorMessage"] = "⚠️ A train with the same number or route already exists!";
                        return RedirectToAction("CreateTrain");
                    }
                }

                // 2️⃣ Insert Train and get ID
                using (var cmd = new SqlCommand("spInsertTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Number", train.Number);
                    cmd.Parameters.AddWithValue("@Name", train.Name);
                    cmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);
                    cmd.Parameters.AddWithValue("@Departure", train.Departure);
                    cmd.Parameters.AddWithValue("@Arrival", train.Arrival);
                    cmd.Parameters.AddWithValue("@Duration", train.Duration);

                    train.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 3️⃣ Insert Classes
                for (int i = 0; i < classCodes.Count; i++)
                {
                    using (var cmd = new SqlCommand("spInsertTrainClass", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", train.Id);
                        cmd.Parameters.AddWithValue("@Code", classCodes[i]);
                        cmd.Parameters.AddWithValue("@Fare", fares[i]);
                        cmd.Parameters.AddWithValue("@SeatsAvailable", seats[i]);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            TempData["SuccessMessage"] = "✅ Train created successfully!";
            return RedirectToAction("AddRoute", new { trainId = train.Id });
        }



        // ✅ GET: Add Route
        public IActionResult AddRoute(int trainId)
        {
            ViewBag.TrainId = trainId;

            var stations = new List<Station>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetAllStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1)
                            });
                        }
                    }
                }
            }

            ViewBag.Stations = stations;
            return View();
        }

        // ✅ POST: Add Route
        [HttpPost]
        public IActionResult AddRoute(int trainId, List<TrainRoute> routes)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                foreach (var route in routes)
                {
                    using (var cmd = new SqlCommand("spInsertTrainRoute", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@StationId", route.StationId);
                        cmd.Parameters.AddWithValue("@StopNumber", route.StopNumber);
                        cmd.Parameters.AddWithValue("@ArrivalTime", (object?)route.ArrivalTime ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DepartureTime", (object?)route.DepartureTime ?? DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }
            }

            TempData["SuccessMessage"] = "✅ Train routes added successfully!";
            return RedirectToAction("Index");
        }


        private List<Station> GetAllStations()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spgetallstatns", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                Code = reader.GetString(reader.GetOrdinal("Code")),
                                Name = reader.GetString(reader.GetOrdinal("Name"))
                            });
                        }
                    }
                }
            }

            return stations;
        }


        [HttpGet]
        public IActionResult Delete(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Delete TrainClasses first
                using (var cmd = new SqlCommand("spDeleteTrainClasses", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.ExecuteNonQuery();
                }

                // Delete Train next
                using (var cmd = new SqlCommand("spDeleteTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Index");
        }


        // =====================================================
        // STATIONS MANAGEMENT
        // =====================================================
        public IActionResult StationList()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spgetallstatns", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

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
            }

            return View(stations);
        }


        [HttpGet]
        public IActionResult CreateStation()
        {
            return View();
        }

        [HttpPost]
        public IActionResult CreateStation(Station station)
        {
            if (!ModelState.IsValid)
                return View(station);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spCreateStation", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    cmd.Parameters.AddWithValue("@Name", station.Name.Trim());

                    var result = cmd.ExecuteScalar();

                    if (result != null && Convert.ToInt32(result) == -1)
                    {
                        TempData["ErrorMessage"] = "⚠️ Station with the same name or code already exists!";
                        return RedirectToAction("CreateStation");
                    }
                }
            }

            TempData["SuccessMessage"] = "✅ Station added successfully!";
            return RedirectToAction("CreateStation");
        }

        [HttpGet]
        public IActionResult EditStation(int id)
        {
            Station station = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetStationById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
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
            {
                TempData["ErrorMessage"] = $"⚠️ Station not found with ID: {id}";
                return RedirectToAction("StationList");
            }

            return View(station);
        }

        [HttpPost]
        public IActionResult EditStation(Station station)
        {
            if (!ModelState.IsValid)
                return View(station);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 🔹 Check for duplicate station (excluding current ID)
                using (var checkCmd = new SqlCommand("spCheckDuplicateStation_Update", conn))
                {
                    checkCmd.CommandType = CommandType.StoredProcedure;
                    checkCmd.Parameters.AddWithValue("@Id", station.Id);
                    checkCmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    checkCmd.Parameters.AddWithValue("@Code", station.Code.Trim());

                    int existingCount = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (existingCount > 0)
                    {
                        TempData["ErrorMessage"] = "⚠️ Station with the same name or code already exists!";
                        return View(station);
                    }
                }

                // 🔹 Update the station details
                using (var cmd = new SqlCommand("spUpdateStation", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Id", station.Id);
                    cmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    cmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["SuccessMessage"] = "✅ Station updated successfully!";
            return RedirectToAction("StationList");
        }

        [HttpPost]
        public IActionResult DeleteStation(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spDeleteStation", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["SuccessMessage"] = "✅ Station deleted successfully!";
            return RedirectToAction("StationList");
        }


        // GET: /Admin/EditTrain/5
        [HttpGet]
        public IActionResult EditTrain(int id)
        {
            Train train = null;
            var stations = new List<Station>();
            var classes = new List<TrainClass>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // --- Get train details ---
                using (var cmd = new SqlCommand("spGetTrainById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Number = Convert.ToInt32(reader["Number"]),
                                Name = reader["Name"].ToString(),
                                FromStationId = Convert.ToInt32(reader["FromStationId"]),
                                ToStationId = Convert.ToInt32(reader["ToStationId"]),
                                Departure = TimeSpan.Parse(reader["Departure"].ToString()),
                                Arrival = TimeSpan.Parse(reader["Arrival"].ToString()),
                                Duration = TimeSpan.Parse(reader["Duration"].ToString())
                            };
                        }
                    }
                }

                if (train == null)
                    return NotFound();

                // --- Get stations ---
                using (var cmd = new SqlCommand("spGetAllStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = reader["Name"].ToString()
                            });
                        }
                    }
                }

                // --- Get train classes ---
                using (var cmd = new SqlCommand("spGetTrainClassesByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            classes.Add(new TrainClass
                            {
                                Code = reader["Code"].ToString(),
                                Fare = Convert.ToDecimal(reader["Fare"]),
                                SeatsAvailable = Convert.ToInt32(reader["SeatsAvailable"])
                            });
                        }
                    }
                }
            }

            ViewBag.Stations = stations;
            ViewBag.TrainClasses = classes;

            return View(train);
        }

        // POST: /Admin/EditTrain
        [HttpPost]
        public IActionResult EditTrain(Train train, List<int> classIds, List<string> classCodes, List<decimal> fares, List<int> seats)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Update train
                using (var cmd = new SqlCommand("spUpdateTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", train.Id);
                    cmd.Parameters.AddWithValue("@Number", train.Number);
                    cmd.Parameters.AddWithValue("@Name", train.Name);
                    cmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);
                    cmd.Parameters.AddWithValue("@Departure", train.Departure);
                    cmd.Parameters.AddWithValue("@Arrival", train.Arrival);
                    cmd.Parameters.AddWithValue("@Duration", train.Duration);
                    cmd.ExecuteNonQuery();
                }

                // Update or insert train classes
                for (int i = 0; i < classCodes.Count; i++)
                {
                    using (var cmd = new SqlCommand("spUpsertTrainClass", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        int classId = (classIds != null && i < classIds.Count) ? classIds[i] : 0;

                        cmd.Parameters.AddWithValue("@ClassId", classId);
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

        // GET: /Admin/DeleteTrain/5
        public IActionResult DeleteTrain(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spDeleteTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Index");
        }

        public IActionResult TrainList()
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetAllTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Number = Convert.ToInt32(reader["Number"]),
                                Name = reader["Name"].ToString(),
                                FromStationId = Convert.ToInt32(reader["FromStationId"]),
                                ToStationId = Convert.ToInt32(reader["ToStationId"]),
                                Departure = TimeSpan.Parse(reader["Departure"].ToString()),
                                Arrival = TimeSpan.Parse(reader["Arrival"].ToString()),
                                Duration = TimeSpan.Parse(reader["Duration"].ToString()),
                                FromStation = new Station
                                {
                                    Id = Convert.ToInt32(reader["FromStationId"]),
                                    Name = reader["FromStationName"].ToString()
                                },
                                ToStation = new Station
                                {
                                    Id = Convert.ToInt32(reader["ToStationId"]),
                                    Name = reader["ToStationName"].ToString()
                                }
                            });
                        }
                    }
                }
            }

            return View(trains);
        }

    }

}