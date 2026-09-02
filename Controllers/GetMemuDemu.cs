using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace IrctcClone.Controllers
{
    public class GetMemuDemuController : Controller
    {
        private readonly string _connectionString;

        public GetMemuDemuController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public IActionResult GetMD(int fromId, int toId, DateTime journeyDate, string trainType = null)
        {
            try
            {
                var list = new List<object>();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spSearchTrainsCluster_MDM", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@FromStationId", fromId);
                        cmd.Parameters.AddWithValue("@ToStationId", toId);
                        cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                        // ✅ IMPORTANT FIX
                        cmd.Parameters.AddWithValue("@TrainType",
                            string.IsNullOrEmpty(trainType) ? (object)DBNull.Value : trainType);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new
                                {
                                    id = reader["Id"],
                                    number = reader["Number"],
                                    name = reader["Name"],
                                    trainType = reader["TrainType"],
                                    runMon = reader["RunMon"],
                                    runTue = reader["RunTue"],
                                    runWed = reader["RunWed"],
                                    runThu = reader["RunThu"],
                                    runFri = reader["RunFri"],
                                    runSat = reader["RunSat"],
                                    runSun = reader["RunSun"],
                                    departure = reader["FromDeparture"],
                                    arrival = reader["ToArrival"],
                                    duration = reader["Duration"]
                                });
                            }
                        }
                    }
                }

                return Json(list);
            }

            catch (Exception ex)
            {
                return Json(new { success = false, message = "Could not fetch MEMU details: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetDemu(int fromId, int toId)
        {
            var list = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spSearchTrainsCluster", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@FromStationId", fromId);
                    cmd.Parameters.AddWithValue("@ToStationId", toId);
                    cmd.Parameters.AddWithValue("@JourneyDate", DateTime.Today);
                    cmd.Parameters.AddWithValue("@TrainType", "DEMU");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                id = reader["Id"],
                                number = reader["Number"],
                                name = reader["Name"],
                                trainType = reader["TrainType"],
                                departure = reader["FromDeparture"],
                                arrival = reader["ToArrival"],
                                duration = reader["Duration"]
                            });
                        }
                    }
                }
            }

            return Json(list);
        }

        [HttpPost]
        public IActionResult CreateMemuDemu(
        int number,
        string name,
        int fromStationId,
        int toStationId,
        TimeSpan departure,
        TimeSpan arrival,
        string duration,
        bool RunMon,
        bool RunTue,
        bool RunWed,
        bool RunThu,
        bool RunFri,
        bool RunSat,
        bool RunSun,
        string trainType,
        DateTime? ServiceStartDate,
        DateTime? ServiceEndDate
        )
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spInsertMemuDemuTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@Number", number);
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);
                    cmd.Parameters.AddWithValue("@Departure", departure);
                    cmd.Parameters.AddWithValue("@Arrival", arrival);
                    cmd.Parameters.AddWithValue("@Duration", duration);
                    cmd.Parameters.AddWithValue("@RunMon", RunMon);
                    cmd.Parameters.AddWithValue("@RunTue", RunTue);
                    cmd.Parameters.AddWithValue("@RunWed", RunWed);
                    cmd.Parameters.AddWithValue("@RunThu", RunThu);
                    cmd.Parameters.AddWithValue("@RunFri", RunFri);
                    cmd.Parameters.AddWithValue("@RunSat", RunSat);
                    cmd.Parameters.AddWithValue("@RunSun", RunSun);
                    cmd.Parameters.AddWithValue("@TrainType", trainType);
                    cmd.Parameters.AddWithValue("@ServiceStartDate", (object?)ServiceStartDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ServiceEndDate", (object?)ServiceEndDate ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }

            TempData["SuccessMessage"] = "✅ MEMU/DEMU/MMTS Train Created!";
            return RedirectToAction("CreateTrain", "Admin");
        }

        [HttpGet]
        public IActionResult GetSchedule(int trainId, string trainType)
        {
            var list = new List<object>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainRoutesByTrainId", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainType", string.IsNullOrEmpty(trainType) ? (object)DBNull.Value : trainType);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new
                            {
                                stationName = reader["StationName"].ToString(),
                                stationCode = reader["StationCode"].ToString(),

                                arrivalTime = reader["ArrivalTime"] == DBNull.Value
                                    ? null
                                    : ((TimeSpan)reader["ArrivalTime"]).ToString(@"hh\:mm\:ss") + ".00",

                                departureTime = reader["DepartureTime"] == DBNull.Value
                                    ? null
                                    : ((TimeSpan)reader["DepartureTime"]).ToString(@"hh\:mm\:ss") + ".00",
                                distance = reader["DistanceFromSource"],
                                day = reader["Day"]
                            });
                        }
                    }
                }
            }

            return Json(list);
        }
    }
}
