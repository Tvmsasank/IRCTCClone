using IRCTCClone.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using IRCTCClone.Models;




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
                var query = "SELECT COUNT(*) FROM Admins WHERE Username = @Username AND Password = @Password";
                using (var cmd = new SqlCommand(query, conn))
                {
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

        // Create Train (GET)
        [HttpGet]
        public IActionResult CreateTrain()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = new SqlCommand("SELECT Id, Name FROM Stations ORDER BY Name", conn);
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

                // ===== Check for duplicate Train =====
                var duplicateQuery = @"
            SELECT COUNT(*) FROM Trains 
            WHERE Number = @Number 
               OR (FromStationId = @FromStationId AND ToStationId = @ToStationId)";
                using (var checkCmd = new SqlCommand(duplicateQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Number", train.Number);
                    checkCmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    checkCmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);

                    int existingCount = Convert.ToInt32(checkCmd.ExecuteScalar());
                    if (existingCount > 0)
                    {
                        TempData["ErrorMessage"] = "⚠️ A train with the same number or route already exists!";
                        return RedirectToAction("CreateTrain");
                    }
                }

                // ===== Get both station names =====
                var stationQuery = "SELECT Id, Name FROM Stations WHERE Id IN (@FromId, @ToId)";
                using (var cmd = new SqlCommand(stationQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@FromId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToId", train.ToStationId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int id = Convert.ToInt32(reader["Id"]);
                            string name = reader["Name"].ToString();

                            if (id == train.FromStationId)
                                train.FromStationName = name;
                            if (id == train.ToStationId)
                                train.ToStationName = name;
                        }
                    }
                }

                // ===== Assign Station objects =====
                train.FromStation = new Station { Id = train.FromStationId, Name = train.FromStationName };
                train.ToStation = new Station { Id = train.ToStationId, Name = train.ToStationName };

                // ===== Insert Train =====
                var insertTrain = @"
            INSERT INTO Trains 
            (Number, Name, FromStationId, ToStationId, Departure, Arrival, Duration, FromStationName, ToStationName)
            VALUES
            (@Number, @Name, @FromStationId, @ToStationId, @Departure, @Arrival, @Duration, @FromStationName, @ToStationName);
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
                    cmd.Parameters.AddWithValue("@FromStationName", train.FromStationName);
                    cmd.Parameters.AddWithValue("@ToStationName", train.ToStationName);

                    train.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // ===== Insert Train Classes =====
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

            TempData["SuccessMessage"] = "✅ Train created successfully!";
            return RedirectToAction("CreateTrain");
        }


        // Delete Train
        [HttpGet]
        public IActionResult Delete(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                var deleteClasses = "DELETE FROM TrainClasses WHERE TrainId = @TrainId";
                using (var cmd = new SqlCommand(deleteClasses, conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.ExecuteNonQuery();
                }

                var deleteTrain = "DELETE FROM Trains WHERE Id = @Id";
                using (var cmd = new SqlCommand(deleteTrain, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
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

                // ✅ Check if a station with the same Name or Code already exists
                string checkDuplicate = @"
            SELECT COUNT(*) FROM Stations 
            WHERE LOWER(Name) = LOWER(@Name) OR LOWER(Code) = LOWER(@Code)";

                using (var checkCmd = new SqlCommand(checkDuplicate, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    checkCmd.Parameters.AddWithValue("@Code", station.Code.Trim());

                    int existingCount = (int)checkCmd.ExecuteScalar();

                    if (existingCount > 0)
                    {
                        // ❌ Station already exists
                        TempData["ErrorMessage"] = "⚠️ Station with the same name or code already exists!";
                        return RedirectToAction("CreateStation"); // ✅ Redirect so TempData shows in view
                    }
                }

                // ✅ Insert new station if no duplicates
                string insert = "INSERT INTO Stations (Code, Name) VALUES (@Code, @Name)";
                using (var cmd = new SqlCommand(insert, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    cmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["SuccessMessage"] = "✅ Station added successfully!";
            return RedirectToAction("CreateStation"); // Redirect back to show message
        }




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
            {
                // Debug message to help during development
                TempData["ErrorMessage"] = $"Station not found with ID: {id}";
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

                string checkDuplicate = @"
            SELECT COUNT(*) FROM Stations
            WHERE (LOWER(Name) = LOWER(@Name) OR LOWER(Code) = LOWER(@Code))
            AND Id <> @Id";

                using (var checkCmd = new SqlCommand(checkDuplicate, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    checkCmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    checkCmd.Parameters.AddWithValue("@Id", station.Id);

                    int existingCount = (int)checkCmd.ExecuteScalar();

                    if (existingCount > 0)
                    {
                        TempData["ErrorMessage"] = "⚠️ Station with same name or code already exists!";
                        return View(station);
                    }
                }

                string updateQuery = "UPDATE Stations SET Code = @Code, Name = @Name WHERE Id = @Id";
                using (var cmd = new SqlCommand(updateQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    cmd.Parameters.AddWithValue("@Name", station.Name.Trim());
                    cmd.Parameters.AddWithValue("@Id", station.Id);
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
                var delete = "DELETE FROM Stations WHERE Id = @Id";
                using (var cmd = new SqlCommand(delete, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("StationList");
        }

        // GET: /Admin/EditTrain/5
        [HttpGet]
        public IActionResult EditTrain(int id)
        {
            Train train = null;  // ✅ Declare at the top

            var stations = new List<Station>();
            var classes = new List<TrainClass>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // --- Get train details ---
                var query = "SELECT * FROM Trains WHERE Id = @Id";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
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
                var stationCmd = new SqlCommand("SELECT Id, Name FROM Stations ORDER BY Name", conn);
                using (var reader = stationCmd.ExecuteReader())
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

                // --- Get train classes ---
                var classCmd = new SqlCommand("SELECT Code, Fare, SeatsAvailable FROM TrainClasses WHERE TrainId = @TrainId", conn);
                classCmd.Parameters.AddWithValue("@TrainId", id);
                using (var reader = classCmd.ExecuteReader())
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

            // ✅ Send data to View
            ViewBag.Stations = stations;
            ViewBag.TrainClasses = classes;

            return View(train);  // ✅ Return the train model
        }



        // POST: /Admin/EditTrain
        [HttpPost]
        public IActionResult EditTrain(Train train, List<int> classIds, List<string> classCodes, List<decimal> fares, List<int> seats)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Update Train
                var updateTrain = @"
                    UPDATE Trains
                    SET Number=@Number, Name=@Name, FromStationId=@FromStationId, ToStationId=@ToStationId,
                        Departure=@Departure, Arrival=@Arrival, Duration=@Duration
                    WHERE Id=@Id";

                using (var cmd = new SqlCommand(updateTrain, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", train.Id);
                    cmd.Parameters.AddWithValue("@Number", train.Number);
                    cmd.Parameters.AddWithValue("@Name", train.Name);
                    cmd.Parameters.AddWithValue("@FromStationId", train.FromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", train.ToStationId);
                    cmd.Parameters.AddWithValue("@Departure", train.Departure);
                    cmd.Parameters.AddWithValue("@Arrival", train.Arrival);
                    cmd.Parameters.AddWithValue("@Duration", train.Duration);
                    cmd.ExecuteNonQuery();
                }

                // Update or insert Train Classes
                for (int i = 0; i < classCodes.Count; i++)
                {
                    if (classIds != null && i < classIds.Count && classIds[i] > 0)
                    {
                        var updateClass = @"
                            UPDATE TrainClasses
                            SET Code=@Code, Fare=@Fare, SeatsAvailable=@SeatsAvailable
                            WHERE Id=@Id";

                        using (var cmd = new SqlCommand(updateClass, conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", classIds[i]);
                            cmd.Parameters.AddWithValue("@Code", classCodes[i]);
                            cmd.Parameters.AddWithValue("@Fare", fares[i]);
                            cmd.Parameters.AddWithValue("@SeatsAvailable", seats[i]);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
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
            }

            return RedirectToAction("Index");
        }

        // GET: /Admin/DeleteTrain/5
        public IActionResult DeleteTrain(int id)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Delete classes first (foreign key constraint)
                var deleteClasses = "DELETE FROM TrainClasses WHERE TrainId = @TrainId";
                using (var cmd = new SqlCommand(deleteClasses, conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.ExecuteNonQuery();
                }

                // Then delete train
                var deleteTrain = "DELETE FROM Trains WHERE Id = @Id";
                using (var cmd = new SqlCommand(deleteTrain, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
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
                var cmd = new SqlCommand("SELECT * FROM Trains", conn);
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

            return View(trains);
        }

    }

}