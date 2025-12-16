using IRCTCClone.Models;
using IRCTCClone.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;

namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    public class TrainController : Controller
    {
        private readonly string _connectionString;
        private readonly IAvailabilityService _availabilityService;
        private readonly IConfiguration _configuration;

        public TrainController(IConfiguration configuration, IAvailabilityService availabilityService)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _availabilityService = availabilityService;
        }


        //------------------------------------------search stations--------------------------------------------//

        [EnableRateLimiting("SearchLimiter")]
        [HttpGet]
        public IActionResult Search(string from = "", string to = "", DateTime? date = null)
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
        public JsonResult GetStations(string term)
        {
            var stations = Station.GetStationsByTerm(_connectionString, term);
            return Json(stations);
        }


        //----------------------------------------train results----------------------------------------------//

        [HttpGet]
        public IActionResult TrainResults(int? fromStationId, int? toStationId, string? journeyDateStr)
        {
            if (fromStationId == null || toStationId == null)
                return View(); // blank

            // Parse date
            DateTime journeyDate;
            if (!DateTime.TryParse(journeyDateStr, out journeyDate))
                journeyDate = DateTime.Today;

            var trains = Train.GetTrains(_connectionString, fromStationId.Value, toStationId.Value, journeyDate.ToString("yyyy-MM-dd"));

            ViewBag.JourneyDate = journeyDate.ToString("yyyy-MM-dd");
            return View(trains);
        }


        [EnableRateLimiting("SearchLimiter")]

        [HttpPost]
        public IActionResult TrainResults(int fromStationId, int toStationId, DateTime journeyDate, string journeyDateStr)
        {

            if (!DateTime.TryParse(journeyDateStr, out journeyDate))
            {
                journeyDate = DateTime.Today;
            }

            var trains = Train.GetTrains(_connectionString, fromStationId, toStationId, journeyDate.ToString("yyyy-MM-dd"));

            ViewBag.JourneyDate = journeyDate.ToString("yyyy-MM-dd");
            return View(trains);
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


        /*[HttpGet]
        public IActionResult GetTrainRoute(int trainId)
        {
            var routes = Train.GetRouteByTrainId(_connectionString, trainId);

            // Return your route partial view
            return View("TrainRouteToUser", routes);
        }*/

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

