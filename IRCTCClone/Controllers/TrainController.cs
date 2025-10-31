using IRCTCClone.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;

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

                    using (var cmd = new SqlCommand("spSearchStations", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@SearchText", from);

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

                using (var cmd = new SqlCommand("spSearchTrains", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
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

                using (var cmd = new SqlCommand("spGetStations", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Term", term ?? "");

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

                // 1️⃣ Call spSearchTrains
                using (var cmd = new SqlCommand("spSearchTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
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
                                    Code = reader.GetString(4)
                                },
                                ToStationId = reader.GetInt32(5),
                                ToStation = new Station
                                {
                                    Code = reader.GetString(6)
                                },
                                Departure = reader.GetTimeSpan(7),
                                Arrival = reader.GetTimeSpan(8),
                                Duration = reader.GetTimeSpan(9),
                                Classes = new List<TrainClass>()
                            });
                        }
                    }
                }

                // 2️⃣ Load classes for each train using spLoadTrainClasses
                foreach (var train in trains)
                {
                    using (var cmd = new SqlCommand("spLoadTrainClasses", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
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

                using (var cmd = new SqlCommand("spGetTrainDetails", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        train = null;
                        while (reader.Read())
                        {
                            if (train == null)
                            {
                                train = new Train
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                    Number = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                    Name = reader.GetString(reader.GetOrdinal("TrainName")),
                                    FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                    FromStation = new Station
                                    {
                                        Name = reader.GetString(reader.GetOrdinal("FromName")),
                                        Code = reader.GetString(reader.GetOrdinal("FromCode"))
                                    },
                                    ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                    ToStation = new Station
                                    {
                                        Name = reader.GetString(reader.GetOrdinal("ToName")),
                                        Code = reader.GetString(reader.GetOrdinal("ToCode"))
                                    },
                                    Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                    Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                    Duration = reader.GetTimeSpan(reader.GetOrdinal("Duration")),
                                    Classes = new List<TrainClass>()
                                };
                            }

                            // Add class if exists
                            if (!reader.IsDBNull(reader.GetOrdinal("ClassId")))
                            {
                                train.Classes.Add(new TrainClass
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                    TrainId = train.Id,
                                    Code = reader.GetString(reader.GetOrdinal("ClassCode")),
                                    Fare = reader.GetDecimal(reader.GetOrdinal("Fare")),
                                    SeatsAvailable = reader.GetInt32(reader.GetOrdinal("SeatsAvailable"))
                                });
                            }
                        }
                    }
                }
            }

            if (train == null) return NotFound();
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
