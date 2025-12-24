using IrctcClone.Models;
using IRCTCClone.Helpers;
using IRCTCClone.Models;
using IRCTCClone.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Server;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using Font = iTextSharp.text.Font;
using Image = iTextSharp.text.Image;
using Microsoft.AspNetCore.RateLimiting;


namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    [Authorize]
    public class BookingController : Controller
    {
        private readonly string _connectionString;
        private readonly EmailService _emailService;
        private const int RAC_LIMIT = 8; //recently added
        private readonly IAvailabilityService _availabilityService;
        private readonly IConfiguration _configuration;

        public BookingController(IConfiguration configuration, EmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _emailService = emailService;
            _configuration = configuration;

            /*            _baseUrl = configuration["AppSettings:BaseUrl"];*/
        }


        private string GetBerthBySeatNumber(string seatNumber)
        {
            int seat = int.Parse(seatNumber);
            int mod = seat % 8;
            return mod switch
            {
                1 or 4 => "LB",
                2 or 5 => "MB",
                3 or 6 => "UB",
                7 => "SL",
                8 => "SU",
                _ => "--"
            };
        }


        //-------------------------------SENDS OTP EMAIL---------------------------------//
        private void SendOTPEmail(string userEmail, string otp)
        {
            string subject = "IRCTC Clone – OTP Verification";
            string body = $@"
                <h3>OTP Verification</h3>
                <p>Your One-Time Password (OTP) is:</p>
                <h2>{otp}</h2>
                <p>This OTP is valid for <strong>1 minute</strong>.</p>
                <p>If you did not initiate this request, please ignore this email.</p>";

            _emailService.SendEmail(
                userEmail,
                subject,
                body
            );
        }


        //-------------------------------HASHES OTP---------------------------------//
        private bool VerifyOTP(string userId, string enteredOtp, string purpose)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string enteredOtpHash = HashOTP(enteredOtp);

                using (var cmd = new SqlCommand("spVerifyOTP", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@EnteredOtpHash", enteredOtpHash);
                    cmd.Parameters.AddWithValue("@Purpose", "PAYMENT");

                    var isValidParam = new SqlParameter("@IsValid", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    cmd.Parameters.Add(isValidParam);

                    cmd.ExecuteNonQuery();
                    return (bool)isValidParam.Value;
                }
            }

        }



        /*---------------------------------GETS SEATS STATUS------------------------------*/
        private SeatStatus GetSeatStatus(int trainId, int classId)
        {
            var seatStatus = new SeatStatus();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("spGetSeatStatusCounts", conn))
            {
                conn.Open();
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        return new SeatStatus
                        {
                            SeatsAvailable = Convert.ToInt32(rdr["SeatsAvailable"]),
                            RACSeats = Convert.ToInt32(rdr["RACSeats"]),
                            ConfirmedCount = Convert.ToInt32(rdr["ConfirmedCount"]),
                            RACCount = Convert.ToInt32(rdr["RACCount"]),
                            WLCount = Convert.ToInt32(rdr["WLCount"])
                        };
                    }
                }
            }
            return null;
        }

        /*------------------------------COUNTS THE BOOKED SEATS---------------------------------*/
        private int GetBookedSeatsCount(int trainId, int classId)
        {
            int count = 0;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("GetBookedSeatsCount", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure; // ✅ Important!
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);

                    object result = cmd.ExecuteScalar();
                    count = result != DBNull.Value ? Convert.ToInt32(result) : 0;
                }
            }
            return count;
        }

        /*----------------------------------CALCULATES THE FARE PER PASSENGER----------------------------*/
        private decimal CalculateFare(
            TrainClass cls,
            int totalSeats,
            int bookedSeats,
            int passengerCount,
            string quota,
            out decimal gst,
            out decimal totalSurge,
            out decimal totalQuotaCharge,
            out decimal convenienceFee,
            out decimal insurance
        )
        {
            decimal baseFare = cls.BaseFare;

            totalSurge = 0;
            totalQuotaCharge = 0;

            //--------------- SURGE (per passenger) -----------------
            decimal surgePerPassenger = 0;

            if (cls.DynamicPricing && totalSeats > 0)
            {
                decimal occupancyRate = (decimal)bookedSeats / totalSeats;

                if (occupancyRate > 0.7m)
                    surgePerPassenger = Math.Round(baseFare * 0.10m, 2);
                else if (occupancyRate > 0.5m)
                    surgePerPassenger = Math.Round(baseFare * 0.05m, 2);
            }

            totalSurge = surgePerPassenger * passengerCount;

            //--------------- QUOTA -----------------
            decimal quotaChargePerPassenger = 0;

            var tatkalConfig = new Dictionary<string, (decimal percent, decimal min, decimal max)>(StringComparer.OrdinalIgnoreCase)
            {
                { "2S", (0.10m, 10m, 15m) },
                { "SL", (0.30m, 100m, 200m) },
                { "3A", (0.30m, 300m, 400m) },
                { "2A", (0.30m, 400m, 500m) },
                { "1A", (0.30m, 400m, 500m) },
                { "CC", (0.30m, 125m, 225m) },
                { "EC", (0.30m, 400m, 500m) }
            };

            switch (quota.ToUpper())
            {
                case "TATKAL":
                    if (tatkalConfig.TryGetValue(cls.Code.ToUpper(), out var config))
                    {
                        decimal tatkalAmount = baseFare * config.percent;
                        quotaChargePerPassenger = Math.Round(Math.Min(Math.Max(tatkalAmount, config.min), config.max), 2);
                    }
                    else
                    {
                        quotaChargePerPassenger = Math.Round(baseFare * 0.30m, 2);
                    }
                    break;

                case "LADIES":
                    quotaChargePerPassenger = Math.Round(baseFare * 0.05m, 2);
                    break;

                case "SC":
                    quotaChargePerPassenger = Math.Round(-baseFare * 0.10m, 2);
                    break;
            }

            totalQuotaCharge = quotaChargePerPassenger * passengerCount;

            //--------------- TOTAL BEFORE FIXED FEES -----------------
            decimal totalBaseFare = baseFare * passengerCount;
            decimal totalBeforeFees = totalBaseFare + totalSurge + totalQuotaCharge;

            //--------------- FIXED FEES (As you requested) -----------------
            convenienceFee = 20.00m;   // Always ₹20
            gst = 3.60m;               // Always ₹3.60
            insurance = 0.45m;         // Always ₹0.45

            //--------------- FINAL FARE -----------------
            decimal finalFare = totalBeforeFees + convenienceFee + gst + insurance;

            return finalFare;
        }

        /*-------------------------------------------------------------------------------------------*/
        private Station GetStationById(int stationId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("GetStationById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@StationId", stationId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Station
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                Code = reader["Code"]?.ToString(),
                                Name = reader["Name"]?.ToString()
                            };
                        }
                    }
                }
            }

            return null;
        }

        //--------------------------------------- CAPTCHA ----------------------------------------//

/*        [DisableRateLimiting]
        [HttpGet("Generate")]
        public IActionResult Generate()
        {
            string captchaText = GenerateRandomText(5);
            HttpContext.Session.SetString("CAPTCHA", captchaText);

            using (Bitmap bmp = new Bitmap(120, 40))
            using (Graphics g = Graphics.FromImage(bmp))
            using (MemoryStream ms = new MemoryStream())
            {
                g.Clear(Color.White);

                using (var font = new System.Drawing.Font("Arial", 20, System.Drawing.FontStyle.Bold))
                {
                    g.DrawString(captchaText, font, Brushes.Black, 10, 5);
                }

                // noise
                Random rnd = new Random();
                for (int i = 0; i < 10; i++)
                {
                    g.DrawLine(Pens.Gray,
                        rnd.Next(120), rnd.Next(40),
                        rnd.Next(120), rnd.Next(40));
                }

                bmp.Save(ms, ImageFormat.Png);
                return File(ms.ToArray(), "image/png");
            }
        }
*/

        //---------------------------------------recently added down---------------------------------------//

        private void AllocateAndInsertPassengers(
            SqlConnection conn,
            int bookingId,
            int trainId,
            int classId,
            DateTime journeyDate,
            List<string> passengerNames,
            List<int> passengerAges,
            List<string> passengerGenders,
            List<string> passengerBerths,
            int seatsCapacity,
            int raccount,
            int racSeats)
        {
            if (passengerNames == null || passengerNames.Count == 0)
                throw new ArgumentException("No passengers provided.", nameof(passengerNames));

            int passengerCount = passengerNames.Count;
            journeyDate = journeyDate.Date;

            using (var tx = conn.BeginTransaction())
            {
                try
                {
                    // 1) Get current CNF/RAC/WL counts
                    int cnfCount = 0, racCount = 0, wlCount = 0;

                    using (var cmd = new SqlCommand("spGetPassengerCounts", conn, tx))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@TrainClassId", classId);
                        cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                cnfCount = rdr["CNFCount"] != DBNull.Value ? Convert.ToInt32(rdr["CNFCount"]) : 0;
                                racCount = rdr["RACCount"] != DBNull.Value ? Convert.ToInt32(rdr["RACCount"]) : 0;
                                wlCount = rdr["WLCount"] != DBNull.Value ? Convert.ToInt32(rdr["WLCount"]) : 0;
                            }
                        }
                    }

                    // 2) Get confirmed seat numbers
                    var occupied = new HashSet<int>();
                    using (var cmd = new SqlCommand("spGetConfirmedSeats", conn, tx))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@ClassId", classId);
                        cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var seatStr = rdr["SeatNumber"]?.ToString();
                                if (string.IsNullOrWhiteSpace(seatStr)) continue;
                                var digits = new string(seatStr.Where(char.IsDigit).ToArray());
                                if (int.TryParse(digits, out int seatNum) && seatNum >= 1 && seatNum <= seatsCapacity)
                                    occupied.Add(seatNum);
                            }
                        }
                    }

                    // 3) Build available seats list
                    var available = Enumerable.Range(1, seatsCapacity)
                                              .Where(n => !occupied.Contains(n))
                                              .OrderBy(n => n)
                                              .ToList();

                    // 4) Allocate CNF seats using contiguous/random logic
                    var possibleStartIndices = new List<int>();
                    for (int i = 0; i <= available.Count - passengerCount; i++)
                    {
                        bool continuous = true;
                        for (int j = 1; j < passengerCount; j++)
                        {
                            if (available[i + j] != available[i] + j)
                            {
                                continuous = false;
                                break;
                            }
                        }
                        if (continuous) possibleStartIndices.Add(i);
                    }

                    List<int> allocatedSeatNumbers;
                    var rng = new Random();
                    if (possibleStartIndices.Count > 0)
                    {
                        int startIndex = possibleStartIndices[rng.Next(possibleStartIndices.Count)];
                        allocatedSeatNumbers = available.Skip(startIndex).Take(passengerCount).ToList();
                    }
                    else
                    {
                        allocatedSeatNumbers = available.Take(passengerCount).ToList();
                    }

                    // 5) Insert passengers with CNF/RAC/WL logic
                    for (int i = 0; i < passengerCount; i++)
                    {
                        string name = passengerNames[i];
                        int age = (passengerAges != null && passengerAges.Count > i) ? passengerAges[i] : 0;
                        string gender = (passengerGenders != null && passengerGenders.Count > i) ? passengerGenders[i] : null;
                        string requestedBerth = (passengerBerths != null && passengerBerths.Count > i && !string.IsNullOrWhiteSpace(passengerBerths[i]))
                                                ? passengerBerths[i] : null;

                        string bookingStatus=null;
                        int? position = null;
                        string seatNumber = null;
                        string berthAssigned = null;

                        if (cnfCount < seatsCapacity && allocatedSeatNumbers.Count > i)
                        {
                            bookingStatus = "CNF";
                            cnfCount++;
                            int seatNum = allocatedSeatNumbers[i];
                            seatNumber = seatNum.ToString();
                            berthAssigned = requestedBerth ?? (seatNum % 3 == 1 ? "LB" : seatNum % 3 == 2 ? "MB" : "UB");
                        }
                        else if (racCount < racSeats)   // FIXED LINE
                        {
                            bookingStatus = "RAC";
                            racCount++;
                            position = racCount;
                        }
                        else
                        {
                            bookingStatus = "WL"; // automatically goes to WL once RAC full
                            wlCount++;
                            position = wlCount;
                        }

                        using (var cmd = new SqlCommand("InsertPassenger", conn, tx))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@BookingId", bookingId);
                            cmd.Parameters.AddWithValue("@Name", name);
                            cmd.Parameters.AddWithValue("@Age", age);
                            cmd.Parameters.AddWithValue("@Gender", gender);
                            cmd.Parameters.AddWithValue("@SeatNumber", (object)seatNumber ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Berth", (object)berthAssigned ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@BookingStatus", bookingStatus);
                            cmd.Parameters.AddWithValue("@Position", (object)position ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }
            }
        }

        private void PromoteQueuesOnSeatFreed(
            SqlConnection conn,
            int trainId,
            int classId,
            DateTime journeyDate,
            string freedSeatPrefix,
            int freedSeatNumeric,
            int seatsFreed)
        {
            for (int s = 0; s < seatsFreed; s++)
            {
                int racPassengerId = 0;

                // 1) Get next RAC passenger
                using (var cmd = new SqlCommand("sp_GetNextRACPassenger", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            racPassengerId = r.GetInt32(0);
                        }
                    }
                }

                // No RAC → nothing to promote
                if (racPassengerId == 0)
                    break;

                // Assign seat
                string seatNumber = $"{freedSeatPrefix}{freedSeatNumeric}";
                string berth = "LB"; // still static but you can change later

                // 2) Promote RAC → CNF
                using (var cmd = new SqlCommand("sp_PromoteRACToCNF", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PassengerId", racPassengerId);
                    cmd.Parameters.AddWithValue("@SeatNumber", seatNumber);
                    cmd.Parameters.AddWithValue("@Berth", berth);
                    cmd.ExecuteNonQuery();
                }

                // 3) Get next WL passenger
                int wlPassengerId = 0;
                using (var cmd = new SqlCommand("sp_GetNextWLPassenger", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            wlPassengerId = r.GetInt32(0);
                        }
                    }
                }

                // No WL → done
                if (wlPassengerId == 0)
                {
                    freedSeatNumeric++;
                    continue;
                }

                // 4) Get next RAC position
                int nextRACPos = 0;
                using (var cmd = new SqlCommand("sp_GetNextRACPosition", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    nextRACPos = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 5) Promote WL → RAC
                using (var cmd = new SqlCommand("sp_PromoteWLToRAC", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PassengerId", wlPassengerId);
                    cmd.Parameters.AddWithValue("@NewPosition", nextRACPos);
                    cmd.ExecuteNonQuery();
                }

                // Next seat number
                freedSeatNumeric++;
            }
        }

        //--------------------------------------recently added up and original down------------------------------//

        // ---------- POST: receive form -> store payload in TempData -> Redirect ----------
        [HttpGet]
        public IActionResult PrepareCheckout(int trainId, int classId, string journeyDate, int FromStationId, int ToStationId, string seatStatus, string FSM, string TSM, string FromStation,string ToStation,int userfromid, int usertoid, string Departure, string Arrival,string Duration,int numPassengers = 1)
        {

            string connStr = _configuration.GetConnectionString("DefaultConnection");
          
            if (numPassengers <= 0) numPassengers = 1;


            // Use seatStatus if needed

            ViewBag.SeatStatus = seatStatus;
            // Save payload in session before redirect
            var payload = new CheckoutPayload
            {
                TrainId = trainId,
                ClassId = classId,
                JourneyDate = journeyDate,
                FromStationId = FromStationId,
                ToStationId = ToStationId,
                FromStation = FromStation,
                FromStation1 = FSM,
                ToStation = ToStation,
                ToStation1 = TSM,
                NumPassengers = numPassengers,
                SeatStatus= seatStatus,
                userfromid= userfromid,
                usertoid= usertoid,
                Departure=Departure,
                Arrival=Arrival,
                Duration=Duration


            };
            HttpContext.Session.SetString("CheckoutPayload", JsonConvert.SerializeObject(payload));


            /*            Train train = Train.GetTrainById(connStr, trainId, classId, journeyDate);*/
            var trains = Train.GetTrains(connStr, FromStationId, ToStationId, journeyDate);
            Train train = trains.FirstOrDefault(t => t.Id == trainId);


            var validation = TrainRouteValidator.Validate(userfromid, usertoid, train, FSM, TSM, FromStation, ToStation);
     
            if (!validation.IsValid)
            {
                TempData["RouteMismatch"] = "YES";
                TempData["ActualFrom"] = validation.ActualFrom;
                TempData["ActualTo"] = validation.ActualTo;
                TempData["SearchedFrom"] = validation.SearchedFrom;
                TempData["SearchedTo"] = validation.SearchedTo;

                //   return RedirectToAction("TrainResults", "Train");

                return RedirectToAction("TrainResults", "Train", new
                {
                    fromStationId = FromStationId,
                    toStationId = ToStationId,
                    FromStation= FromStation,
                    ToStation= ToStation,
                    journeyDateStr = journeyDate
                });
            }

            // Auth check
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account",
                    new { returnUrl = Url.Action("Checkout", "Booking") });
            }

            return RedirectToAction("Checkout", "Booking");

        }

        // ---------- GET: reads payload from TempData and runs DB logic to build view model ----------
        [Authorize]
        [HttpGet]
        public IActionResult Checkout()
        {
            // Get JSON string from session
            var payloadJson = HttpContext.Session.GetString("CheckoutPayload");

            if (string.IsNullOrEmpty(payloadJson))
            {
                TempData["Error"] = "Invalid access. Please search and select a train before checkout.";
                return RedirectToAction("SearchTrain"); // redirect to train search page
            }
            var payload = JsonConvert.DeserializeObject<CheckoutPayload>(payloadJson);

            // Now you can use 'payload' as before
            int trainId = payload.TrainId;
            int classId = payload.ClassId;
            string journeyDate = payload.JourneyDate;
            int FromStationId = payload.FromStationId;
            int ToStationId = payload.ToStationId;
            int numPassengers = payload.NumPassengers;
            int searchedFromId = payload.FromStationId; // the station user searched
            int searchedToId = payload.ToStationId;     // the station user searched

            string SeatStatus = payload.SeatStatus;
            string FromStation = payload.FromStation;
            string ToStation = payload.ToStation;

            string FromStationName1 = payload.FromStation1;
            string ToStationName1 = payload.ToStation1;

            string Arrival = payload.Arrival;
            string Duration = payload.Duration;
            string Departure = payload.Departure;



            string[] parts = SeatStatus.Split('-');
            string numberPart = parts[1].Trim();   // "5"
            string textPart = parts[0].Trim();   // "5"

            int SeatStatuscount = 0;
            int RacSeatStatuscount = 0;
            int WLSeatStatuscount = 0;


            if (textPart == "AVAILABLE")
            {
                SeatStatuscount = int.Parse(numberPart);
            }

          
            if (textPart == "RAC")
            {
                 RacSeatStatuscount = int.Parse(numberPart);

            }
            else if ((textPart == "WL"))
            {

                 WLSeatStatuscount = int.Parse(numberPart);

            }
                

            ViewBag.SeatStatusCount = SeatStatuscount;

            // 1️⃣ Create a booking object with empty passengers
            Booking booking = new Booking();
            booking.Passengers = new List<Passenger>();
            for (int i = 0; i < numPassengers; i++) booking.Passengers.Add(new Passenger());

            Train train = null;
            TrainClass cls = null;

            // ---------- DB: load train + class info ----------
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetTrainForCheckout", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);

                    // If your SP expects DATE type, parse into DateTime
                    if (DateTime.TryParse(journeyDate, out var jd))
                        cmd.Parameters.AddWithValue("@JourneyDate", jd);
                    else
                        cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                Name = reader.GetString(reader.GetOrdinal("TrainName")),
                                Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                Duration = reader.GetString(reader.GetOrdinal("Duration")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")).Date,
                                FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                FromStation = new Station
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("FromStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("FromStationCode"))
                                },
                                ToStation = new Station
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("ToStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("ToStationCode"))
                                }
                            };
                        }

                        if (reader.NextResult() && reader.Read())
                        {
                            cls = new TrainClass
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                TrainId = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                Code = reader.GetString(reader.GetOrdinal("Code")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                SeatsAvailable = reader.GetInt32(reader.GetOrdinal("SeatsAvailable")),
                                RACSeats = reader.GetInt32(reader.GetOrdinal("RACSeats"))
                            };
                        }
                    }
                }
            }

            if (train == null || cls == null)
            {
                TempData["Error"] = "Train or class details not found.";
                return RedirectToAction("Index", "Home");
            }

            // Override route based on user search if needed
            train.FromStationId = FromStationId;
            train.ToStationId = ToStationId;
            train.FromStation = GetStationById(FromStationId);
            train.ToStation = GetStationById(ToStationId);

            // ---------- CNF / RAC / WL logic ----------
            booking.TrainId = trainId;
            booking.TrainClassId = classId;

            var seatStatus = GetSeatStatus(booking.TrainId, booking.TrainClassId);
            int remainingSeats = seatStatus.SeatsAvailable/* - seatStatus.ConfirmedCount*/;
            int racCount = seatStatus.RACCount;
            int wlCount = WLSeatStatuscount;

            foreach (var passenger in booking.Passengers)
            {
                string status;
                int position = 0;

                if (remainingSeats > 0)
                {
                    status = "CNF";
                    passenger.Position = null;
                    remainingSeats--;
                }
                else if (racCount < seatStatus.RACSeats)   // RESTORED ORIGINAL WORKING LOGIC
                {
                    racCount++;
                    status = "RAC";
                    position = racCount;
                    passenger.Position = position;
                }
                else
                {
                    wlCount++;
                    status = "WL";
                    position = wlCount;
                    passenger.Position = position;
                }

                passenger.BookingStatus = status;
                ViewBag.BookingStatus = status;
            }

            // Pass to ViewBag for display
            ViewBag.Train = train;
            ViewBag.Class = cls;
            ViewBag.JourneyDate = journeyDate;
            ViewBag.FromStationId = FromStationId;
            ViewBag.ToStationId = ToStationId;
            ViewBag.Booking = booking; // booking with passengers
            ViewBag.FromStation1 = FromStationName1;
            ViewBag.ToStation1 = ToStationName1;
            ViewBag.FromStation = FromStation;
            ViewBag.ToStation = ToStation;

            ViewBag.Departure = Departure;
            ViewBag.Arrival = Arrival;
            ViewBag.Duration = Duration;

            return View("Checkout", booking);
        }


        [HttpPost]
        public IActionResult RequestOtp(Booking model)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "Not authenticated" });

            string otp = GenerateOTP();
            string hashedOtp = HashOTP(otp);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spRequestOtp", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@OtpHash", hashedOtp);
                    cmd.ExecuteNonQuery();
                }
            }

            // send email after SP execution
            SendOTPEmail(userId, otp);

            return Json(new { success = true });
        }




        [EnableRateLimiting("BookingLimiter")]
        [HttpPost]
        public IActionResult Confirm(
            int trainId,
            int classId,
            string journeyDate,
            List<string> passengerNames,
            List<int> passengerAges,
            List<string> passengerGenders,
            List<string> passengerBerths,
            string trainName,
            string trainNumber,
            string Class,
            int FromStationId,
            int ToStationId,
            int SeatStatuscount,
            string BookingStatus,
            string CaptchaInput,
            string otp = null
            )
        {

            // ================= AUTH =================
            string userid = User.FindFirstValue(ClaimTypes.NameIdentifier); // email
            string username = User.Identity.Name ?? "Passenger";

            // ================= CAPTCHA VERIFICATION (FIRST GATE) =================
            string sessionCaptcha = HttpContext.Session.GetString("CAPTCHA");

            if (string.IsNullOrEmpty(sessionCaptcha) ||
                string.IsNullOrEmpty(CaptchaInput) ||
                !CaptchaInput.Equals(sessionCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Invalid captcha.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

            // OPTIONAL: prevent captcha reuse
            HttpContext.Session.Remove("CAPTCHA");

            // ================= OTP VERIFICATION (MANDATORY) =================
            if (string.IsNullOrEmpty(otp))
            {
                TempData["Error"] = "OTP is required to complete booking.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

            bool isOtpValid = VerifyOTP(userid, otp, "PAYMENT");

            if (!isOtpValid)
            {
                TempData["Error"] = "Invalid or expired OTP.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

            if (string.IsNullOrEmpty(userid))
            {
                TempData["Error"] = "Please log in to continue.";
                return RedirectToAction("Login", "Account");
            }

            // ================= BASIC VALIDATION =================
            int availableCount = SeatStatuscount;
            if (passengerNames == null || passengerNames.Count == 0)
            {
                TempData["Error"] = "Please add at least one passenger.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

            if (passengerAges == null || passengerGenders == null ||
                passengerAges.Count != passengerNames.Count || passengerGenders.Count != passengerNames.Count)
            {
                TempData["Error"] = "Passenger details are incomplete.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

                Station fromStation = null;
                Station toStation = null;



            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Get FROM Station
                using (var cmd = new SqlCommand("GetStationById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@StationId", FromStationId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            fromStation = new Station
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                Code = reader["Code"]?.ToString(),
                                Name = reader["Name"]?.ToString()
                            };
                        }
                    }
                }

                // Get TO Station
                using (var cmd = new SqlCommand("GetStationById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@StationId", ToStationId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            toStation = new Station
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                Code = reader["Code"]?.ToString(),
                                Name = reader["Name"]?.ToString()
                            };
                        }
                    }
                }

                if (fromStation == null || toStation == null || string.IsNullOrEmpty(fromStation.Name) || string.IsNullOrEmpty(toStation.Name))
                {
                    TempData["Error"] = "Invalid station selection.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                ViewBag.FromStation = fromStation;
                ViewBag.ToStation = toStation;
                ViewBag.JourneyDate = journeyDate;

                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["Error"] = "Please log in to continue.";
                    return RedirectToAction("Login", "Account");
                }

                // Get TrainClass info
                TrainClass cls = null;
                using (var cmd = new SqlCommand("GetTrainClassById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@ClassId", classId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            cls = new TrainClass
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Code = reader["Code"].ToString(),
                                BaseFare = Convert.ToDecimal(reader["BaseFare"]),
                                SeatsAvailable = Convert.ToInt32(reader["SeatsAvailable"]),
                                SeatPrefix = reader["SeatPrefix"]?.ToString()
                            };
                        }
                    }
                }

                if (cls == null || string.IsNullOrEmpty(cls.Code))
                {
                    TempData["Error"] = "Train class not found.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                int passengerCount = passengerNames.Count;
                int bookedSeats = GetBookedSeatsCount(trainId, classId);
                string quota = Request.Form["Quota"].FirstOrDefault();
                quota = string.IsNullOrWhiteSpace(quota) ? null : quota.ToUpper();

                // Declare out variables
                decimal gst, surge, quotaCharge, convenienceFee, insurance;

                // Calculate total booking fare (total for passengerCount)
                decimal bookingFinalFare = CalculateFare(
                    cls,
                    cls.SeatsAvailable,
                    bookedSeats,
                    passengerCount,
                    quota,
                    out gst,
                    out surge,
                    out quotaCharge,
                    out convenienceFee,
                    out insurance
                );

                decimal totalBaseFare = cls.BaseFare * passengerCount;
                decimal totalSurge = surge;
                decimal totalQuotaCharge = quotaCharge;
                decimal totalFare = bookingFinalFare;
                string status = BookingStatus;



/*                // ================= OTP GATE =================
                string userid = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userid))
                {
                    TempData["Error"] = "Please log in to continue.";
                    return RedirectToAction("Login", "Account");
                }

                // STEP 1: OTP NOT ENTERED → GENERATE & SEND
                if (string.IsNullOrEmpty(otp))
                {
                    string generatedOtp = GenerateOTP();
                    string hashedOtp = HashOTP(generatedOtp);

                    using (var otpCmd = new SqlCommand(
                        @"INSERT INTO OTPRequests (UserId, OTPHash, Purpose, ExpiryTime)
          VALUES (@UserId, @OTPHash, 'PAYMENT', DATEADD(MINUTE,1,GETDATE()))", conn))
                    {
                        otpCmd.Parameters.AddWithValue("@UserId", userId);
                        otpCmd.Parameters.AddWithValue("@OTPHash", hashedOtp);
                        otpCmd.ExecuteNonQuery();
                    }

                    // Send OTP
                    string useremail = User.FindFirstValue(ClaimTypes.Email);
                    SendOTPEmail(useremail, generatedOtp);

                    TempData["OTP_REQUIRED"] = true;

                    // 🔴 STOP HERE — DO NOT BOOK YET
                    return RedirectToAction("Checkout", new
                    {
                        trainId,
                        classId,
                        journeyDate
                    });
                }

                // STEP 2: OTP ENTERED → VERIFY
                bool isOtpValid = VerifyOTP(userid, otp, "PAYMENT");
                if (!isOtpValid)
                {
                    TempData["Error"] = "Invalid or expired OTP.";
                    TempData["OTP_REQUIRED"] = true;

                    return RedirectToAction("Checkout", new
                    {
                        trainId,
                        classId,
                        journeyDate
                    });
                }
                // ================= OTP VERIFIED =================
*/

                var seatStatus = GetSeatStatus(trainId, classId);

                // Update seats availability
                using (var cmd = new SqlCommand("UpdateTrainClassSeats", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@ClassId", cls.Id);
                    cmd.Parameters.AddWithValue("@Status", BookingStatus);
                    cmd.Parameters.AddWithValue("@Count", passengerCount);
                    cmd.ExecuteNonQuery();
                }

                int bookingId;
                string pnr = GeneratePnr();

                // Insert Booking
                using (var cmd = new SqlCommand("InsertBooking", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PNR", pnr);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainClassId", cls.Id);
                    cmd.Parameters.AddWithValue("@JourneyDate", DateTime.Parse(journeyDate));
                    cmd.Parameters.AddWithValue("@BookingDate", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@BaseFare", totalBaseFare);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@TrainNumber", trainNumber);
                    cmd.Parameters.AddWithValue("@TrainName", trainName);
                    cmd.Parameters.AddWithValue("@Class", Class);
                    cmd.Parameters.AddWithValue("@Frmst", fromStation.Name);
                    cmd.Parameters.AddWithValue("@Tost", toStation.Name);
                    cmd.Parameters.AddWithValue("@Quota", quota);
                    cmd.Parameters.AddWithValue("@GST", gst);
                    cmd.Parameters.AddWithValue("@QuotaCharge", totalQuotaCharge);
                    cmd.Parameters.AddWithValue("@SurgeAmount", totalSurge);
                    cmd.Parameters.AddWithValue("@TotalFare", totalFare);

                    bookingId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // ---------------------------- ALLOCATE SEATS RANDOMLY ----------------------------

                // Before calling AllocateAndInsertPassengers

                // GetSeatStatus should call spGetSeatStatusCounts and return an object with SeatsAvailable and RACSeats


                AllocateAndInsertPassengers(
                    conn,
                    bookingId,
                    trainId,
                    classId,
                    DateTime.Parse(journeyDate),
                    passengerNames,
                    passengerAges,
                    passengerGenders,
                    passengerBerths,
                    seatsCapacity: availableCount,
                    raccount: seatStatus.RACCount,
                    racSeats: seatStatus.RACSeats// this will now be updated inside method,

                );

                // -------------------------------------------------------------------------------

                // Build booking object for email
                Booking booking = new Booking
                {
                    BookingId = bookingId,
                    PNR = pnr,
                    UserId = userId,
                    TrainId = trainId,
                    TrainClassId = cls.Id,
                    JourneyDate = DateTime.Parse(journeyDate),
                    BookingDate = DateTime.UtcNow,
                    BaseFare = cls.BaseFare,
                    Status = status,
                    TrainNumber = int.TryParse(trainNumber, out var tn) ? tn : 0,
                    TrainName = trainName,
                    ClassCode = Class,
                    Frmst = fromStation.Name,
                    Tost = toStation.Name,
                    QuotaCharge = totalQuotaCharge,
                    GST = gst,
                    SurgeAmount = surge,
                    FinalFare = totalBaseFare,
                    TotalFare = totalFare,
                };

                // Build email content
                string userEmail = User.FindFirstValue(ClaimTypes.NameIdentifier); 
                string userName = User.Identity.Name ?? "Passenger";
                var pdfResult = _emailService.GeneratePdf(bookingId);
                if (pdfResult.pdfBytes == null)
                {
                    //handle error
                    return NotFound("PDF generation failed");
                }
                var pdfBytes = pdfResult.pdfBytes;
                string pnrLocal = pdfResult.PNR;
                string body = $@"
                        <h3>Booking Confirmed ✅</h3>
                        <p>Hi {userName},</p>
                        <p>Your booking is confirmed. Details below:</p>
                        <ul>
                            <li><strong>PNR:</strong> {pnrLocal}</li>
                            <li><strong>Train:</strong> {trainName} ({trainNumber})</li>
                            <li><strong>Quota:</strong> {quota}</li>
                            <li><strong>From:</strong> {fromStation?.Name}</li>
                            <li><strong>To:</strong> {toStation?.Name}</li>
                            <li><strong>Date:</strong> {DateTime.Parse(journeyDate):dd-MMM-yyyy}</li>
                            <li><strong>Base Fare per passenger:</strong> ₹{cls.BaseFare}</li>
                            <li><strong>Convenience Fee (Incl. of GST):</strong> ₹{gst + 20.00m}</li>
                            <li><strong>Travel Insurance:</strong> ₹{0.45m}</li>
                            <li><strong>Quota Charge:</strong> ₹{totalQuotaCharge}</li>
                            <li><strong>Surge:</strong> ₹{surge}</li>
                            <li><strong>Passengers:</strong> {passengerCount}</li>
                            <li><strong>Total Fare:</strong> ₹{totalFare}</li>
                        </ul>
                        <p>Your detailed ticket is attached below.</p>
                        <p>Thank you — IRCTC Clone</p>
                    ";

                // Fire-and-forget email
                Task.Run(() =>
                {
                    _emailService.SendEmailWithAttachment(
                        userEmail,
                        "Booking Confirmed ✅",
                        body,
                        pdfBytes,
                        $"E-Ticket_{pnrLocal}.pdf"
                    );
                });

                TempData["BookingSuccess"] = "Ticket booked successfully and sent to your email!";
                return RedirectToAction("Confirmation", new { id = bookingId });
            }
        }


        // GET: /Booking/Confirmation
        [HttpGet]
        public IActionResult Confirmation(int id)
        {
            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetBookingDetails", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // 1️⃣ Read Booking details
                        if (reader.Read())
                        {
                            booking = new Booking()
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                PNR = reader["PNR"].ToString(),
                                UserId = reader["UserId"].ToString(),
                                TrainId = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                TrainNumber = Convert.ToInt32(reader["TrainNumber"]),
                                TrainName = reader["TrainName"].ToString(),
                                TrainClassId = reader.GetInt32(reader.GetOrdinal("TrainClassId")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BookingDate = reader.GetDateTime(reader.GetOrdinal("BookingDate")),
                                BaseFare = reader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(reader["BaseFare"]) : 0,
                                TotalFare = reader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(reader["TotalFare"]) : 0,
                                GST = reader["GST"] != DBNull.Value ? Convert.ToDecimal(reader["GST"]) : 0,
                                QuotaCharge = reader["QuotaCharge"] != DBNull.Value ? Convert.ToDecimal(reader["QuotaCharge"]) : 0,
                                SurgeAmount = reader["SurgeAmount"] != DBNull.Value ? Convert.ToDecimal(reader["SurgeAmount"]) : 0,
                                Quota = reader["Quota"] != DBNull.Value ? reader["Quota"].ToString()?.Trim() : null,
                                Status = reader["Status"].ToString(),
                                ClassCode = reader["ClassCode"].ToString(),
                                SeatPrefix = reader["SeatPrefix"].ToString(),
                                Frmst = reader["Frmst"].ToString(),
                                Tost = reader["Tost"].ToString(),
                                Passengers = new List<Passenger>(),
                                Stations = new List<Station>()
                            };

                        }
                        else
                        {
                            return NotFound();
                        }

                        // 2️⃣ Move to next result set for passengers
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                    Name = reader.GetString(reader.GetOrdinal("Name")),
                                    Age = reader.GetInt32(reader.GetOrdinal("Age")),
                                    Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                    SeatNumber = reader.IsDBNull(reader.GetOrdinal("SeatNumber"))
                                                    ? null
                                                    : reader.GetString(reader.GetOrdinal("SeatNumber")),
                                    Berth = reader.IsDBNull(reader.GetOrdinal("Berth"))
                                                    ? null
                                                    : reader.GetString(reader.GetOrdinal("Berth")),
                                    BookingStatus = reader.GetString(reader.GetOrdinal("BookingStatus")),
                                    Position = reader.IsDBNull(reader.GetOrdinal("Position"))
                                                    ? (int?)null
                                                    : reader.GetInt32(reader.GetOrdinal("Position")),
                                    SeatPrefix = booking.SeatPrefix // or reader if coming from DB
                                });

                            }
                        }

                        // 3️⃣ Stations
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Stations.Add(new Station
                                {
                                    Code = reader["Code"]?.ToString(),
                                    Name = reader["Name"]?.ToString()
                                });
                            }
                        }

                    }
                }
            }

            return View(booking);
        }

        [HttpGet]
        public IActionResult History()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var bookings = new List<Booking>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetUserBookingHistory", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            bookings.Add(new Booking
                            {
                                BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode"))
                            });
                        }
                    }
                }
            }

            return View(bookings);
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            Booking booking = null;
            var passengers = new List<Passenger>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetBookingDtls", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // 1️⃣ Booking data
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                TrainId = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                TrainClassId = reader.GetInt32(reader.GetOrdinal("TrainClassId")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                Quota = reader.GetString(reader.GetOrdinal("Quota")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
                                Frmst = reader.GetString(reader.GetOrdinal("Frmst")),
                                Tost = reader.GetString(reader.GetOrdinal("Tost")),
                                SeatPrefix = reader.GetString(reader.GetOrdinal("SeatPrefix"))
                            };
                        }
                        else return NotFound();

                        // 2️⃣ Passenger data
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                passengers.Add(new Passenger
                                {
                                    Id= reader.GetInt32(reader.GetOrdinal("Id")),
                                    Name = reader.GetString(reader.GetOrdinal("Name")),
                                    Age = reader.GetInt32(reader.GetOrdinal("Age")),
                                    Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                    SeatNumber = reader.IsDBNull(reader.GetOrdinal("SeatNumber")) ? null : reader.GetString(reader.GetOrdinal("SeatNumber")),
                                    Berth = reader.IsDBNull(reader.GetOrdinal("Berth")) ? null : reader.GetString(reader.GetOrdinal("Berth")),

                                    BookingStatus = reader.IsDBNull(reader.GetOrdinal("BookingStatus"))
                                                        ? null
                                                        : reader.GetString(reader.GetOrdinal("BookingStatus")),

                                    Position = reader.IsDBNull(reader.GetOrdinal("Position"))
                                                        ? (int?)null
                                                        : reader.GetInt32(reader.GetOrdinal("Position")),

                                    SeatPrefix = booking.SeatPrefix
                                });
                            }
                        }
                    }
                }
            }

            booking.Passengers = passengers;
            return View(booking);
        }

        [HttpPost]
        public IActionResult DeletePassenger(int id)
        {
            bool isDeleted = false;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Delete passenger
                using (SqlCommand cmd = new SqlCommand("spDeletePassengers", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Id", id);
                    int rows = cmd.ExecuteNonQuery();
                    isDeleted = rows > 0;
                }

                if (isDeleted)
                {
                    // Recalculate + Shift
                    using (SqlCommand cmd2 = new SqlCommand("spRecalculateAvailability", conn))
                    {
                        cmd2.CommandType = CommandType.StoredProcedure;
                        cmd2.Parameters.AddWithValue("@PassengerId", id);
                        cmd2.ExecuteNonQuery();
                    }
                }
            }

            return Json(new { success = isDeleted });
        }

        [HttpGet]
        public IActionResult MyBookings()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var bookings = new List<Booking>();
            var passengerLookup = new Dictionary<int, List<Passenger>>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetUserBookingsWithPassengers", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // First result set: bookings
                        while (reader.Read())
                        {
                            bookings.Add(new Booking
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                Train = new Train
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                    Number = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                    Name = reader.GetString(reader.GetOrdinal("TrainName"))
                                },
                                TrainClass = new TrainClass
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                    Code = reader.GetString(reader.GetOrdinal("ClassCode"))
                                },
                                Passengers = new List<Passenger>()
                            });
                        }

                        // Second result set: passengers
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                int bookingId = reader.GetInt32(reader.GetOrdinal("BookingId"));
                                var passenger = new Passenger
                                {
                                    Name = reader.GetString(reader.GetOrdinal("Name")),
                                    Age = reader.GetInt32(reader.GetOrdinal("Age")),
                                    Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                    SeatNumber = reader.GetString(reader.GetOrdinal("SeatNumber"))
                                };

                                if (!passengerLookup.ContainsKey(bookingId))
                                    passengerLookup[bookingId] = new List<Passenger>();

                                passengerLookup[bookingId].Add(passenger);
                            }
                        }
                    }
                }
            }

            // Assign passengers to bookings
            foreach (var booking in bookings)
            {
                if (passengerLookup.TryGetValue(booking.Id, out var list))
                    booking.Passengers = list;
            }

            return View(bookings);
        }

        [HttpPost]
        public IActionResult Cancel(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spCancelBooking", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BookingId", id);
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["Message"] = "Booking cancelled successfully!";
            }
            catch (SqlException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("History");
        }

        [HttpGet]
        public IActionResult DownloadTicket(int id)
        {
            var result = _emailService.GeneratePdf(id);

            if (result.pdfBytes == null)
                return NotFound("Booking not found");

            return File(result.pdfBytes, "application/pdf", $"{result.PNR}.pdf");
        }


        private Booking GetBookingDetails(int bookingId)
        {
            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetBookingDetails", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // 1️⃣ Booking
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                UserId = reader["UserId"]?.ToString(),
                                PNR = reader["PNR"]?.ToString(),
                                TrainName = reader["TrainName"]?.ToString(),
                                TrainNumber = Convert.ToInt32(reader["TrainNumber"]),
                                Frmst = reader["Frmst"]?.ToString(),
                                Tost = reader["Tost"]?.ToString(),
                                JourneyDate = Convert.ToDateTime(reader["JourneyDate"]),
                                BookingDate = Convert.ToDateTime(reader["BookingDate"]),
                                Status = reader["Status"]?.ToString(),
                                ClassCode = reader["ClassCode"]?.ToString(),
                                Quota = reader["Quota"]?.ToString(),
                                BaseFare = Convert.ToDecimal(reader["BaseFare"]),
                                GST = Convert.ToDecimal(reader["GST"]),
                                SurgeAmount = Convert.ToDecimal(reader["SurgeAmount"]),
                                TotalFare = Convert.ToDecimal(reader["TotalFare"]),
                                QuotaCharge = Convert.ToDecimal(reader["QuotaCharge"]),
                                Passengers = new List<Passenger>(),
                                Stations = new List<Station>()
                            };
                        }

                        // 2️⃣ Passengers
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
                                    Name = reader["Name"]?.ToString(),
                                    Age = Convert.ToInt32(reader["Age"]),
                                    Gender = reader["Gender"]?.ToString(),
                                    SeatNumber = reader["SeatNumber"]?.ToString(),
                                    Berth = reader["Berth"]?.ToString(),
                                    BookingStatus = reader["BookingStatus"]?.ToString(),
                                    Position = reader["Position"] as int?
                                });
                            }
                        }

                        // 3️⃣ Stations
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Stations.Add(new Station
                                {
                                    Code = reader["Code"]?.ToString(),
                                    Name = reader["Name"]?.ToString()
                                });
                            }
                        }
                    }
                }
            }

            return booking;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult CheckPNR()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult CheckPNR(string pnr)
        {
            if (string.IsNullOrEmpty(pnr))
            {
                ViewBag.Error = "Please enter a valid PNR number.";
                return View();
            }

            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetPNRDetails", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PNR", pnr);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                Frmst = reader.GetString(reader.GetOrdinal("Frmst")),
                                Tost = reader.GetString(reader.GetOrdinal("Tost")),
                                SeatPrefix = reader.GetString(reader.GetOrdinal("SeatPrefix")),
                                Passengers = new List<Passenger>()
                            };
                        }

                        // Move to next result
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
                                    Name = reader.GetString(reader.GetOrdinal("Name")),
                                    Age = reader.GetInt32(reader.GetOrdinal("Age")),
                                    Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                    SeatNumber = reader.IsDBNull(reader.GetOrdinal("SeatNumber"))
                                                  ? ""
                                                  : reader.GetString(reader.GetOrdinal("SeatNumber")),

                                    Berth = reader.IsDBNull(reader.GetOrdinal("Berth"))
                                          ? ""
                                          : reader.GetString(reader.GetOrdinal("Berth")),
                                    BookingStatus = reader.GetString(reader.GetOrdinal("Bookingstatus")),
                                    Position = reader.IsDBNull(reader.GetOrdinal("Position"))
                                          ? 0
                                          : reader.GetInt32(reader.GetOrdinal("Position")),
                                    SeatPrefix = booking.SeatPrefix
                                });


                               

                    
                           

                            }
                        }
                    }
                }
            }

            if (booking == null)
            {
                ViewBag.Error = " ❌ PNR Flushed / Booking Cancelled. ";
                    /*"⚠️ Invalid PNR. Please check and try again."*/
                return View();
            }

/*            if (booking.Status == "CANCELLED")
            {
                ViewBag.Error = "❌ PNR Flushed / Booking Cancelled.";
                return View();
            }*/

            return View("PNRDetails", booking);
        }

        [HttpGet]
        public async Task<IActionResult> GetNext7Days(int trainId, string travelClass)
        {
            var data = await _availabilityService.GetNext7DaysAsync(trainId, travelClass);
            return Json(data);
        }


/*        public IActionResult ConfirmRoute(int trainId, int classId, string journeyDate)
        {
            string connStr = _configuration.GetConnectionString("DefaultConnection");

            var payloadJson = HttpContext.Session.GetString("CheckoutPayload");
            if (string.IsNullOrEmpty(payloadJson))
            {
                TempData["Error"] = "Invalid access. Please search and select a train before checkout.";
                return RedirectToAction("SearchTrain");
            }

            var payload = JsonConvert.DeserializeObject<CheckoutPayload>(payloadJson);
            int searchedFromId = payload.FromStationId;
            int searchedToId = payload.ToStationId;

            Train train = Train.GetTrainById(connStr, trainId, classId, journeyDate);
            if (train == null)
            {
                TempData["Error"] = "Selected train not found.";
                return RedirectToAction("SearchTrain");
            }

            try
            {
                // Will throw if searched route does not match train's actual route
                TrainRouteValidator.ValidateTrainRoute(searchedFromId, searchedToId, train, connStr);
            }
            catch (InvalidOperationException ex)
            {
                TempData["RouteWarning"] = ex.Message;
                return RedirectToAction("ConfirmRoutePage", new { trainId });
            }

            return RedirectToAction("Checkout", new { trainId });
        }
*/

        [HttpPost]
        [Authorize]
        public IActionResult ProceedWithRoute(int trainId)
        {
            // User confirmed to continue despite route mismatch
            return RedirectToAction("Checkout", new { trainId });
        }


        /*------------------------------pnr and otp generation-----------------------*/
        //pnr generation
        private string GeneratePnr()
        {
            var rand = new Random();

            // First digit 4
            int firstDigit = 4;

            // Remaining 9 digits between 0–9
            string restDigits = new string(Enumerable.Range(0, 9)
                .Select(_ => (char)('0' + rand.Next(10)))
                .ToArray());

            return firstDigit + restDigits;
        }


        //otp generation
        public static string GenerateOTP()
        {
            Random rnd = new Random();
            return rnd.Next(100000, 999999).ToString(); // 6-digit
        }

        public static string HashOTP(string otp)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(otp));
            return Convert.ToBase64String(bytes);
        }

/*        private string GenerateRandomText(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Random rnd = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[rnd.Next(s.Length)]).ToArray());
        }
*/
    }






}
