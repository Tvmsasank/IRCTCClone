using IRCTCClone.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;




namespace IrctcClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    public class AdminController : Controller
    {
        private readonly string _connectionString;

        public AdminController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        //------------------------------------------ADMIN LOGIN-------------------------------------------//
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
            if (Admin.ValidateLogin(_connectionString, username, password))
            {
                HttpContext.Session.SetString("AdminUser", username);
                return RedirectToAction("Dashboard");
            }

            ViewBag.Error = "Invalid username or password!";
            return View();
        }


        //------------------------------------------ADMIN LOGOUT------------------------------------------//


        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("AdminLogin");
        }

        //--------------------------------------------DASHBOARD-------------------------------------------//
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetString("AdminUser") == null)
                return RedirectToAction("AdminLogin");

            return View();
        }

        //------------------------------------------TRAINS MANAGEMENT------------------------------------//
        // List all trains
        public IActionResult Index()
        {
            var trains = Train.GetAllTrains(_connectionString);
            return View(trains);
        }


        // ✅ 1. GET - Create Train
        [HttpGet]
        public IActionResult CreateTrain()
        {
            var stations = Station.GetAllStations(_connectionString);
            ViewBag.Stations = stations;
            return View();
        }


        // Create Train (POST)
        [HttpPost]
        public IActionResult CreateTrain(Train train, List<string> classCodes, List<string> BookingCode, List<decimal> fares, List<int> seats)
        {
            // 1️⃣ Validate class input
            if (classCodes == null || BookingCode == null || fares == null || seats == null || classCodes.Count != BookingCode.Count || classCodes.Count != fares.Count || classCodes.Count != seats.Count)
            {
                TempData["ErrorMessage"] = "Please provide valid class details!";
                return RedirectToAction("CreateTrain");
            }

            // 2️⃣ Check for duplicates
            if (Train.CheckDuplicate(_connectionString, train.Number, train.FromStationId, train.ToStationId))
            {
                TempData["ErrorMessage"] = "⚠️ A train with the same number or route already exists!";
                return RedirectToAction("CreateTrain");
            }

            // 3️⃣ Insert Train and get ID
            train.InsertTrain(_connectionString); // make sure this sets train.Id

            if (train.Id == 0)
            {
                TempData["ErrorMessage"] = "❌ Failed to create train!";
                return RedirectToAction("AddRoute", new { trainId = train.Id });
            }

            // 4️⃣ Insert Train Classes safely
            for (int i = 0; i < classCodes.Count; i++)
            {
                // Skip empty inputs
                if (string.IsNullOrWhiteSpace(classCodes[i]) || fares[i] <= 0 || seats[i] < 0)
                    continue;

                var trainClass = new TrainClass
                {
                    TrainId = train.Id,
                    Code = classCodes[i],
                    SeatPrefix = !string.IsNullOrWhiteSpace(BookingCode[i]) ? BookingCode[i].Trim() : null, // ✅ add booking code,
                    BaseFare = fares[i],
                    SeatsAvailable = seats[i]
                };
                trainClass.Insert(_connectionString);
            }

            TempData["SuccessMessage"] = "✅ Train created successfully!";
            return RedirectToAction("CreateTrain", new { trainId = train.Id });
        }


        private List<TrainRoute> GetTrainRoutes(int trainId)
        {
            var routes = new List<TrainRoute>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetTrainRoutesByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            routes.Add(new TrainRoute
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                TrainId = trainId,
                                StationId = Convert.ToInt32(reader["StationId"]),
                                StationName = Convert.ToString(reader["StationName"]),
                                StopNumber = Convert.ToInt32(reader["StopNumber"]),
                                ArrivalTime = reader["ArrivalTime"] == DBNull.Value ? null : (TimeSpan?)reader["ArrivalTime"],
                                DepartureTime = reader["DepartureTime"] == DBNull.Value ? null : (TimeSpan?)reader["DepartureTime"]
                            });
                        }
                    }
                }
            }

            return routes;
        }

        [HttpGet]
        public IActionResult AddRoute(int id)
        {
            // ✅ 1. Load the Train using the ID
            Train train = GetTrainById(id);   // we will write this function below
            ViewBag.Train = train;
            ViewBag.TrainId = id;

            // ✅ 2. Load Stations
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

            // ✅ 3. Load Train Routes
            var routes = GetTrainRoutes(id);
            ViewBag.Routes = routes;

            // ✅ 4. Return view with routes list
            return View("AddRoute", routes);
        }

        private Train GetTrainById(int trainId)
        {
            Train train = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGTById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Id", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = reader.GetInt32(0),
                                Number = reader.GetInt32(1),
                                Name = reader.GetString(2),
                                FromStation = new Station { Name = reader.GetString(3) },
                                ToStation = new Station { Name = reader.GetString(4) },
                                Departure = reader.GetTimeSpan(5),
                                Arrival = reader.GetTimeSpan(6),
                                Duration = reader.GetString(7)
                            };
                        }
                    }
                }
            }

            return train;
        }



        // ✅ POST: Add Route
        [HttpPost]
        public IActionResult AddRoute(int trainId, List<TrainRoute> routes, string deletedIds)
        {
            // 👇 Debug check (optional)
            if (trainId == 0)
            {
                TempData["ErrorMessage"] = "Train ID missing! Cannot add routes.";
                return RedirectToAction("TrainList");
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                if (!string.IsNullOrEmpty(deletedIds))
                {
                    using (var cmd = new SqlCommand("spDeleteTrainRoute", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Ids", deletedIds);
                        cmd.ExecuteNonQuery();
                    }
                }

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

            TempData["SuccessMessage"] = "✅ Train routes updated successfully!";
            return RedirectToAction("AddRoute");
        }



        public IActionResult GetTrainRoute(int trainId)
        {
            var routes = new List<TrainRoute>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetTrainRoutes", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            routes.Add(new TrainRoute
                            {
                                StationId = reader.GetInt32(reader.GetOrdinal("StationId")),
                                StopNumber = reader.GetInt32(reader.GetOrdinal("StopNumber")),
                                ArrivalTime = reader.IsDBNull(reader.GetOrdinal("ArrivalTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("ArrivalTime")),
                                DepartureTime = reader.IsDBNull(reader.GetOrdinal("DepartureTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("DepartureTime")),
                                Station = new Station
                                {
                                    Name = reader.GetString(reader.GetOrdinal("StationName"))
                                }
                            });
                        }
                    }
                }
            }

            return PartialView("TrainRoutePartial", routes);
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
            return RedirectToAction("EditStation");
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
            var routes = new List<TrainRoute>(); // ✅ NEW

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
                                Duration = reader["Duration"].ToString()
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
                                Id = Convert.ToInt32(reader["Id"]),
                                Code = reader["Code"].ToString(),
                                BaseFare = Convert.ToDecimal(reader["BaseFare"]),
                                SeatsAvailable = Convert.ToInt32(reader["SeatsAvailable"]),
                                SeatPrefix = reader["SeatPrefix"].ToString()
                            });
                        }
                    }
                }

                // --- ✅ Get train routes ---
                using (var cmd = new SqlCommand("spGetTrainRoutesByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            routes.Add(new TrainRoute
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                TrainId = id,
                                StationId = Convert.ToInt32(reader["StationId"]),

                                ArrivalTime = string.IsNullOrWhiteSpace(reader["ArrivalTime"]?.ToString())
                                    ? (TimeSpan?)null
                                    : TimeSpan.Parse(reader["ArrivalTime"].ToString()),

                                DepartureTime = string.IsNullOrWhiteSpace(reader["DepartureTime"]?.ToString())
                                    ? (TimeSpan?)null
                                    : TimeSpan.Parse(reader["DepartureTime"].ToString()),

                                StopNumber = Convert.ToInt32(reader["StopNumber"])
                            });
                        }
                    }
                }

            }

            ViewBag.Stations = stations;
            ViewBag.TrainClasses = classes;
            ViewBag.TrainRoutes = routes; // ✅ Pass routes to view

            return View(train);
        }


        [HttpPost]
        public IActionResult EditTrain(
            Train train,
            List<int> classIds,
            List<string> classCodes,
            List<decimal> fares,
            List<int> seats,
            List<int> routeStations,          // ✅ NEW
            List<string> routeArrivals,       // ✅ NEW
            List<string> routeDepartures,     // ✅ NEW
            List<int> routeOrder,             // ✅ NEW
            string deletedClassIds // 👈 add this
        )
        {
            try
            { 

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // --- Update train ---
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


                    // --- ✅ Delete removed classes ---
                    if (!string.IsNullOrEmpty(deletedClassIds))
                    {
                        using (var delCmd = new SqlCommand("spDeleteTrainClassesByIds", conn))
                        {
                            delCmd.CommandType = CommandType.StoredProcedure;
                            delCmd.Parameters.AddWithValue("@Ids", deletedClassIds);
                            delCmd.ExecuteNonQuery();
                        }
                    }

                    var seatPrefixes = Request.Form["SeatPrefix"].ToList();

                    // --- Update / Insert Train Classes ---
                    for (int i = 0; i < classCodes.Count; i++)
                    {
                        using (var cmd = new SqlCommand("spUpsertTrainClass", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            int classId = (classIds != null && i < classIds.Count) ? classIds[i] : 0;
                            decimal fare = (i < fares.Count) ? fares[i] : 0;           // ✅ scalar
                            int seat = (i < seats.Count) ? seats[i] : 0;               // ✅ scalar
                            string prefix = (i < seatPrefixes.Count) ? seatPrefixes[i] : ""; // ✅ scalar

                            cmd.Parameters.AddWithValue("@ClassId", classId);
                            cmd.Parameters.AddWithValue("@TrainId", train.Id);
                            cmd.Parameters.AddWithValue("@Code", classCodes[i]);
                            cmd.Parameters.AddWithValue("@BaseFare", fare);             // ✅ not fares
                            cmd.Parameters.AddWithValue("@SeatsAvailable", seat);       // ✅ not seats
                            cmd.Parameters.AddWithValue("@SeatPrefix", string.IsNullOrWhiteSpace(prefix) ? (object)DBNull.Value : prefix);
                            // ✅ not seatPrefixes

                            cmd.ExecuteNonQuery();
                        }
                    }

                    // --- ✅ Update / Insert Train Routes ---
                    // You can first delete old routes (optional) and then insert new ones
/*                    using (var delCmd = new SqlCommand("spDeleteTrainRoutesByTrainId", conn))
                    {
                        delCmd.CommandType = CommandType.StoredProcedure;
                        delCmd.Parameters.AddWithValue("@TrainId", train.Id);
                        delCmd.ExecuteNonQuery();
                    }*/

                    for (int i = 0; i < routeStations.Count; i++)
                    {
                        using (var cmd = new SqlCommand("spInsertTrainRoute", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@TrainId", train.Id);
                            cmd.Parameters.AddWithValue("@StationId", routeStations[i]);
                            cmd.Parameters.AddWithValue("@ArrivalTime", TimeSpan.Parse(routeArrivals[i]));
                            cmd.Parameters.AddWithValue("@DepartureTime", TimeSpan.Parse(routeDepartures[i]));
                            cmd.Parameters.AddWithValue("@StopNumber", routeOrder[i]);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                TempData["SuccessMessage"] = "Train details edited successfully!";
                return RedirectToAction("EditTrain", new { id = train.Id });
            }

            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error updating train: " + ex.Message;
                return RedirectToAction("EditTrain", new { id = train.Id });
            }
        }


        // GET: /Admin/DeleteTrain/5
        public IActionResult DeleteTrain(int id)
        {
            TrainClass.DeleteTrain(_connectionString, id);
            TempData["Success"] = "Train deleted successfully!";
            return RedirectToAction("TrainList");
        }

        public IActionResult TrainList()
        {
            var trains = Train.GetTrainsList(_connectionString);
            return View(trains);
        }

    }

}
