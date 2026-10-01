using IrctcClone.Helpers;
using IRCTCClone.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace IrctcClone.Controllers
{
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("DefaultPolicy")]
    public class AdminController : Controller
    {
        private readonly string _connectionString;

        public AdminController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        /*        public class NoCacheMiddleware
                {
                    private readonly RequestDelegate _next;

                    public NoCacheMiddleware(RequestDelegate next)
                    {
                        _next = next;
                    }

                    public async Task Invoke(HttpContext context)
                    {
                        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                        context.Response.Headers["Pragma"] = "no-cache";
                        context.Response.Headers["Expires"] = "0";

                        await _next(context);
                    }
                }*/

        //------------------------------------------ADMIN LOGIN-------------------------------------------//

        /*        [HttpGet]
                [Authorize(Roles = "Admin")]
                public IActionResult KeepAlive()
                {
                    return Ok();
                }*/

        [AllowAnonymous]
        [HttpGet]
        public IActionResult AdminLogin()
        {
            if (HttpContext.Session.GetString("AdminUser") != null)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }


        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> AdminLogin(string username, string password, string captchaInput)
        {
            string sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");

            bool isCaptchaValid =
                !string.IsNullOrWhiteSpace(sessionCaptcha) &&
                !string.IsNullOrWhiteSpace(captchaInput) &&
                captchaInput.Trim().Equals(sessionCaptcha, StringComparison.Ordinal);

            // ❌ CAPTCHA WRONG
            if (!isCaptchaValid)
            {
                await HttpContext.SignOutAsync();              // 🔥 ADD THIS
                HttpContext.Session.Clear();                   // 🔥 ADD THIS

                ViewBag.Error = "Invalid captcha";
                return View();
            }

            // remove captcha after correct validation
            HttpContext.Session.Remove("CAPTCHA");

            bool isLoginValid = Admin.ValidateLogin(_connectionString, username, password);

            // ❌ BOTH WRONG
            if (!isCaptchaValid && !isLoginValid)
            {
                await HttpContext.SignOutAsync();              // 🔥 ADD THIS
                HttpContext.Session.Clear();                   // 🔥 ADD THIS

                ViewBag.Error = "Invalid credentials and captcha";
                return View();
            }

            // ❌ LOGIN WRONG
            if (!isLoginValid)
            {
                await HttpContext.SignOutAsync();              // 🔥 ADD THIS
                HttpContext.Session.Clear();                   // 🔥 ADD THIS

                ViewBag.Error = "Invalid username or password";
                return View();
            }

            // ✅ SUCCESS
            HttpContext.Session.SetString("AdminUser", username);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "Admin")   // Key Line
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            return RedirectToAction("Dashboard");
        }

        //------------------------------------------ADMIN LOGOUT------------------------------------------//

        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync();
            return RedirectToAction("AdminLogin");
        }

        //--------------------------------------------DASHBOARD-------------------------------------------//
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [Authorize(Roles = "Admin")]
        public IActionResult Dashboard()
        {
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

            var mdtrains = MDTrains.GetAllTypes(_connectionString);
            ViewBag.mdtrains = mdtrains;
            return View();
        }


        // Create Train (POST)
        [HttpPost]
        public IActionResult CreateTrain(
            Train train,
            List<string> classCodes,
            List<string> BookingCode,
            List<int> seats,
            string CoachPositions)
        {
            // 1️⃣ Validate class inputs
            if (classCodes == null || BookingCode == null || seats == null || classCodes.Count != BookingCode.Count || classCodes.Count != seats.Count)
            {
                TempData["ErrorMessage"] = "Please provide valid class details!";
                return RedirectToAction("CreateTrain");
            }

            // 2️⃣ Check for duplicates
            if (Train.CheckDuplicate(_connectionString, train.Number))
            {
                TempData["ErrorMessage"] = "⚠️ A train with the same number or route already exists!";
                return RedirectToAction("CreateTrain");
            }

            // 3️⃣ Insert Train and get ID
            train.InsertTrain(_connectionString); // train.Id will be populated inside the object

            if (train.Id == 0)
            {
                TempData["ErrorMessage"] = "❌ Failed to create train!";
                return RedirectToAction("AddRoute", new { trainId = train.Id });
            }

            // 4️⃣ Insert Train Classes safely
            for (int i = 0; i < classCodes.Count; i++)
            {
                // Skip empty inputs
                if (string.IsNullOrWhiteSpace(classCodes[i]) || seats[i] < 0)
                    continue;

                var trainClass = new TrainClass
                {
                    TrainId = train.Id,
                    Code = classCodes[i].Trim(),
                    SeatPrefix = !string.IsNullOrWhiteSpace(BookingCode[i]) ? BookingCode[i].Trim().ToUpper() : null,
                    SeatsAvailable = seats[i]
                };
                trainClass.Insert(_connectionString);
            }

            // 5️⃣ Save CoachPositions and dynamically generate Coaches and Seat layouts!
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // A. Save the CoachPositions lineup into the Trains table column we created
                using (var cmd = new SqlCommand("spUpdateCoachPositions", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@Positions", (object)CoachPositions ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@TrainId", train.Id);

                    cmd.ExecuteNonQuery();
                }

                // B. Execute the dynamic Coach composition parser in a high-speed Transaction
                if (!string.IsNullOrWhiteSpace(CoachPositions))
                {
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // Call the parser helper method!
                            ParseCompositionAndGenerateSeats(conn, transaction, train.Id, CoachPositions);
                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            TempData["ErrorMessage"] = $"❌ Train created but failed to generate coach layouts: {ex.Message}";
                            return RedirectToAction("CreateTrain");
                        }
                    }
                }
            }

            TempData["SuccessMessage"] = "✅ Train, classes, and coach layouts created successfully!";

            // Redirect to the route builder or list (adjust Action name if needed, e.g. "AddRoute" or "TrainList")
            return RedirectToAction("CreateTrain", new { trainId = train.Id });
        }


        private List<TrainRoute> GetTrainRoutes(int trainId, string trainType)
        {
            var routes = new List<TrainRoute>();

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
                            routes.Add(new TrainRoute
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                TrainId = trainId,
                                StationId = Convert.ToInt32(reader["StationId"]),
                                StationName = Convert.ToString(reader["StationName"]),
                                StopNumber = Convert.ToInt32(reader["StopNumber"]),
                                ArrivalTime = reader["ArrivalTime"] == DBNull.Value ? null : (TimeSpan?)reader["ArrivalTime"],
                                DepartureTime = reader["DepartureTime"] == DBNull.Value ? null : (TimeSpan?)reader["DepartureTime"],
                                Day = reader["Day"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Day"]),
                                Distance = reader["DistanceFromSource"] == DBNull.Value ? 0 : Convert.ToInt32(reader["DistanceFromSource"]),
                            });
                        }
                    }
                }
            }

            return routes;
        }

        [HttpGet]
        public IActionResult AddRoute(int id, string trainType)
        {
            // ✅ 1. Load the Train using the ID
            Train train = GetTrainById(id, trainType);   // we will write this function below
            if (train != null && string.IsNullOrEmpty(train.CoachPositions))
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cpCmd = new SqlCommand("SELECT CoachPositions FROM Trains WHERE Id = @TrainId", conn))
                    {
                        cpCmd.Parameters.AddWithValue("@TrainId", id);
                        var cpObj = cpCmd.ExecuteScalar();
                        if (cpObj != null && cpObj != DBNull.Value)
                        {
                            train.CoachPositions = cpObj.ToString();
                        }
                    }
                }
            }
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

            var cancelDates = new List<string>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainCancellationDates", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", train.Id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cancelDates.Add(Convert.ToDateTime(reader["CancelDate"]).ToString("yyyy-MM-dd"));
                        }
                    }
                }
            }

            var skipDates = new List<string>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainSkipDates", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            skipDates.Add(Convert.ToDateTime(reader["FromDate"]).ToString("yyyy-MM-dd"));
                        }
                    }
                }
            }

            var tempRoutes = new List<dynamic>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainTempRoutes", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tempRoutes.Add(new
                            {
                                StationId = Convert.ToInt32(reader["StationId"]),
                                FromDate = Convert.ToDateTime(reader["FromDate"]),
                                ToDate = Convert.ToDateTime(reader["ToDate"])
                            });
                        }
                    }
                }
            }

            foreach (var temp in tempRoutes)
            {
                DateTime fDate = temp.FromDate;
                DateTime tDate = temp.ToDate;
                for (var d = fDate.Date; d <= tDate.Date; d = d.AddDays(1))
                {
                    string dateStr = d.ToString("yyyy-MM-dd");
                    if (!skipDates.Contains(dateStr))
                    {
                        skipDates.Add(dateStr);
                    }
                }
            }

            ViewBag.SkipDates = string.Join(", ", skipDates.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());
            ViewBag.CancelDates = string.Join(", ", cancelDates.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());

            ViewBag.Stations = stations;

            // ✅ 3. Load Train Routes
            var routes = GetTrainRoutes(id, train.TrainType);

            // ✅ APPLY TEMP DATA TO ROUTES
            foreach (var route in routes)
            {
                var temp = tempRoutes
                    .FirstOrDefault(t => t.StationId == route.StationId);

                if (temp != null)
                {
                    route.IsSkipped = true;
                    route.FromDate = temp.FromDate;
                    route.ToDate = temp.ToDate;
                }
            }

            if (tempRoutes.Any())
            {
                ViewBag.FromDate = tempRoutes.First().FromDate.ToString("yyyy-MM-dd");
                ViewBag.ToDate = tempRoutes.First().ToDate.ToString("yyyy-MM-dd");
            }

            ViewBag.Routes = routes;
            bool isCancelled = false;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spCheckUpcomingTrainCancellation", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", id);
                    int count = (int)cmd.ExecuteScalar();

                    isCancelled = count > 0;
                }
            }

            ViewBag.IsCancelled = isCancelled;

            // ✅ Load Temporary Stops for this train
            var temporaryStops = new List<TrainTemporaryStop>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    EnsureTemporaryStopsSchema(conn);
                    using (var cmd = new SqlCommand("spGetTemporaryStopsByTrainId", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int dist = 0;
                                try
                                {
                                    int distOrd = reader.GetOrdinal("Distance");
                                    if (distOrd >= 0 && !reader.IsDBNull(distOrd))
                                    {
                                        dist = Convert.ToInt32(reader.GetValue(distOrd));
                                    }
                                }
                                catch { }

                                int stopNum = 0;
                                try
                                {
                                    int stopOrd = reader.GetOrdinal("StopNumber");
                                    if (stopOrd >= 0 && !reader.IsDBNull(stopOrd))
                                    {
                                        stopNum = Convert.ToInt32(reader.GetValue(stopOrd));
                                    }
                                }
                                catch { }

                                int afterId = 0;
                                try
                                {
                                    int afterOrd = reader.GetOrdinal("AfterStationId");
                                    if (afterOrd >= 0 && !reader.IsDBNull(afterOrd))
                                    {
                                        afterId = Convert.ToInt32(reader.GetValue(afterOrd));
                                    }
                                }
                                catch { }

                                temporaryStops.Add(new TrainTemporaryStop
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    TrainId = Convert.ToInt32(reader["TrainId"]),
                                    StationId = Convert.ToInt32(reader["StationId"]),
                                    StationName = reader["StationName"].ToString(),
                                    StationCode = reader["StationCode"].ToString(),
                                    ArrivalTime = reader["ArrivalTime"] == DBNull.Value ? null : (TimeSpan?)reader["ArrivalTime"],
                                    DepartureTime = reader["DepartureTime"] == DBNull.Value ? null : (TimeSpan?)reader["DepartureTime"],
                                    RouteDay = Convert.ToInt32(reader["RouteDay"]),
                                    Distance = dist,
                                    StopNumber = stopNum,
                                    AfterStationId = afterId,
                                    FromDate = Convert.ToDateTime(reader["FromDate"]),
                                    ToDate = Convert.ToDateTime(reader["ToDate"]),
                                    Reason = reader["Reason"]?.ToString() ?? "Festival Season"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            var allTempDates = new List<string>();
            var tempStationIds = new HashSet<int>();
            foreach (var ts in temporaryStops)
            {
                tempStationIds.Add(ts.StationId);
                for (var d = ts.FromDate.Date; d <= ts.ToDate.Date; d = d.AddDays(1))
                {
                    allTempDates.Add(d.ToString("yyyy-MM-dd"));
                }
            }

            ViewBag.TempDates = string.Join(", ", allTempDates.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());
            ViewBag.TemporaryStops = temporaryStops;

            foreach (var route in routes)
            {
                if (tempStationIds.Contains(route.StationId))
                {
                    route.IsTemporary = true;
                    var matchingTs = temporaryStops.FirstOrDefault(t => t.StationId == route.StationId);
                    if (matchingTs != null)
                    {
                        if (matchingTs.Distance > 0 && route.Distance == 0)
                            route.Distance = matchingTs.Distance;
                        if (matchingTs.StopNumber > 0)
                            route.StopNumber = matchingTs.StopNumber;
                    }
                }
            }

            // Insert temporary stoppage station immediately after its preceding station (AfterStationId)
            foreach (var ts in temporaryStops)
            {
                if (!routes.Any(r => r.StationId == ts.StationId))
                {
                    var tempRoute = new TrainRoute
                    {
                        TrainId = id,
                        StationId = ts.StationId,
                        ArrivalTime = ts.ArrivalTime,
                        DepartureTime = ts.DepartureTime,
                        Distance = ts.Distance,
                        Day = ts.RouteDay,
                        StopNumber = ts.StopNumber > 0 ? ts.StopNumber : 0,
                        IsTemporary = true,
                        Station = new Station { Id = ts.StationId, Name = ts.StationName, Code = ts.StationCode }
                    };

                    int insertIndex = -1;
                    if (ts.AfterStationId > 0)
                    {
                        insertIndex = routes.FindIndex(r => r.StationId == ts.AfterStationId);
                    }

                    if (insertIndex >= 0)
                    {
                        routes.Insert(insertIndex + 1, tempRoute);
                    }
                    else if (ts.StopNumber > 0 && ts.StopNumber <= routes.Count)
                    {
                        routes.Insert(ts.StopNumber - 1, tempRoute);
                    }
                    else
                    {
                        routes.Add(tempRoute);
                    }
                }
            }

            // 🔥 RENUMBER STRICTLY SEQUENTIALLY: 1, 2, 3...
            for (int i = 0; i < routes.Count; i++)
            {
                routes[i].StopNumber = i + 1;
            }

            // ✅ 4. Return view with routes list
            return View("AddRoute", routes);
        }

        private void EnsureTemporaryStopsSchema(SqlConnection conn)
        {
            try
            {
                string schemaSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TrainTemporaryStops')
                BEGIN
                    CREATE TABLE TrainTemporaryStops (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        TrainId INT NOT NULL,
                        StationId INT NOT NULL,
                        ArrivalTime TIME NULL,
                        DepartureTime TIME NULL,
                        RouteDay INT NOT NULL DEFAULT 1,
                        Distance INT NOT NULL DEFAULT 0,
                        StopNumber INT NOT NULL DEFAULT 0,
                        AfterStationId INT NOT NULL DEFAULT 0,
                        FromDate DATE NOT NULL,
                        ToDate DATE NOT NULL,
                        Reason NVARCHAR(250) NULL DEFAULT 'Festival Season',
                        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
                    );
                    CREATE INDEX IX_TTS_TrainId_Dates ON TrainTemporaryStops(TrainId, FromDate, ToDate);
                END
                ELSE
                BEGIN
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('TrainTemporaryStops') AND name = 'Distance')
                    BEGIN
                        ALTER TABLE TrainTemporaryStops ADD Distance INT NOT NULL DEFAULT 0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('TrainTemporaryStops') AND name = 'StopNumber')
                    BEGIN
                        ALTER TABLE TrainTemporaryStops ADD StopNumber INT NOT NULL DEFAULT 0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('TrainTemporaryStops') AND name = 'AfterStationId')
                    BEGIN
                        ALTER TABLE TrainTemporaryStops ADD AfterStationId INT NOT NULL DEFAULT 0;
                    END
                END";

                using (var cmd = new SqlCommand(schemaSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                // One-time update for existing Hitech City stops inserted after Lingampalli (StationId 2)
                try
                {
                    using (var migCmd = new SqlCommand("UPDATE TrainTemporaryStops SET AfterStationId = 2 WHERE StationId = 4 AND (AfterStationId IS NULL OR AfterStationId = 0)", conn))
                    {
                        migCmd.ExecuteNonQuery();
                    }
                }
                catch { }

                string procsSql = @"
                CREATE OR ALTER PROCEDURE spGetTemporaryStopsByTrainId
                    @TrainId INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT 
                        ts.Id,
                        ts.TrainId,
                        ts.StationId,
                        s.Name AS StationName,
                        s.Code AS StationCode,
                        ts.ArrivalTime,
                        ts.DepartureTime,
                        ts.RouteDay,
                        ts.Distance,
                        ts.StopNumber,
                        ts.AfterStationId,
                        ts.FromDate,
                        ts.ToDate,
                        ts.Reason
                    FROM TrainTemporaryStops ts
                    INNER JOIN Stations s ON ts.StationId = s.Id
                    WHERE ts.TrainId = @TrainId
                    ORDER BY ts.StopNumber, ts.RouteDay, ts.ArrivalTime;
                END";

                using (var cmd = new SqlCommand(procsSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                string insProcSql = @"
                CREATE OR ALTER PROCEDURE spInsertTemporaryStop
                    @TrainId INT,
                    @StationId INT,
                    @ArrivalTime TIME = NULL,
                    @DepartureTime TIME = NULL,
                    @RouteDay INT = 1,
                    @Distance INT = 0,
                    @StopNumber INT = 0,
                    @AfterStationId INT = 0,
                    @FromDate DATE,
                    @ToDate DATE,
                    @Reason NVARCHAR(250) = 'Festival Season'
                AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT INTO TrainTemporaryStops (TrainId, StationId, ArrivalTime, DepartureTime, RouteDay, Distance, StopNumber, AfterStationId, FromDate, ToDate, Reason)
                    VALUES (@TrainId, @StationId, @ArrivalTime, @DepartureTime, @RouteDay, @Distance, @StopNumber, @AfterStationId, @FromDate, @ToDate, @Reason);
                END";

                using (var cmd = new SqlCommand(insProcSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                string delProcSql = @"
                CREATE OR ALTER PROCEDURE spDeleteTemporaryStopsByTrainId
                    @TrainId INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DELETE FROM TrainTemporaryStops WHERE TrainId = @TrainId;
                END";

                using (var cmd = new SqlCommand(delProcSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private Train GetTrainById(int trainId, string trainType)
        {
            Train train = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                bool isMemu = !string.IsNullOrEmpty(trainType) &&
                              (trainType.ToUpper() == "MEMU" || trainType.ToUpper() == "DEMU" || trainType.ToUpper() == "MMTS");

                // 🟡 MEMU / DEMU
                if (isMemu)
                {
                    using (var cmd = new SqlCommand("spGetMemuDemuTrainById", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@Id", trainId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                train = new Train
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Number = Convert.ToInt32(reader["TrainNumber"]),
                                    Name = reader["TrainName"].ToString(),
                                    TrainType = reader["TrainType"].ToString(),
                                    FromStation = new Station { Name = reader["FromName"].ToString() },
                                    ToStation = new Station { Name = reader["ToName"].ToString() },
                                    Departure = (TimeSpan)reader["DepartureTime"],
                                    Arrival = (TimeSpan)reader["ArrivalTime"],
                                    Duration = reader["Duration"].ToString(),
                                    Day = 0
                                };
                            }
                        }
                    }
                }
                // 🔵 NORMAL
                else
                {
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
                                    Duration = reader.GetString(7),
                                    Day = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                                };
                            }
                        }
                    }
                }
            }

            return train;
        }


        // ✅ POST: Add Route
        [HttpPost]
        public IActionResult AddRoute(int trainId, List<TrainRoute> routes, string deletedIds, List<TrainTemporaryStop>? tempStops = null)
        {
            bool cancelTrain = Request.Form["cancelTrain"] == "on";
            string skipDates = Request.Form["skipDates"];
            string cancelDates = Request.Form["cancelDates"];
            string trainType = "";

            // 👇 Debug check (optional)
            if (trainId == 0)
            {
                TempData["ErrorMessage"] = "Train ID missing! Cannot add routes.";
                return RedirectToAction("TrainList");
            }

            // ✅ PARSE DATES
            var skipDateList = new List<DateTime>();
            var cancelDateList = new List<DateTime>();

            if (!string.IsNullOrEmpty(skipDates))
            {
                skipDateList = skipDates
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => DateTime.Parse(d.Trim()))
                    .Distinct()
                    .ToList();
            }

            if (!string.IsNullOrEmpty(cancelDates))
            {
                cancelDateList = cancelDates
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => DateTime.Parse(d.Trim()))
                    .Distinct()
                    .ToList();
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1) Fetch current routes from DB using stored procedure to detect any deleted or excess stops
                using (var cmd = new SqlCommand("spGetTrainTypeById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Id", trainId);
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                        trainType = result.ToString();
                }

                bool isMemuDemu = trainType == "MEMU" || trainType == "DEMU" || trainType == "MMTS";

                var baseRoutes = routes.Where(r => !r.IsTemporary).ToList();

                // Check existing DB routes for this train
                var currentDbRoutes = GetTrainRoutes(trainId, trainType);
                var submittedIds = baseRoutes.Where(r => r.Id > 0).Select(r => r.Id).ToHashSet();

                var idsToDelete = new List<string>();

                // Any existing DB stops that were removed by the admin
                foreach (var dbRoute in currentDbRoutes)
                {
                    if (!submittedIds.Contains(dbRoute.Id) || dbRoute.StopNumber > baseRoutes.Count)
                    {
                        idsToDelete.Add(dbRoute.Id.ToString());
                    }
                }

                // Explicit deletedIds from client-side
                if (!string.IsNullOrWhiteSpace(deletedIds))
                {
                    var idList = deletedIds.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(s => s.Trim())
                                           .Where(s => int.TryParse(s, out _));
                    idsToDelete.AddRange(idList);
                }

                idsToDelete = idsToDelete.Distinct().ToList();

                if (idsToDelete.Any())
                {
                    using (var delCmd = new SqlCommand("spDeleteTrainRoute", conn))
                    {
                        delCmd.CommandType = CommandType.StoredProcedure;
                        delCmd.Parameters.AddWithValue("@Ids", string.Join(",", idsToDelete));
                        delCmd.ExecuteNonQuery();
                    }
                }

                // 2) Save / Upsert base routes using user's stored procedure
                int baseStopNumber = 1;
                foreach (var route in baseRoutes)
                {
                    using (var cmd = new SqlCommand(isMemuDemu ? "spInsertMemuDemuRoute" : "spInsertTrainRoute", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@StationId", route.StationId);
                        cmd.Parameters.AddWithValue("@StopNumber", baseStopNumber++);
                        cmd.Parameters.AddWithValue("@ArrivalTime", (object?)route.ArrivalTime ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DepartureTime", (object?)route.DepartureTime ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DistanceFromSource", (object?)route.Distance ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Day", route.Day > 0 ? route.Day : 1);
                        cmd.ExecuteNonQuery();
                    }
                }

                // If any excess stops remained in DB (e.g. if the train previously had 17 stops and now has 16),
                // delete them cleanly using stored procedures
                var updatedDbRoutes = GetTrainRoutes(trainId, trainType);
                var excessIds = updatedDbRoutes.Where(r => r.StopNumber > baseRoutes.Count)
                                               .Select(r => r.Id.ToString())
                                               .ToList();
                if (excessIds.Any())
                {
                    using (var cleanCmd = new SqlCommand("spDeleteTrainRoute", conn))
                    {
                        cleanCmd.CommandType = CommandType.StoredProcedure;
                        cleanCmd.Parameters.AddWithValue("@Ids", string.Join(",", excessIds));
                        cleanCmd.ExecuteNonQuery();
                    }
                }

                // 3) clear old temp entries for same range (clean update)
                using (var delTemp = new SqlCommand("spDeleteTrainRouteTemp", conn))
                {
                    delTemp.CommandType = CommandType.StoredProcedure;

                    delTemp.Parameters.AddWithValue("@TrainId", trainId);

                    delTemp.ExecuteNonQuery();
                }

                // 4) INSERT ONLY SKIPPED STATIONS (MERGING CONSECUTIVE DATES INTO RANGES)
                var skipDateRanges = new List<(DateTime From, DateTime To)>();
                var sortedSkipDates = skipDateList.OrderBy(d => d.Date).Distinct().ToList();
                if (sortedSkipDates.Any())
                {
                    DateTime rangeStart = sortedSkipDates[0];
                    DateTime rangeEnd = sortedSkipDates[0];

                    for (int i = 1; i < sortedSkipDates.Count; i++)
                    {
                        if (sortedSkipDates[i].Date == rangeEnd.AddDays(1).Date)
                        {
                            rangeEnd = sortedSkipDates[i];
                        }
                        else
                        {
                            skipDateRanges.Add((rangeStart, rangeEnd));
                            rangeStart = sortedSkipDates[i];
                            rangeEnd = sortedSkipDates[i];
                        }
                    }
                    skipDateRanges.Add((rangeStart, rangeEnd));
                }

                foreach (var route in routes)
                {
                    if (route.IsSkipped && skipDateRanges.Any())
                    {
                        foreach (var range in skipDateRanges)
                        {
                            using (var cmd = new SqlCommand("spInsertTrainRouteTemp", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;

                                cmd.Parameters.AddWithValue("@TrainId", trainId);
                                cmd.Parameters.AddWithValue("@StationId", route.StationId);

                                cmd.Parameters.AddWithValue("@FromDate", range.From);
                                cmd.Parameters.AddWithValue("@ToDate", range.To);

                                // 🔥 THIS IS THE FIX (WITHOUT THIS NOTHING WORKS)
                                cmd.Parameters.AddWithValue("@RouteDay", route.Day);

                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 🔥 CLEAR OLD CANCEL DATES FIRST
                using (var del = new SqlCommand("spDeleteTrainCancellationDates", conn))
                {
                    del.CommandType = CommandType.StoredProcedure;

                    del.Parameters.AddWithValue("@TrainId", trainId);

                    del.ExecuteNonQuery();
                }

                // 5️⃣ INSERT TRAIN CANCELLATION (MULTIPLE DATES)
                if (cancelDateList.Any())
                {
                    // delete only if new data exists
                    using (var del = new SqlCommand("spDeleteTrainCancellationDates", conn))
                    {
                        del.CommandType = CommandType.StoredProcedure;

                        del.Parameters.AddWithValue("@TrainId", trainId);

                        del.ExecuteNonQuery();
                    }

                    foreach (var date in cancelDateList)
                    {
                        using (var cmd = new SqlCommand("spInsertTrainCancellationDate", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("@TrainId", trainId);
                            cmd.Parameters.AddWithValue("@Date", date);

                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                // 6️⃣ TEMPORARY STOPPAGES (Save/Update)
                string tempDates = Request.Form["tempDates"];
                var tempDateList = new List<DateTime>();

                if (!string.IsNullOrEmpty(tempDates))
                {
                    tempDateList = tempDates
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(d => DateTime.TryParse(d.Trim(), out _))
                        .Select(d => DateTime.Parse(d.Trim()))
                        .Distinct()
                        .OrderBy(d => d.Date)
                        .ToList();
                }

                var tempDateRanges = new List<(DateTime From, DateTime To)>();
                if (tempDateList.Any())
                {
                    DateTime rStart = tempDateList[0];
                    DateTime rEnd = tempDateList[0];

                    for (int i = 1; i < tempDateList.Count; i++)
                    {
                        if (tempDateList[i].Date == rEnd.AddDays(1).Date)
                        {
                            rEnd = tempDateList[i];
                        }
                        else
                        {
                            tempDateRanges.Add((rStart, rEnd));
                            rStart = tempDateList[i];
                            rEnd = tempDateList[i];
                        }
                    }
                    tempDateRanges.Add((rStart, rEnd));
                }

                // 🔥 FALLBACK FOR TEMPORARY DATES:
                // If the user added a temporary halt but didn't separately pick dates in tempDates:
                // 1. Inherit from skipDateRanges (if regular station was skipped for this halt)
                // 2. Or default to the upcoming 3-month operational window
                if (!tempDateRanges.Any() && routes.Any(r => r.IsTemporary))
                {
                    if (skipDateRanges.Any())
                    {
                        tempDateRanges.AddRange(skipDateRanges);
                    }
                    else
                    {
                        tempDateRanges.Add((DateTime.Today, DateTime.Today.AddMonths(3)));
                    }
                }

                try
                {
                    EnsureTemporaryStopsSchema(conn);

                    using (var del = new SqlCommand("spDeleteTemporaryStopsByTrainId", conn))
                    {
                        del.CommandType = CommandType.StoredProcedure;
                        del.Parameters.AddWithValue("@TrainId", trainId);
                        del.ExecuteNonQuery();
                    }

                    if (tempDateRanges.Any())
                    {
                        for (int i = 0; i < routes.Count; i++)
                        {
                            var route = routes[i];
                            if (route.IsTemporary)
                            {
                                int afterStationId = (i > 0) ? routes[i - 1].StationId : 0;
                                foreach (var range in tempDateRanges)
                                {
                                    using (var insCmd = new SqlCommand("spInsertTemporaryStop", conn))
                                    {
                                        insCmd.CommandType = CommandType.StoredProcedure;
                                        insCmd.Parameters.AddWithValue("@TrainId", trainId);
                                        insCmd.Parameters.AddWithValue("@StationId", route.StationId);
                                        insCmd.Parameters.AddWithValue("@ArrivalTime", (object?)route.ArrivalTime ?? DBNull.Value);
                                        insCmd.Parameters.AddWithValue("@DepartureTime", (object?)route.DepartureTime ?? DBNull.Value);
                                        insCmd.Parameters.AddWithValue("@RouteDay", route.Day > 0 ? route.Day : 1);
                                        insCmd.Parameters.AddWithValue("@Distance", route.Distance);
                                        insCmd.Parameters.AddWithValue("@StopNumber", route.StopNumber > 0 ? route.StopNumber : (i + 1));
                                        insCmd.Parameters.AddWithValue("@AfterStationId", afterStationId);
                                        insCmd.Parameters.AddWithValue("@FromDate", range.From);
                                        insCmd.Parameters.AddWithValue("@ToDate", range.To);
                                        insCmd.Parameters.AddWithValue("@Reason", !string.IsNullOrWhiteSpace(route.TemporaryReason) ? route.TemporaryReason : "Festival Season");
                                        insCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception) { }
            }

            TempData["SuccessMessage"] = "✅ Train routes updated successfully!";
            return RedirectToAction("AddRoute", new { id = trainId, trainType = trainType });
        }


        [AllowAnonymous]    
        public IActionResult GetTrainRoute(int trainId, string? trainType = null, DateTime? journeyDate = null, int? trainNumber = null, string? trainName = null, string? fromStation = null, string? toStation = null)
        {

            var routes = new List<TrainRoute>();

            // ✅ FIX: handle null properly
            if (!journeyDate.HasValue || journeyDate.Value < new DateTime(1753, 1, 1))
            {
                journeyDate = DateTime.Today;
            }

            string runsOn = " - - - - - - - ";

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                try
                {
                    using (var cmdTrain = new SqlCommand("SELECT Number, Name, RunMon, RunTue, RunWed, RunThu, RunFri, RunSat, RunSun FROM Trains WHERE Id = @TrainId", conn))
                    {
                        cmdTrain.Parameters.AddWithValue("@TrainId", trainId);
                        using (var r = cmdTrain.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                if (trainNumber == null && !r.IsDBNull(0)) trainNumber = r.GetInt32(0);
                                if (string.IsNullOrEmpty(trainName) && !r.IsDBNull(1)) trainName = r.GetString(1);
                                char m = (!r.IsDBNull(2) && r.GetInt32(2) == 1) ? 'M' : '-';
                                char tu = (!r.IsDBNull(3) && r.GetInt32(3) == 1) ? 'T' : '-';
                                char w = (!r.IsDBNull(4) && r.GetInt32(4) == 1) ? 'W' : '-';
                                char th = (!r.IsDBNull(5) && r.GetInt32(5) == 1) ? 'T' : '-';
                                char f = (!r.IsDBNull(6) && r.GetInt32(6) == 1) ? 'F' : '-';
                                char sa = (!r.IsDBNull(7) && r.GetInt32(7) == 1) ? 'S' : '-';
                                char su = (!r.IsDBNull(8) && r.GetInt32(8) == 1) ? 'S' : '-';
                                runsOn = $"{m}{tu}{w}{th}{f}{sa}{su}";
                            }
                        }
                    }
                }
                catch { }

                bool isMemuDemu = !string.IsNullOrEmpty(trainType) && (trainType.ToUpper() == "MEMU" || trainType.ToUpper() == "DEMU" || trainType.ToUpper() == "MMTS");

                using (var cmd = new SqlCommand(isMemuDemu ? "spGetMemuDemuRoutes" : "spGetNormalTrainRoutes", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Value.Date);

                    using (var reader = cmd.ExecuteReader())
                    {
                        int displayIndex = 1;

                        while (reader.Read())
                        {
                            if (runsOn == " - - - - - - - " && !reader.IsDBNull(reader.GetOrdinal("RunsOn")))
                            {
                                runsOn = reader.GetString(reader.GetOrdinal("RunsOn"));
                            }

                            routes.Add(new TrainRoute
                            {
                                StationId = reader.GetInt32(reader.GetOrdinal("StationId")),
                                StopNumber = displayIndex++,//reader.GetInt32(reader.GetOrdinal("StopNumber")),
                                ArrivalTime = reader.IsDBNull(reader.GetOrdinal("ArrivalTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("ArrivalTime")),
                                DepartureTime = reader.IsDBNull(reader.GetOrdinal("DepartureTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("DepartureTime")),
                                Station = new Station
                                {
                                    Name = reader.GetString(reader.GetOrdinal("StationName")),
                                    Code = reader.GetString(reader.GetOrdinal("StationCode"))
                                },
                                Distance = reader.GetInt32(reader.GetOrdinal("DistanceFromSource")),
                                Day = reader.GetInt32(reader.GetOrdinal("Day")),
                            });
                        }
                    }
                }

                // ✅ Check temporary skipped routes & omit skipped stations until skipped dates have ended
                var tempRoutes = new List<(int StationId, DateTime FromDate, DateTime ToDate)>();
                try
                {
                    using (var cmdTemp = new SqlCommand("spGetTrainTempRoutes", conn))
                    {
                        cmdTemp.CommandType = CommandType.StoredProcedure;
                        cmdTemp.Parameters.AddWithValue("@TrainId", trainId);

                        using (var readerTemp = cmdTemp.ExecuteReader())
                        {
                            while (readerTemp.Read())
                            {
                                tempRoutes.Add((
                                    Convert.ToInt32(readerTemp["StationId"]),
                                    Convert.ToDateTime(readerTemp["FromDate"]),
                                    Convert.ToDateTime(readerTemp["ToDate"])
                                ));
                            }
                        }
                    }
                }
                catch { }

                DateTime targetDate = journeyDate?.Date ?? DateTime.Today;

                foreach (var route in routes)
                {
                    var stationSkips = tempRoutes.Where(t => t.StationId == route.StationId).ToList();
                    if (stationSkips.Any())
                    {
                        DateTime maxSkip = stationSkips.Max(t => t.ToDate.Date);
                        DateTime minSkip = stationSkips.Min(t => t.FromDate.Date);

                        bool isDateSkipped = stationSkips.Any(t => targetDate >= t.FromDate.Date && targetDate <= t.ToDate.Date)
                                             || (targetDate >= minSkip && targetDate <= maxSkip)
                                             || (DateTime.Today <= maxSkip && targetDate <= maxSkip);

                        if (isDateSkipped)
                        {
                            route.IsSkipped = true;
                        }
                    }
                }

                // Omit skipped stations from the train schedule timetable until skipped dates have ended
                var activeRoutes = routes.Where(r => !r.IsSkipped).ToList();
                if (activeRoutes.Any())
                {
                    int stopIndex = 1;
                    foreach (var r in activeRoutes)
                    {
                        r.StopNumber = stopIndex++;
                    }
                    routes = activeRoutes;
                }

                // ✅ Check active temporary stops for this train on targetDate
                try
                {
                    var activeTempStops = new List<(int StationId, string Code, string Name, TimeSpan? Arr, TimeSpan? Dep, int Day, string Reason, int Distance)>();
                    var allConfiguredTempStationIds = new HashSet<int>();

                    using (var cmdTempStops = new SqlCommand("spGetTemporaryStopsByTrainId", conn))
                    {
                        cmdTempStops.CommandType = CommandType.StoredProcedure;
                        cmdTempStops.Parameters.AddWithValue("@TrainId", trainId);
                        using (var r = cmdTempStops.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                int stnId = Convert.ToInt32(r["StationId"]);
                                allConfiguredTempStationIds.Add(stnId);

                                DateTime fDate = Convert.ToDateTime(r["FromDate"]).Date;
                                DateTime tDate = Convert.ToDateTime(r["ToDate"]).Date;
                                if (targetDate >= fDate && targetDate <= tDate)
                                {
                                    int dist = 0;
                                    try
                                    {
                                        int distOrd = r.GetOrdinal("Distance");
                                        if (distOrd >= 0 && !r.IsDBNull(distOrd))
                                        {
                                            dist = Convert.ToInt32(r.GetValue(distOrd));
                                        }
                                    }
                                    catch { }

                                    activeTempStops.Add((
                                        stnId,
                                        r["StationCode"].ToString() ?? "",
                                        r["StationName"].ToString() ?? "",
                                        r["ArrivalTime"] == DBNull.Value ? null : (TimeSpan?)r["ArrivalTime"],
                                        r["DepartureTime"] == DBNull.Value ? null : (TimeSpan?)r["DepartureTime"],
                                        Convert.ToInt32(r["RouteDay"]),
                                        r["Reason"]?.ToString() ?? "Festival Season",
                                        dist
                                    ));
                                }
                            }
                        }
                    }

                    var activeStationIds = new HashSet<int>();
                    foreach (var ts in activeTempStops)
                    {
                        activeStationIds.Add(ts.StationId);
                        var existingRoute = routes.FirstOrDefault(rt => rt.StationId == ts.StationId);
                        if (existingRoute != null)
                        {
                            existingRoute.IsTemporary = true;
                            existingRoute.TemporaryReason = ts.Reason;
                            if (ts.Distance > 0 && existingRoute.Distance == 0)
                            {
                                existingRoute.Distance = ts.Distance;
                            }
                        }
                        else
                        {
                            routes.Add(new TrainRoute
                            {
                                TrainId = trainId,
                                StationId = ts.StationId,
                                StationCode = ts.Code,
                                StationName = ts.Name,
                                ArrivalTime = ts.Arr,
                                DepartureTime = ts.Dep,
                                Day = ts.Day,
                                Distance = ts.Distance,
                                IsTemporary = true,
                                TemporaryReason = ts.Reason,
                                Station = new Station
                                {
                                    Id = ts.StationId,
                                    Code = ts.Code,
                                    Name = ts.Name
                                }
                            });
                        }
                    }

                    // Temporary stops not active on targetDate should not appear in schedule
                    var inactiveTempStationIds = allConfiguredTempStationIds.Except(activeStationIds).ToList();
                    if (inactiveTempStationIds.Any())
                    {
                        routes = routes.Where(rt => !inactiveTempStationIds.Contains(rt.StationId)).ToList();
                    }

                    routes = routes.OrderBy(r => r.Day)
                                   .ThenBy(r => r.ArrivalTime ?? r.DepartureTime ?? TimeSpan.Zero)
                                   .ToList();

                    for (int i = 0; i < routes.Count; i++)
                    {
                        routes[i].StopNumber = i + 1;
                    }
                }
                catch (SqlException) { }
            }

            ViewBag.RunsOn = runsOn;
            ViewBag.TrainNumber = trainNumber;
            ViewBag.TrainName = trainName;
            ViewBag.FromStation = fromStation;
            ViewBag.ToStation = toStation;

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
                                Name = reader.GetString(2),
                                City = reader.IsDBNull(3) ? null : reader.GetString(3),
                                State = reader.IsDBNull(4) ? null : reader.GetString(4)
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
                    cmd.Parameters.AddWithValue("@City", string.IsNullOrEmpty(station.City) ? DBNull.Value : station.City.Trim());
                    cmd.Parameters.AddWithValue("@State", string.IsNullOrEmpty(station.State) ? DBNull.Value : station.State.Trim());

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
                                Name = reader.GetString(2),
                                City = reader.IsDBNull(3) ? null : reader.GetString(3),
                                State = reader.IsDBNull(4) ? null : reader.GetString(4)
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
                    checkCmd.Parameters.AddWithValue(
                        "@City",
                        string.IsNullOrWhiteSpace(station.City)
                            ? DBNull.Value
                            : station.City.Trim()
                    );
                    checkCmd.Parameters.AddWithValue("@Code", station.Code.Trim());
                    //checkCmd.Parameters.AddWithValue("@State", station.State.Trim());

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
                    cmd.Parameters.AddWithValue("@State", station.State.Trim());
                    cmd.Parameters.AddWithValue(
                        "@City",
                        string.IsNullOrWhiteSpace(station.City)
                            ? DBNull.Value
                            : station.City.Trim()
                    );
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
                                Duration = reader["Duration"].ToString(),
                                RunMon = reader.IsDBNull(reader.GetOrdinal("RunMon"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunMon")),
                                RunTue = reader.IsDBNull(reader.GetOrdinal("RunTue"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunTue")),
                                RunWed = reader.IsDBNull(reader.GetOrdinal("RunWed"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunWed")),
                                RunThu = reader.IsDBNull(reader.GetOrdinal("RunThu"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunThu")),

                                RunFri = reader.IsDBNull(reader.GetOrdinal("RunFri"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunFri")),

                                RunSat = reader.IsDBNull(reader.GetOrdinal("RunSat"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunSat")),

                                RunSun = reader.IsDBNull(reader.GetOrdinal("RunSun"))
                                        ? false
                                        : reader.GetBoolean(reader.GetOrdinal("RunSun")),
                                IsTrainCancelled = reader.IsDBNull(reader.GetOrdinal("IsCancelled"))
                                                  ? false
                                                  : reader.GetBoolean(reader.GetOrdinal("IsCancelled"))
                            };
                        }
                    }
                }

                if (train == null)
                    return NotFound();

                // Load service dates and coach positions
                try
                {
                    using (var dateCmd = new SqlCommand("SELECT ServiceStartDate, ServiceEndDate, CoachPositions FROM Trains WHERE Id = @TrainId", conn))
                    {
                        dateCmd.Parameters.AddWithValue("@TrainId", id);
                        using (var dReader = dateCmd.ExecuteReader())
                        {
                            if (dReader.Read())
                            {
                                train.ServiceStartDate = dReader.IsDBNull(0) ? null : dReader.GetDateTime(0);
                                train.ServiceEndDate = dReader.IsDBNull(1) ? null : dReader.GetDateTime(1);
                                train.CoachPositions = dReader.IsDBNull(2) ? null : dReader.GetString(2);
                            }
                        }
                    }
                }
                catch { }

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
                                SeatsAvailable = Convert.ToInt32(reader["SeatsAvailable"]),
                                SeatPrefix = reader["SeatPrefix"].ToString()
                            });
                        }
                    }
                }

                // --- ✅ Get train routes ---
                using (var cmd = new SqlCommand("spGetTrainRoutesByTrainId", conn))
                {

                    string trainType = null;

                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);
                    cmd.Parameters.AddWithValue("@TrainType", string.IsNullOrEmpty(trainType) ? (object)DBNull.Value : trainType);

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
            string deletedClassIds,
            string? CoachPositions
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
                        cmd.Parameters.AddWithValue("RunMon", train.RunMon);
                        cmd.Parameters.AddWithValue("RunTue", train.RunTue);
                        cmd.Parameters.AddWithValue("RunWed", train.RunWed);
                        cmd.Parameters.AddWithValue("RunThu", train.RunThu);
                        cmd.Parameters.AddWithValue("RunFri", train.RunFri);
                        cmd.Parameters.AddWithValue("RunSat", train.RunSat);
                        cmd.Parameters.AddWithValue("RunSun", train.RunSun);
                        cmd.Parameters.AddWithValue("IsCancelled", train.IsTrainCancelled);

                        cmd.ExecuteNonQuery();
                    }

                    // Save Service dates and CoachPositions
                    try
                    {
                        using (var dateCmd = new SqlCommand("UPDATE Trains SET ServiceStartDate = @ServiceStartDate, ServiceEndDate = @ServiceEndDate, CoachPositions = @CoachPositions WHERE Id = @TrainId", conn))
                        {
                            dateCmd.Parameters.AddWithValue("@TrainId", train.Id);
                            dateCmd.Parameters.AddWithValue("@ServiceStartDate", (object?)train.ServiceStartDate ?? DBNull.Value);
                            dateCmd.Parameters.AddWithValue("@ServiceEndDate", (object?)train.ServiceEndDate ?? DBNull.Value);
                            dateCmd.Parameters.AddWithValue("@CoachPositions", (object?)CoachPositions ?? DBNull.Value);
                            dateCmd.ExecuteNonQuery();
                        }

                        // Generate/update coaches & berths if CoachPositions provided
                        if (!string.IsNullOrWhiteSpace(CoachPositions))
                        {
                            using (var trans = conn.BeginTransaction())
                            {
                                try
                                {
                                    ParseCompositionAndGenerateSeats(conn, trans, train.Id, CoachPositions);
                                    trans.Commit();
                                }
                                catch (Exception cEx)
                                {
                                    trans.Rollback();
                                    Console.WriteLine("Error generating coaches in EditTrain: " + cEx.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error updating service dates/coach positions: " + ex.Message);
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
                            cmd.Parameters.AddWithValue("@SeatsAvailable", seat);       // ✅ not seats
                            cmd.Parameters.AddWithValue("@SeatPrefix", string.IsNullOrWhiteSpace(prefix) ? (object)DBNull.Value : prefix);
                            // ✅ not seatPrefixes

                            cmd.ExecuteNonQuery();
                        }
                    }

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
                            cmd.Parameters.AddWithValue("@Day", routeOrder[i]);
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

        public void SeedCoachesAndSeats(string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // 1. Fetch all existing TrainClasses
                var trainClasses = new List<dynamic>();

                using (var cmd = new SqlCommand("spFetchTrainClasses", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            trainClasses.Add(new
                            {
                                ClassId = Convert.ToInt32(rdr["Id"]),
                                TrainId = Convert.ToInt32(rdr["TrainId"]),
                                ClassCode = rdr["Code"]?.ToString() ?? "SL",
                                SeatPrefix = rdr["SeatPrefix"]?.ToString() ?? "S",
                                TotalSeats = Convert.ToInt32(rdr["SeatsAvailable"])
                            });
                        }
                    }
                }

                // 2. Loop through each TrainClass and build Coaches & Seats
                foreach (var cls in trainClasses)
                {
                    // Determine coach size
                    int seatsPerCoach = 80;
                    string cleanCode = cls.ClassCode.ToUpper();
                    if (cleanCode.Contains("2A")) seatsPerCoach = 48;
                    else if (cleanCode.Contains("CC")) seatsPerCoach = 78;

                    int capacity = cls.TotalSeats;
                    int coachCount = (int)Math.Ceiling((double)capacity / seatsPerCoach);

                    for (int c = 1; c <= coachCount; c++)
                    {
                        string coachName = $"{cls.SeatPrefix}{c}";
                        int seatsInThisCoach = Math.Min(seatsPerCoach, capacity - ((c - 1) * seatsPerCoach));

                        // A. Insert Coach
                        int coachId = 0;

                        using (var cmd = new SqlCommand("spInsertCoach", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("@TrainId", cls.TrainId);
                            cmd.Parameters.AddWithValue("@ClassId", cls.ClassId);
                            cmd.Parameters.AddWithValue("@CoachName", coachName);
                            cmd.Parameters.AddWithValue("@SeatsCount", seatsInThisCoach);

                            coachId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // B. Insert Seats for this coach with cyclic berth calculations & quota ranges
                        for (int s = 1; s <= seatsInThisCoach; s++)
                        {
                            // 1. Determine Berth
                            string berth = "M";
                            if (cleanCode.Contains("SL") || cleanCode.Contains("3A"))
                            {
                                int rem = s % 8;
                                if (rem == 1 || rem == 4) berth = "LB";
                                else if (rem == 2 || rem == 5) berth = "MB";
                                else if (rem == 3 || rem == 6) berth = "UB";
                                else if (rem == 7) berth = "SL";
                                else berth = "SU";
                            }
                            else if (cleanCode.Contains("2A"))
                            {
                                int rem = s % 6;
                                if (rem == 1 || rem == 3) berth = "LB";
                                else if (rem == 2 || rem == 4) berth = "UB";
                                else if (rem == 5) berth = "SL";
                                else berth = "SU";
                            }
                            else // CC
                            {
                                int rem = s % 5;
                                if (rem == 1 || rem == 5) berth = "W";
                                else if (rem == 2 || rem == 4) berth = "A";
                                else berth = "M";
                            }

                            // 2. Determine Quota Range
                            string quota = "GENERAL";
                            if (s >= 1 && s <= 8) quota = "SENIOR"; // First 8 seats
                            else if (s >= 9 && s <= 16) quota = "LADIES"; // Next 8 seats
                            else if (s >= 17 && s <= 32) quota = "TATKAL"; // Next 16 seats

                            using (var cmd = new SqlCommand("spInsertCoachSeat", conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;

                                cmd.Parameters.AddWithValue("@CoachId", coachId);
                                cmd.Parameters.AddWithValue("@SeatNumber", s);
                                cmd.Parameters.AddWithValue("@BerthType", berth);
                                cmd.Parameters.AddWithValue("@QuotaType", quota);

                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
        }

        [AllowAnonymous]
        public IActionResult MigrateExistingTrains()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1️⃣ First, clean up the old coaches and seats tables to avoid duplicates
                using (var cmd = new SqlCommand("spResetCoachesAndSeats", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.ExecuteNonQuery();
                }

                // 2️⃣ Fetch all existing trains
                var trains = new List<dynamic>();

                using (var cmd = new SqlCommand("spFetchAllTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            trains.Add(new
                            {
                                Id = Convert.ToInt32(rdr["Id"]),
                                Name = rdr["Name"]?.ToString() ?? "",
                                Number = Convert.ToInt32(rdr["Number"])
                            });
                        }
                    }
                }

                // 3️⃣ Loop through each train, detect its classes, and build a smart composition
                // 🟢 UPDATED AUTO-MIGRATION LOGIC INSIDE MigrateExistingTrains
                foreach (var t in trains)
                {
                    var classes = new List<string>();
                    using (var cmd = new SqlCommand("spFetchTrainClassCodes", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@TrainId", t.Id);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                classes.Add(rdr["Code"]?.ToString()?.ToUpper()?.Trim());
                            }
                        }
                    }
                    var compositionList = new List<string>();
                    string trainName = t.Name.ToUpper();
                    if (trainName.Contains("VANDEBHARAT") || trainName.Contains("VANDE BHARAT") || trainName.Contains("VB"))
                    {
                        // High-capacity Vande Bharat Composition (16 Coaches!)
                        if (classes.Contains("EC")) compositionList.AddRange(new[] { "E1", "E2" });
                        compositionList.AddRange(new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "C12", "C13", "C14" });
                    }
                    else if (trainName.Contains("DURONTO") || trainName.Contains("RAJDHANI"))
                    {
                        // Premium 20-Coach Rajdhani Lineup
                        if (classes.Contains("1A")) compositionList.AddRange(new[] { "H1", "H2" });
                        if (classes.Contains("2A")) compositionList.AddRange(new[] { "A1", "A2", "A3", "A4" });
                        if (classes.Contains("3A")) compositionList.AddRange(new[] { "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10" });
                        if (classes.Contains("SL")) compositionList.AddRange(new[] { "S1", "S2", "S3", "S4" });
                    }
                    else if (trainName.Contains("GARIB RATH") || trainName.Contains("GARIBRATH"))
                    {
                        // High-capacity 16-Coach Garib Rath Lineup
                        compositionList.AddRange(new[] { "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "G9", "G10", "G11", "G12", "G13", "G14" });
                    }
                    else
                    {
                        // Realistic 20-Coach Standard Express Composition
                        if (classes.Contains("2S")) compositionList.AddRange(new[] { "GS1", "GS2" }); // General Seating Front
                        if (classes.Contains("1A")) compositionList.Add("H1");
                        if (classes.Contains("2A")) compositionList.AddRange(new[] { "A1", "A2" });
                        if (classes.Contains("3A")) compositionList.AddRange(new[] { "B1", "B2", "B3", "B4" });
                        if (classes.Contains("SL")) compositionList.AddRange(new[] { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S10" });
                        if (classes.Contains("2S")) compositionList.AddRange(new[] { "GS3", "GS4" }); // General Seating Rear
                    }
                    // Fallback default lineup
                    if (compositionList.Count == 0)
                    {
                        compositionList.AddRange(new[] { "S1", "S2", "S3", "S4", "B1", "B2" });
                    }
                    string coachPositionsString = string.Join(",", compositionList);

                    // 4️⃣ Update the CoachPositions column in the Trains table
                    using (var cmd = new SqlCommand("spUpdateTrainCoachPositions", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@Composition", coachPositionsString);
                        cmd.Parameters.AddWithValue("@TrainId", t.Id);

                        cmd.ExecuteNonQuery();
                    }

                    // 5️⃣ Generate dynamic coaches and seat layouts for this train
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            // Call the composition parser we wrote in the previous step!
                            ParseCompositionAndGenerateSeats(conn, transaction, t.Id, coachPositionsString);
                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            // Log or bypass errors to ensure seeder continues for other trains
                        }
                    }
                }
            }

            return Ok("Successfully migrated all 200+ existing trains and generated dynamic coach seating layouts!");
        }

        public void ParseCompositionAndGenerateSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int trainId,
            string coachPositions)
        {
            if (string.IsNullOrWhiteSpace(coachPositions)) return;

            // Split composition (e.g., "S1,S2,S3,B1,B2,A1" -> ["S1", "S2", "S3", "B1", "B2", "A1"])
            var coachList = coachPositions.Split(',')
                                          .Select(c => c.Trim().ToUpper())
                                          .Where(c => !string.IsNullOrEmpty(c))
                                          .ToList();

            foreach (var coachCode in coachList)
            {
                // 1. Identify Prefix and Seat count based on Coach Code
                string prefix = new string(coachCode.TakeWhile(char.IsLetter).ToArray());

                // Standardize auto-generation to full IRCTC names
                string classCode = "Sleeper (SL)";
                int seatsPerCoach = 80;

                switch (prefix)
                {
                    case "S":
                    case "SE":
                        classCode = "Sleeper (SL)";
                        seatsPerCoach = 80;
                        break;
                    case "B":
                        classCode = "AC 3 Tier (3A)";
                        seatsPerCoach = 72;
                        break;
                    case "M":
                        classCode = "AC 3 Economy (3E)";
                        seatsPerCoach = 83;
                        break;
                    case "A":
                        classCode = "AC 2 Tier (2A)";
                        seatsPerCoach = 54;
                        break;
                    case "H":
                        classCode = "AC First Class (1A)";
                        seatsPerCoach = 24;
                        break;
                    case "C":
                    case "D":
                    case "E":
                        classCode = "AC Chair Car (CC)";
                        seatsPerCoach = 78;
                        break;
                    case "GS":
                    case "GN":
                    case "GEN":
                    case "UR":
                    case "D1":
                    case "D2":
                        classCode = "Second Sitting (2S)";
                        seatsPerCoach = 108;
                        break;
                    case "ENG":
                    case "LOCO":
                    case "EOG":
                    case "HOG":
                    case "SLR":
                    case "PC":
                    case "PWR":
                        continue; // Non-passenger operational units (Locomotive, Generator Car, Guard/Brake Van, Pantry)
                    default:
                        continue;
                }

                // 2. Fetch the corresponding Class ID for this train in the database (with loose matching for descriptive names)
                int classId = 0;

                using (var cmd = new SqlCommand("spFetchTrainClassId", conn, transaction))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@Code", classCode);

                    var result = cmd.ExecuteScalar();

                    if (result != null)
                    {
                        classId = Convert.ToInt32(result);
                    }
                }

                // Call your stored procedure to create the class if it doesn't exist
                if (classId == 0)
                {
                    using (var cmd = new SqlCommand("spInsertTrainClass", conn, transaction))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@Code", classCode);
                        cmd.Parameters.AddWithValue("@SeatPrefix", prefix);
                        cmd.Parameters.AddWithValue("@SeatsAvailable", 0); // Capacity starts at 0, updated dynamically below
                        cmd.ExecuteNonQuery();
                    }

                    // Retrieve the newly created Class ID
                    using (var cmd = new SqlCommand("spFetchTrainClassId", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@Code", classCode);
                        classId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                // 3. Create the Coach Record
                int coachId = 0;

                using (var cmd = new SqlCommand("spInsertCoach", conn, transaction))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@CoachName", coachCode); // S1, B1, etc.
                    cmd.Parameters.AddWithValue("@SeatsCount", seatsPerCoach);

                    coachId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 4. Generate the dynamic Seat Layouts for this coach
                for (int s = 1; s <= seatsPerCoach; s++)
                {
                    string berth = "M";
                    // Match using Contains to handle full IRCTC descriptive names perfectly
                    if (classCode.Contains("SL") || classCode.Contains("3A") || classCode.Contains("3E"))
                    {
                        int rem = s % 8;
                        if (rem == 1 || rem == 4) berth = "LB";
                        else if (rem == 2 || rem == 5) berth = "MB";
                        else if (rem == 3 || rem == 6) berth = "UB";
                        else if (rem == 7) berth = "SL";
                        else berth = "SU";
                    }
                    else if (classCode.Contains("2A") || classCode.Contains("1A"))
                    {
                        int rem = s % 6;
                        if (rem == 1 || rem == 3) berth = "LB";
                        else if (rem == 2 || rem == 4) berth = "UB";
                        else if (rem == 5) berth = "SL";
                        else berth = "SU";
                    }
                    else // CC
                    {
                        int rem = s % 5;
                        if (rem == 1 || rem == 5) berth = "W";
                        else if (rem == 2 || rem == 4) berth = "A";
                        else berth = "M";
                    }

                    string quota = "GENERAL";
                    if (seatsPerCoach == 80)
                    {
                        if (s >= 1 && s <= 6) quota = "SENIOR";
                        else if (s >= 7 && s <= 12) quota = "LADIES";
                        else if (s >= 13 && s <= 20) quota = "TATKAL";
                        else if (s >= 21 && s <= 28) quota = "PREMIUM TATKAL";
                    }
                    else if (seatsPerCoach == 78)
                    {
                        if (s >= 1 && s <= 6) quota = "SENIOR";
                        else if (s >= 7 && s <= 12) quota = "LADIES";
                        else if (s >= 13 && s <= 20) quota = "TATKAL";
                        else if (s >= 21 && s <= 28) quota = "PREMIUM TATKAL";
                    }
                    else if (seatsPerCoach == 48)
                    {
                        if (s >= 1 && s <= 3) quota = "SENIOR";
                        else if (s >= 4 && s <= 6) quota = "LADIES";
                        else if (s >= 7 && s <= 12) quota = "TATKAL";
                        else if (s >= 13 && s <= 18) quota = "PREMIUM TATKAL";
                    }
                    else if (seatsPerCoach == 24)
                    {
                        if (s >= 1 && s <= 2) quota = "SENIOR";
                        else if (s >= 3 && s <= 4) quota = "LADIES";
                        else if (s >= 5 && s <= 8) quota = "TATKAL";
                        else if (s >= 9 && s <= 12) quota = "PREMIUM TATKAL";
                    }
                    else // General seating / 2S (e.g. 108 seats)
                    {
                        if (s >= 1 && s <= 4) quota = "SENIOR";
                        else if (s >= 5 && s <= 8) quota = "LADIES";
                        else if (s >= 9 && s <= 20) quota = "TATKAL";
                        else if (s >= 21 && s <= 32) quota = "PREMIUM TATKAL";
                    }

                    using (var cmd = new SqlCommand("spInsertCoachSeat", conn, transaction))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@CoachId", coachId);
                        cmd.Parameters.AddWithValue("@SeatNumber", s);
                        cmd.Parameters.AddWithValue("@BerthType", berth);
                        cmd.Parameters.AddWithValue("@QuotaType", quota);

                        cmd.ExecuteNonQuery();
                    }
                }
            }

            // 5. Update Total capacities across all classes for this train (counting only GENERAL quota seats)
            using (var cmd = new SqlCommand("spUpdateClassCapacities", conn, transaction))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@TrainId", trainId);

                cmd.ExecuteNonQuery();
            }
        }

        // GET: /Admin/DeleteTrain/5
        [HttpGet("/Admin/DeleteTrain/{id}/{trainType}")]
        public IActionResult DeleteTrain(int id, string trainType)
        {
            trainType ??= "NORMAL"; // fallback
            TrainClass.DeleteTrain(_connectionString, id, trainType);
            TempData["Success"] = "Train deleted successfully!";
            return RedirectToAction("TrainList");
        }

        public IActionResult TrainList()
        {
            var trains = Train.GetTrainsList(_connectionString);
            return View(trains);
        }

        [AllowAnonymous]
        public IActionResult RunSeeder()
        {
            SeedCoachesAndSeats(_connectionString);
            return Ok("Database coaches and layouts successfully seeded!");
        }

    }

}
