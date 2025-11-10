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


        //------------------------------------------search stations--------------------------------------------//


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


        [HttpGet]
        public JsonResult GetStations(string term)
        {
            var stations = Station.GetStationsByTerm(_connectionString, term);
            return Json(stations);
        }


        //----------------------------------------train results----------------------------------------------//


        [HttpPost]
        public IActionResult TrainResults(int fromStationId, int toStationId, DateTime journeyDate)
        {
            var trains = Train.GetTrains(_connectionString, fromStationId);


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

