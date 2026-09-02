using IrctcClone.Helpers;
using IRCTCClone.E_D;
using IRCTCClone.Helpers;
using IRCTCClone.Models;
using IRCTCClone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;

namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    public class TrainController : Controller
    {
        private readonly string _connectionString;
        private readonly IAvailabilityService _availabilityService;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly StationResolverService _stationResolver;

        public TrainController(IConfiguration configuration, IAvailabilityService availabilityService, IMemoryCache cache, StationResolverService stationResolver)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _availabilityService = availabilityService;
            _cache = cache;
            _stationResolver = stationResolver;
        }

        private TimeSpan ParseDuration(string duration)
        {
            var parts = duration.Split(':');
            return new TimeSpan(
                int.Parse(parts[0]),
                int.Parse(parts[1]),
                0
            );
        }

        private string GetStationDisplayName(int stationId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                var cmd = new SqlCommand("spGetStationDisplayNameById", conn);

                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@id", stationId);

                var result = cmd.ExecuteScalar();

                return result != null ? result.ToString() : "";
            }
        }

        private int GetStationIdByCity(string city)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var cmd = new SqlCommand("spGetStationIdByCity", conn);

            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue("@city", city);

            var result = cmd.ExecuteScalar();

            return result != null ? Convert.ToInt32(result) : 0;
        }

        private int ResolveStationDynamic(string input)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                var cmd = new SqlCommand("spSearchStationId", conn);

                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@input", input.ToLower());

                var result = cmd.ExecuteScalar();

                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        private List<Train> ApplySorting(List<Train> trains, string sortBy, DateTime journeyDate)
        {
            if (string.IsNullOrEmpty(sortBy))
                return trains;

            return sortBy switch
            {
                "DEP_EARLY" =>
                    trains.OrderBy(t => t.Departure).ToList(),

                "DEP_LATE" =>
                    trains.OrderByDescending(t => t.Departure).ToList(),

                "ARR_EARLY" =>
                    trains.OrderBy(t =>
                        journeyDate.Date
                            .Add(t.Departure)
                            .Add(ParseDuration(t.Duration)))
                    .ToList(),

                "ARR_LATE" =>
                    trains.OrderByDescending(t =>
                        journeyDate.Date
                            .Add(t.Departure)
                            .Add(ParseDuration(t.Duration)))
                    .ToList(),

                "DUR_SHORT" =>
                    trains.OrderBy(t => ParseDuration(t.Duration)).ToList(),

                "DUR_LONG" =>
                    trains.OrderByDescending(t => ParseDuration(t.Duration)).ToList(),

                _ => trains
            };
        }



        //------------------------------------------search stations--------------------------------------------//

        [EnableRateLimiting("SearchLimiter")]
        [HttpGet]
        public IActionResult Search(string from, string to, DateTime? date = null)
        {
            if (Request.IsAjaxRequest())
            {
                // ✅ Just call the model method
                var stations = Station.SearchStations(_connectionString, from);
                return Json(stations);
            }

            var vm = new { From = from, To = to, Date = date ?? DateTime.Today };
            return PartialView("SearchPartial", vm);
        }


        //-------------------------------------------search trains---------------------------------------------// 


        [HttpPost]
        public IActionResult Search(string from, string to, DateTime date)
        {
            var trains = Train.SearchTrains(_connectionString, from, to);

            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.JourneyDate = date.ToString("yyyy-MM-dd");

            return View("TrainResults", trains);
        }


        //-----------------------------------------get stations-----------------------------------------------//

        [EnableRateLimiting("StationLimiter")]
        [HttpGet]
        public IActionResult GetStations(string term)
        {
            bool isLoggedIn = User.Identity.IsAuthenticated;
            bool isTatkal = TatkalHelper.IsTatkalWindow();

            // CORE IRCTC RULE
            if(isTatkal && !isLoggedIn)
            {
                return Unauthorized();
            }

            //Normal Flow
            var stations = Station.GetStationsByTerm(_connectionString, term);
            return Json(stations.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                code = s.Code,
                city = s.City,
                state = s.State,
                displayName = s.StationDisplayName
            }));
        }


        //----------------------------------------train results----------------------------------------------//

        [HttpGet]
        public IActionResult SetSort(string value)
        {
            HttpContext.Session.SetString("SortBy", value);
            return Ok();
        }

        [HttpPost]
        public IActionResult SmartSearch(string query)
        {
            try
            {
                var parsed = NaturalLanguageParser.Parse(query);

                int fromId = _stationResolver.ResolveStation(parsed.From);
                int toId = _stationResolver.ResolveStation(parsed.To);

                if (fromId == 0 || toId == 0)
                {
                    TempData["Error"] = "Could not detect stations";
                    return RedirectToAction("Index");
                }

                // ✅ GET NAMES FOR UI
                string fromName = GetStationDisplayName(fromId);
                string toName = GetStationDisplayName(toId);

                // ✅ STORE IN TEMPDATA (THIS WAS MISSING)
                TempData["fromStationId"] = fromId;
                TempData["toStationId"] = toId;
                TempData["FromStation"] = fromName;
                TempData["ToStation"] = toName;
                TempData["JourneyDate"] = parsed.JourneyDate.ToString("yyyy-MM-dd");

                return RedirectToAction("TrainResults");

            }

            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index");
            }
        }

/*        [HttpGet]
        public async Task<IActionResult> SmartSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return RedirectToAction("Index", "Home");

            ParsedSearch parsed;

            try
            {
                parsed = await _aiSearchService.ParseQueryAsync(query);
            }
            catch
            {
                // fallback
                parsed = NaturalLanguageParser.Parse(query);
            }

            // 🔥 Convert city → station ID (IMPORTANT)
            int fromId = GetStationIdByCity(parsed.From);
            int toId = GetStationIdByCity(parsed.To);

            return RedirectToAction("TrainResults", new
            {
                fromStationId = fromId,
                toStationId = toId,
                journeyDate = parsed.JourneyDate.ToString("yyyy-MM-dd"),
                classCode = parsed.ClassCode
            });
        }
*/
        [HttpGet]
        public IActionResult TrainResults(string t)
        {
            try
            {
                DateTime GetStationRunDate(Train train, DateTime searchDate)
                {
                    for (int i = 0; i < 14; i++)
                    {
                        DateTime date = searchDate.AddDays(i);

                        bool runs =
                            (date.DayOfWeek == DayOfWeek.Monday && train.RunMon) ||
                            (date.DayOfWeek == DayOfWeek.Tuesday && train.RunTue) ||
                            (date.DayOfWeek == DayOfWeek.Wednesday && train.RunWed) ||
                            (date.DayOfWeek == DayOfWeek.Thursday && train.RunThu) ||
                            (date.DayOfWeek == DayOfWeek.Friday && train.RunFri) ||
                            (date.DayOfWeek == DayOfWeek.Saturday && train.RunSat) ||
                            (date.DayOfWeek == DayOfWeek.Sunday && train.RunSun);

                        if (runs)
                            return date;
                    }

                    return searchDate.AddDays(7);
                }

                // First-time load after POST (TempData exists)
                if (!string.IsNullOrEmpty(t))
                {
                    var decrypted = UrlEncryptionHelper.Decrypt(t);
                    var parts = decrypted.Split('|');

                    int fromStationId = int.Parse(parts[0]);
                    int toStationId = int.Parse(parts[1]);
                    string fromName = parts[2];
                    string toName = parts[3];
                    string dateStr = parts[4];

                    TempData["fromStationId"] = fromStationId;
                    TempData["toStationId"] = toStationId;
                    TempData["FromStation"] = fromName;
                    TempData["ToStation"] = toName;
                    TempData["JourneyDate"] = dateStr;
                }

                // 🔒 SINGLE SOURCE OF TRUTH
                if (TempData.Peek("fromStationId") == null ||
                    TempData.Peek("toStationId") == null)
                {
                    return View(Enumerable.Empty<Train>());
                }

                int searchFromId = (int)TempData.Peek("fromStationId");
                int searchToId = (int)TempData.Peek("toStationId");

                string journeyDateStr =
                    TempData.Peek("JourneyDate")?.ToString()
                    ?? DateTime.Today.ToString("yyyy-MM-dd");

                //string quota = Request.Query["Quota"].ToString();
                //string classCode = Request.Query["classCode"];

                string quota = HttpContext.Session.GetString("Quota") ?? "GENERAL";
                string classCode = HttpContext.Session.GetString("ClassCode") ?? "ALL";

                if (string.IsNullOrEmpty(quota))
                {
                    quota = TempData.Peek("Quota")?.ToString();
                }

                if (string.IsNullOrEmpty(quota))
                {
                    quota = "GENERAL";
                }

                ViewBag.Quota = quota;

                var trains = Train.GetTrains(
                    _connectionString,
                    searchFromId,
                    searchToId,
                    journeyDateStr,
                    quota
                );

                // ✅ APPLY SMART RANKING HERE
                trains = TrainRankingHelper.RankTrains(trains);

                if (!string.IsNullOrEmpty(classCode) && classCode != "ALL")
                {
                    trains = trains
                        .Where(t => t.Classes.Any(c =>
                            c.Code.Contains("(" + classCode + ")")
                        ))
                        .ToList();
                }

                DateTime journeyDate = DateTime.Parse(journeyDateStr);
                var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                    DateTime.UtcNow,
                    "India Standard Time"
                );

                foreach (var train in trains)
                {
                    // Correct boarding date calculation
                    train.NextRunDate = journeyDate;

                    var departureDateTime = train.NextRunDate.Date + train.Departure;

                    train.IsDeparted = departureDateTime <= indiaTime;

                    TimeSpan timeToDeparture = departureDateTime - indiaTime;
                    train.IsChartPrepared = timeToDeparture.TotalHours <= 8;

                    // 🔥 ADD THIS BLOCK
                    bool isCancelled = false;

                    using (var conn = new SqlConnection(_connectionString))
                    {
                        conn.Open();

                        using (var cmd = new SqlCommand("spIsTrainCancelledOnDate", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("@TrainId", train.Id);
                            cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                            isCancelled = (int)cmd.ExecuteScalar() > 0;
                        }

                        // 🔥 STORE IN MODEL (IMPORTANT)
                        train.IsCancelled = isCancelled;
                        //train.HasAlerts = HasTrainAlerts(train.Id, journeyDate);
                    }

                    train.MapUrl = BuildGoogleMapsUrl(train.Id);
                }

                string sortby = HttpContext.Session.GetString("SortBy") ?? "DEP_EARLY";

                //sorting trains
                trains = ApplySorting(trains, sortby, journeyDate);

                ViewBag.CurrentSort = sortby;
                ViewBag.JourneyDate = journeyDateStr;
                ViewBag.ResultCount = trains.Count;
                ViewBag.classCode = classCode;
                ViewBag.IsFilterApplied = !string.IsNullOrEmpty(classCode) && classCode != "ALL";

                TempData.Keep();

                return View(trains);

            }

            catch (Exception ex)
            {
                TempData["Error"] = "Query Resolution Error: " + ex.Message;
                return RedirectToAction("Index", "Home");
            }
        }

        /*--------------------------------------------------------------------------------------------------------------------------------------------------------------------*/

        [EnableRateLimiting("SearchLimiter")]
        [HttpPost]
        public IActionResult TrainResults(
            int fromStationId, 
            int toStationId, 
            DateTime journeyDate, 
            string journeyDateStr, 
            string fromStation, 
            string toStation, 
            string sortby, 
            string quota,
            string classCode,
            string cancelDates
        ) 
        {
            ViewBag.Quota = quota ?? "GENERAL";

            var trains = Train.GetTrains(_connectionString, fromStationId, toStationId, journeyDate.ToString("yyyy-MM-dd"), quota);

            if (!string.IsNullOrEmpty(cancelDates))
            {
                var dates = cancelDates.Split(',');

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    foreach (var train in trains)
                    {
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO TrainCancellationDates (TrainId, CancelDate)
                            VALUES (@TrainId, @Date)", conn))
                        {
                            cmd.Parameters.AddWithValue("@TrainId", train.Id);
                            cmd.Parameters.AddWithValue("@Date", DateTime.Parse(cancelDates));

                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }

            // ✅ APPLY SMART RANKING HERE
            trains = TrainRankingHelper.RankTrains(trains);

            if (string.IsNullOrEmpty(sortby))
            {
                sortby = "DEP_EARLY";
            }

            if (!string.IsNullOrEmpty(classCode) && classCode != "ALL")
            {
                trains = trains
                    .Where(t => t.Classes.Any(c =>
                        c.Code.Contains("(" + classCode + ")")
                    ))
                    .ToList();
            }

            trains = ApplySorting(trains, sortby, journeyDate);

            TempData["FromStation"] = fromStation;
            TempData["ToStation"] = toStation;
            TempData["fromStationId"] = fromStationId;
            TempData["toStationId"] = toStationId;
            TempData["JourneyDate"] = journeyDate;

            // ✅ TRAIN DEPARTED LOGIC (HERE)
            var indiaTimePost = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            bool showConfirmation = false;
            string confirmKeyToSend = null;

            foreach (var train in trains)
            {
                // SQL already validated correct run day
                train.NextRunDate = journeyDate;

                var departureDateTime = train.NextRunDate.Date + train.Departure;

                train.IsDeparted = departureDateTime <= indiaTimePost;
                TimeSpan timeToDeparture = departureDateTime - indiaTimePost;
                train.IsChartPrepared = timeToDeparture.TotalHours <= 8;

                // ================= ROUTE MISMATCH CHECK =================
                bool routeMismatch =
                    !string.Equals(train.FSM, fromStation, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(train.TSM, toStation, StringComparison.OrdinalIgnoreCase);

                string confirmKey = $"route_confirm_{fromStationId}_{toStationId}_{train.Id}";
                bool alreadyConfirmed = HttpContext.Session.GetString(confirmKey) == "1";

                if (routeMismatch && !alreadyConfirmed && !showConfirmation)
                {
                    showConfirmation = true;
                    confirmKeyToSend = confirmKey;
                }

                train.MapUrl = BuildGoogleMapsUrl(train.Id);
                //train.HasAlerts = HasTrainAlerts(train.Id, journeyDate);
            }

            ViewBag.CurrentSort = sortby;
            ViewBag.ResultCount = trains.Count;
            ViewBag.Quota = quota;
            TempData["Quota"] = quota;

            ViewBag.ShowConfirmation = showConfirmation;
            ViewBag.ConfirmKey = confirmKeyToSend;
            ViewBag.classCode = classCode;

            TempData.Keep();

            if (!trains.Any())
            {
                TempData["Error"] = "Trains not available for the searched route / date";
            }

            HttpContext.Session.SetString("Quota", quota ?? "GENERAL");
            HttpContext.Session.SetString("ClassCode", classCode ?? "ALL");


            /*            return RedirectToAction("TrainResults", new
                        {
                            Quota = quota,   // ✅ VERY IMPORTANT
                            classCode = classCode
                        });
            */

            return RedirectToAction("TrainResults");

        }

        /*-------------------------------------------------------------------------------------------------------------------------------------------*/

        [HttpPost]
        public IActionResult ConfirmRoute(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                HttpContext.Session.SetString(key, "1");
            }
            return Ok();
        }


        //-----------------------------------------train details--------------------------------------------//


        [HttpGet]
        public IActionResult TrainDetails(int id)
        {
            var train = Train.GetTrainDetails(_connectionString, id);

            if (train == null)
                return NotFound();

            return View(train);
        }

        public IActionResult Get7DayAvailability(int trainId, int trainClassId)
        {
            List<SevenDayAvailability> result = new List<SevenDayAvailability>();

            using (SqlConnection con = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                using (SqlCommand cmd = new SqlCommand("SP_Get7DayAvailability", con))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainClassId", trainClassId);
                    cmd.Parameters.AddWithValue("@StartDate", DateTime.Now.Date);

                    con.Open();

                    using (SqlDataReader dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            result.Add(new SevenDayAvailability
                            {
                                TravelDate = Convert.ToDateTime(dr["TravelDate"]),
                                TotalSeats = Convert.ToInt32(dr["TotalSeats"]),
                                BookedSeats = Convert.ToInt32(dr["BookedSeats"]),
                                AvailableSeats = Convert.ToInt32(dr["AvailableSeats"]),
                                FarePerDay = Convert.ToDecimal(dr["FarePerDay"])
                            });
                        }
                    }
                }
            }

            return Json(result);
        }


        [HttpGet]
        public IActionResult ClearRouteMismatch()
        {
            // Remove only mismatch flags
            TempData.Remove("RouteMismatch");
            TempData.Remove("ShowConfirmation");
            TempData.Remove("SearchedFrom");
            TempData.Remove("SearchedTo");
            TempData.Remove("ActualFrom");
            TempData.Remove("ActualTo");

            // READ search context
            var fromId = TempData["fromStationId"];
            var toId = TempData["toStationId"];
            var date = TempData["JourneyDate"];
            var fromName = TempData["FromStation"];
            var toName = TempData["ToStation"];

            TempData.Keep(); // IMPORTANT

            return RedirectToAction("TrainResults", new
            {
                fromStationId = fromId,
                toStationId = toId,
                FromStation = fromName,
                ToStation = toName,
                journeyDateStr = date
            });
        }


        public IActionResult ChangeJourneyDate(string date)
        {
            var fromId = TempData.Peek("fromStationId");
            var toId = TempData.Peek("toStationId");
            var fromNm = TempData.Peek("FromStation");
            var toNm = TempData.Peek("ToStation");

            string raw =
                $"{fromId}|{toId}|{fromNm}|{toNm}|{date}";

            string token = UrlEncryptionHelper.Encrypt(raw);

            return RedirectToAction("TrainResults", new { t = token });
        }


        /*[HttpGet]
        public IActionResult GetTrainRoute(int trainId)
        {
            var routes = Train.GetRouteByTrainId(_connectionString, trainId);

            // Return your route partial view
            return View("TrainRouteToUser", routes);
        }*/


        // GOOGLE MAPS INTEGRATION
        public string BuildGoogleMapsUrl(int trainId)
        {
            List<string> stations = new();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainRouteStationNames", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@id", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(reader["Name"].ToString() + " Railway Station India");
                        }
                    }
                }
            }

            if (stations.Count < 2) return "";

            string origin = Uri.EscapeDataString(stations.First());
            string destination = Uri.EscapeDataString(stations.Last());

            string url =
                $"https://www.google.com/maps/dir/{origin}/{destination}/?travelmode=transit&dir_action=navigate";

            return url;
        }

        [HttpGet]
        public IActionResult GetTrainAlerts(int trainId)
        {
            var alerts = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 🔴 CANCELLED
                using (var cmd = new SqlCommand("spGetCancelledDatesByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            alerts.Add(new
                            {
                                type = "Cancelled",
                                message = "Cancelled on " + Convert.ToDateTime(reader["CancelDate"]).ToString("dd MMM")
                            });
                        }
                    }
                }

                // 🟡 SKIPPED STATIONS
                using (var cmd = new SqlCommand("spGetSkippedStationsByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int routeDay = reader["RouteDay"] == DBNull.Value ? 1 : Convert.ToInt32(reader["RouteDay"]);

                            DateTime fromDate = Convert.ToDateTime(reader["FromDate"]);
                            DateTime toDate = Convert.ToDateTime(reader["ToDate"]);

                            // 🔥 CORE FIX
                            DateTime actualFrom = fromDate.AddDays(routeDay - 1);
                            DateTime actualTo = toDate.AddDays(routeDay - 1);

                            alerts.Add(new
                            {
                                type = "Skipped",
                                message = "Skipped station: "
                                          + reader["Name"].ToString()
                                          + " ("
                                          + Convert.ToDateTime(reader["FromDate"]).ToString("dd MMM")
                                          + " - "
                                          + Convert.ToDateTime(reader["ToDate"]).ToString("dd MMM")
                                          + ")"
                            });
                        }
                    }
                }

                // 🔵 DIVERTED
                //    using (var cmd = new SqlCommand(@"
                //SELECT ViaStation 
                //FROM DivertedTrains 
                //WHERE TrainId = @TrainId", conn))
                //    {
                //        cmd.Parameters.AddWithValue("@TrainId", trainId);

                //        using (var reader = cmd.ExecuteReader())
                //        {
                //            while (reader.Read())
                //            {
                //                alerts.Add(new
                //                {
                //                    type = "Diverted",
                //                    message = "Diverted via " + reader["ViaStation"].ToString()
                //                });
                //            }
                //        }
                //    }

                // 🟢 TEMPORARY STOPS
                //    using (var cmd = new SqlCommand(@"
                //SELECT s.Name 
                //FROM TemporaryStops ts
                //INNER JOIN Stations s ON s.Id = ts.StationId
                //WHERE ts.TrainId = @TrainId", conn))
                //    {
                //        cmd.Parameters.AddWithValue("@TrainId", trainId);

                //        using (var reader = cmd.ExecuteReader())
                //        {
                //            while (reader.Read())
                //            {
                //                alerts.Add(new
                //                {
                //                    type = "Temporary Stop",
                //                    message = "Stop added at " + reader["Name"].ToString()
                //                });
                //            }
                //        }
                //    }
                //}

                return Json(alerts);
            }
        }

        private bool HasTrainAlerts(int trainId, DateTime journeyDate)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 🔴 CHECK CANCELLED
                using (var cmd = new SqlCommand("spIsTrainCancelledOnDate", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                    int cancelCount = (int)cmd.ExecuteScalar();

                    if (cancelCount > 0)
                        return true;
                }

                // 🟡 CHECK SKIPPED
                using (var cmd = new SqlCommand("spCheckTrainHasSkippedStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                    int skipCount = (int)cmd.ExecuteScalar();

                    if (skipCount > 0)
                        return true;
                }
            }

            return false;
        }

        //[HttpGet]
        //public IActionResult CheckStationSkipped(int trainId, int fromStationId, int toStationId, DateTime journeyDate)
        //{
        //    bool isSkipped = false;

        //    using (var conn = new SqlConnection(_connectionString))
        //    {
        //        conn.Open();

        //        using (var cmd = new SqlCommand("spCheckStationSkipped", conn))
        //        {
        //            cmd.CommandType = CommandType.StoredProcedure;

        //            cmd.Parameters.AddWithValue("@TrainId", trainId);
        //            cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
        //            cmd.Parameters.AddWithValue("@ToStationId", toStationId);
        //            cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

        //            isSkipped = Convert.ToBoolean(cmd.ExecuteScalar());
        //        }
        //    }

        //    return Json(new { isSkipped });
        //}
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

