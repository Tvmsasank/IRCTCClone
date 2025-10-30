using IRCTCClone.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace IRCTCClone.Controllers
{
    public class TrainController : Controller
    {
        private readonly string _connectionString;

        public TrainController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        /*[HttpGet]
        public JsonResult SearchStations(string term)
        {
            var results = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var query = @"
            SELECT TOP 10 Id, Name, Code
            FROM Stations
            WHERE Name LIKE @term OR Code LIKE @term
            ORDER BY Name";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@term", "%" + term + "%");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new
                            {
                                id = reader.GetInt32(0),
                                name = reader.GetString(1),
                                code = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return Json(results);
        }*/

        [HttpGet]
        public IActionResult Search(string from = "", string to = "", DateTime? date = null)
        {
            if (Request.IsAjaxRequest())
            {
                var stations = new List<object>();
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(
                        @"SELECT TOP 10 Id, Name, Code 
                                  FROM Stations 
                                  WHERE Name LIKE @From OR Code LIKE @From", conn))
                    {
                        cmd.Parameters.AddWithValue("@From", $"%{from}%");
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                stations.Add(new
                                {
                                    Id = reader.GetInt32(0),
                                    Name = reader.GetString(1),
                                    Code = reader.GetString(2)
                                });
                            }
                        }
                    }
                }
                return Json(stations);
            }

            var vm = new { From = from, To = to, Date = date ?? DateTime.Today };
            return PartialView("SearchPartial", vm);
        }

        [HttpPost]
        public IActionResult Search(string from, string to, DateTime date)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    @"SELECT t.Id, t.Number, t.Name, t.FromStationId, t.ToStationId, t.Departure, t.Arrival, t.Duration
                      FROM Trains t
                      INNER JOIN Stations fs ON fs.Id = t.FromStationId
                      INNER JOIN Stations ts ON ts.Id = t.ToStationId
                      WHERE fs.Name = @From AND ts.Name = @To", conn))
                {
                    cmd.Parameters.AddWithValue("@From", from);
                    cmd.Parameters.AddWithValue("@To", to);

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
                                Duration = reader.GetTimeSpan(7)
                            });
                        }
                    }
                }
            }

            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.JourneyDate = date.ToString("yyyy-MM-dd");

            return View("Results", trains);
        }
      
        
        [HttpGet]
        public JsonResult GetStations(string term)
        {
            var stations = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT TOP 50 Id, Code, Name FROM Stations WHERE Name LIKE @term + '%' OR Code LIKE @term + '%' ORDER BY Name";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@term", term ?? "");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new
                            {
                                id = reader.GetInt32(0),
                                code = reader.GetString(1),
                                name = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return Json(stations);
        }


        [HttpPost]
        public IActionResult Results(int fromStationId, int toStationId, DateTime journeyDate)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                @"SELECT t.Id, t.Number, t.Name,
                fs.Id AS FromStationId, fs.Code AS FromStationCode,
                ts.Id AS ToStationId, ts.Code AS ToStationCode,
                t.Departure, t.Arrival, t.Duration
                FROM Trains t
                INNER JOIN Stations fs ON t.FromStationId = fs.Id
                INNER JOIN Stations ts ON t.ToStationId = ts.Id
                WHERE t.FromStationId = @FromStationId AND t.ToStationId = @ToStationId", conn))
                {
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);

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
                                FromStation = new Station
                                {
                                    Code = reader.GetString(4)   // 👈 Code instead of Name
                                },
                                ToStationId = reader.GetInt32(5),
                                ToStation = new Station
                                {
                                    Code = reader.GetString(6)   // 👈 Code instead of Name
                                },
                                Departure = reader.GetTimeSpan(7),
                                Arrival = reader.GetTimeSpan(8),
                                Duration = reader.GetTimeSpan(9),
                                Classes = new List<TrainClass>()
                            });
                        }
                    }
                }


                // Load classes for each train
                foreach (var train in trains)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT Id, TrainId, Code, Fare, SeatsAvailable FROM TrainClasses WHERE TrainId = @TrainId", conn))
                    {
                        cmd.Parameters.AddWithValue("@TrainId", train.Id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                train.Classes.Add(new TrainClass
                                {
                                    Id = reader.GetInt32(0),
                                    TrainId = reader.GetInt32(1),
                                    Code = reader.GetString(2),
                                    Fare = reader.GetDecimal(3),
                                    SeatsAvailable = reader.GetInt32(4)

                                });
                            }
                        }
                    }
                }
            }

            ViewBag.JourneyDate = journeyDate.ToString("yyyy-MM-dd");
            return View(trains);
        }

        public IActionResult Details(int id)
        {
            Train train = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Get train info
                using (var cmd = new SqlCommand(@"
            SELECT 
                t.Id, t.Number, t.Name, 
                t.FromStationId, fs.Name AS FromName, fs.Code AS FromCode,
                t.ToStationId, ts.Name AS ToName, ts.Code AS ToCode,
                t.Departure, t.Arrival, t.Duration
            FROM Trains t
            INNER JOIN Stations fs ON t.FromStationId = fs.Id
            INNER JOIN Stations ts ON t.ToStationId = ts.Id
            WHERE t.Id = @TrainId", conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = reader.GetInt32(0),
                                Number = reader.GetInt32(1),
                                Name = reader.GetString(2),
                                FromStationId = reader.GetInt32(3),
                                FromStation = new Station
                                {
                                    Name = reader.GetString(4),
                                    Code = reader.GetString(5)
                                },
                                ToStationId = reader.GetInt32(6),
                                ToStation = new Station
                                {
                                    Name = reader.GetString(7),
                                    Code = reader.GetString(8)
                                },
                                Departure = reader.GetTimeSpan(9),
                                Arrival = reader.GetTimeSpan(10),
                                Duration = reader.GetTimeSpan(11),
                                Classes = new List<TrainClass>()
                            };
                        }
                        else return NotFound();
                    }
                }


                // ✅ Fetch available classes
                using (var cmd = new SqlCommand(
                    "SELECT Id, TrainId, Code, Fare, SeatsAvailable FROM TrainClasses WHERE TrainId = @TrainId", conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            train.Classes.Add(new TrainClass
                            {
                                Id = reader.GetInt32(0),
                                TrainId = reader.GetInt32(1),
                                Code = reader.GetString(2),
                                Fare = reader.GetDecimal(3),
                                SeatsAvailable = reader.GetInt32(4)
                            });
                        }
                    }
                }
            }

            return View(train);
        }
    }

    public static class HttpRequestExtensions
    {
        public static bool IsAjaxRequest(this Microsoft.AspNetCore.Http.HttpRequest request)
        {
            if (request.Headers != null)
            {
                return request.Headers["X-Requested-With"] == "XMLHttpRequest";
            }
            return false;
        }
    }
}
