using IrctcClone.Helpers;
using IrctcClone.Infrastructure.Messaging;
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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
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
using System.Threading.Tasks;
using static Org.BouncyCastle.Bcpg.Attr.ImageAttrib;
using static System.Net.Mime.MediaTypeNames;
using Font = iTextSharp.text.Font;
using Image = iTextSharp.text.Image;
using System.Text.Json;
using RabbitMQ.Client;

namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]
    [Authorize]
    public class BookingController : Controller
    {
        private readonly string _connectionString;
        private readonly EmailService _emailService;
        private readonly IAvailabilityService _availabilityService;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<TrainHub> _hub;
        private readonly IBookingService _bookingService;
        private readonly FareService _fareService;
        private readonly PassengerAllocationService _allocationService;
        private readonly RabbitMqPublisher _publisher;

        public BookingController(
            IConfiguration configuration,
            EmailService emailService,
            IHubContext<TrainHub> hub,
            IBookingService bookingService,
            FareService fareService,
            PassengerAllocationService allocationService,
            RabbitMqPublisher publisher)
        {
            _connectionString =
                configuration.GetConnectionString("DefaultConnection");

            _emailService = emailService;

            _configuration = configuration;

            _hub = hub;

            _bookingService = bookingService;

            _fareService = fareService;

            _allocationService = allocationService;

            _publisher = publisher;
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
        private async Task SendOTPEmail(string userEmail, string otp)
        {
            string subject = "IRCTC Clone – OTP Verification";
            string body = $@"
                <h3>OTP Verification</h3>
                <p>Your One-Time Password (OTP) is:</p>
                <h2>{otp}</h2>
                <p>This OTP is valid for <strong>1 minute</strong>.</p>
                <p>If you did not initiate this request, please ignore this email.</p>";

            await _emailService.SendEmail(
                userEmail,
                subject,
                body
            );
        }


        //-------------------------------------------- REQUEST OTP -------------------------------------//
        [HttpPost]
        public async Task<IActionResult> RequestOtp(Booking model)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "UserId NULL" });

                string otp = GenerateOTP();
                string hashedOtp = HashOTP(otp);
                string purpose = "PAYMENT";

                DateTime expiryTime;

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("spRequestOtp", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@OtpHash", hashedOtp);
                        cmd.Parameters.AddWithValue("@Purpose", purpose);

                        var expiryParam = new SqlParameter("@ExpiryTime", SqlDbType.DateTime)
                        {
                            Direction = ParameterDirection.Output
                        };

                        cmd.Parameters.Add(expiryParam);

                        cmd.ExecuteNonQuery();

                        expiryTime = (DateTime)expiryParam.Value;
                        if (expiryTime.Kind == DateTimeKind.Unspecified)
                        {
                            if (Math.Abs((expiryTime - DateTime.UtcNow).TotalMinutes) < 30)
                            {
                                expiryTime = DateTime.SpecifyKind(expiryTime, DateTimeKind.Utc);
                            }
                            else
                            {
                                expiryTime = DateTime.SpecifyKind(expiryTime, DateTimeKind.Local);
                            }
                        }
                    }
                }

                await SendOTPEmail(userId, otp);

                //TempData["DebugOtp"] = otp;

                return Json(new { success = true, otpExpiryUtc = expiryTime.ToString("o") });

                //return Json(new
                //{
                //    success = true,
                //    otp = otp,
                //    otpExpiryUtc = expiryTime.ToString("o")
                //});
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpPost]
        public IActionResult VerifyOtp(string otp)
        {
            try
            {
                // TEMP FIX
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new
                    {
                        success = false,
                        message = "User session expired"
                    });
                }

                bool isValid =
                    VerifyOTP(userId, otp, "PAYMENT");

                if (isValid)
                {
                    HttpContext.Session.SetString("OtpVerified", "true");
                }

                return Json(new
                {
                    success = isValid,
                    message = isValid
                        ? "OTP verified"
                        : "Invalid OTP"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
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
                    cmd.Parameters.AddWithValue("@Purpose", purpose);

                    var isValidParam = new SqlParameter("@IsValid", SqlDbType.Bit) 
                    { 
                        Direction = ParameterDirection.Output 
                    };

                    cmd.Parameters.Add(isValidParam);

                    cmd.ExecuteNonQuery();

                    return isValidParam.Value != DBNull.Value && (bool)isValidParam.Value;
                }
            }

        }

        /*        [HttpPost]
                public IActionResult AuthenticateOtp(string userid, string otp)
                {
                    if (!VerifyOTP(userid, otp, "PAYMENT"))
                    {
                        return Json(new { success = false, message = "Invalid or expired OTP." });
                    }

                    return Json(new { success = true });
                }
        */


        /*---------------------------------GETS SEATS STATUS------------------------------*/
        private SeatStatus GetSeatStatus(int trainId, int classId, DateTime journeyDate, string quota)
        {
            if (journeyDate == DateTime.MinValue)
            {
                return null;
            }

            var seatStatus = new SeatStatus();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Standardize quota
                string targetQuota = string.IsNullOrWhiteSpace(quota) ? "GENERAL" : quota.ToUpper().Trim();
                if (targetQuota == "PT") targetQuota = "PREMIUM TATKAL";
                if (targetQuota == "SS") targetQuota = "SENIOR";

                // A. Get total seats in this quota for the train class
                int totalQuotaSeats = 0;

                using (var qCmd = new SqlCommand("spGetTotalQuotaSeats", conn))
                {
                    qCmd.CommandType = CommandType.StoredProcedure;

                    qCmd.Parameters.AddWithValue("@TrainId", trainId);
                    qCmd.Parameters.AddWithValue("@ClassId", classId);
                    qCmd.Parameters.AddWithValue("@Quota", targetQuota);

                    var qRes = qCmd.ExecuteScalar();
                    totalQuotaSeats = (qRes != null && qRes != DBNull.Value) ? Convert.ToInt32(qRes) : 0;
                }

                // B. Get confirmed passengers for this class, date, and quota
                int cnfQuotaCount = 0;

                using (var cCmd = new SqlCommand("spGetConfirmedQuotaCount", conn))
                {
                    cCmd.CommandType = CommandType.StoredProcedure;

                    cCmd.Parameters.AddWithValue("@TrainId", trainId);
                    cCmd.Parameters.AddWithValue("@ClassId", classId);
                    cCmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                    cCmd.Parameters.AddWithValue("@Quota", targetQuota);

                    var cRes = cCmd.ExecuteScalar();
                    cnfQuotaCount = (cRes != null && cRes != DBNull.Value) ? Convert.ToInt32(cRes) : 0;
                }

                // If coaches are seeded, calculate strictly based on Quota range!
                if (totalQuotaSeats > 0)
                {
                    seatStatus.SeatsAvailable = Math.Max(totalQuotaSeats - cnfQuotaCount, 0);
                }
                else
                {
                    // Fallback to legacy SP if coaches not seeded yet
                    using (var cmd = new SqlCommand("spGetSeatStatusCounts", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);
                        cmd.Parameters.AddWithValue("@ClassId", classId);
                        cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);
                        cmd.Parameters.AddWithValue("@Quota", quota);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                return new SeatStatus
                                {
                                    SeatsAvailable = (rdr["SeatsAvailable"] != null && rdr["SeatsAvailable"] != DBNull.Value) ? Convert.ToInt32(rdr["SeatsAvailable"]) : 0,
                                    RACSeats = (rdr["RACSeats"] != null && rdr["RACSeats"] != DBNull.Value) ? Convert.ToInt32(rdr["RACSeats"]) : 0,
                                    ConfirmedCount = (rdr["ConfirmedCount"] != null && rdr["ConfirmedCount"] != DBNull.Value) ? Convert.ToInt32(rdr["ConfirmedCount"]) : 0,
                                    RACCount = (rdr["RACCount"] != null && rdr["RACCount"] != DBNull.Value) ? Convert.ToInt32(rdr["RACCount"]) : 0,
                                    WLCount = (rdr["WLCount"] != null && rdr["WLCount"] != DBNull.Value) ? Convert.ToInt32(rdr["WLCount"]) : 0,
                                    TatkalSeats = (rdr["TatkalSeats"] != null && rdr["TatkalSeats"] != DBNull.Value) ? Convert.ToInt32(rdr["TatkalSeats"]) : 0
                                };
                            }
                        }
                    }
                }

                // Fill other properties to avoid null/exceptions
                seatStatus.RACSeats = 10; // static fallback
                seatStatus.WLCount = 0;

                // Get Waitlist count if booked
                using (var wlCmd = new SqlCommand("spGetWaitlistCount", conn))
                {
                    wlCmd.CommandType = CommandType.StoredProcedure;

                    wlCmd.Parameters.AddWithValue("@TrainId", trainId);
                    wlCmd.Parameters.AddWithValue("@ClassId", classId);
                    wlCmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                    wlCmd.Parameters.AddWithValue("@Quota", targetQuota);

                    var wlRes = wlCmd.ExecuteScalar();
                    seatStatus.WLCount = (wlRes != null && wlRes != DBNull.Value) ? Convert.ToInt32(wlRes) : 0;
                }

                return seatStatus;
            }
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

        //--------------------------------------recently added up and original down------------------------------//

        // ---------- POST: receive form -> store payload in TempData -> Redirect ----------
        [HttpGet]
        public IActionResult PrepareCheckout(
            int trainId, 
            int classId, 
            string journeyDate,
            string classCode,
            string quota,
            string trainName,
            int FromStationId, 
            int ToStationId, 
            string seatStatus, 
            string FSM, 
            string TSM, 
            string FromStation, 
            string ToStation, 
            int userfromid, 
            int usertoid, 
            string Departure, 
            string Arrival, 
            string Duration, 
            string fare,
            int numPassengers = 1)
        {  
            try
            {
                string connStr = _configuration.GetConnectionString("DefaultConnection");

                if (numPassengers <= 0) numPassengers = 1;


                // Use seatStatus if needed

                ViewBag.SeatStatus = seatStatus;
                ViewBag.classCode = classCode;

                // Save payload in session before redirect
                var payload = new CheckoutPayload
                {
                    TrainId = trainId,
                    ClassId = classId,
                    JourneyDate = journeyDate,
                    ClassCode = classCode,
                    Quota = quota,
                    TrainName = trainName,
                    FromStationId = FromStationId,
                    ToStationId = ToStationId,
                    FromStation = FromStation,
                    FromStation1 = FSM,
                    ToStation = ToStation,
                    ToStation1 = TSM,
                    NumPassengers = numPassengers,
                    SeatStatus = seatStatus,
                    userfromid = userfromid,
                    usertoid = usertoid,
                    Departure = Departure,
                    Arrival = Arrival,
                    Duration = Duration,
                    diffare = fare,
                };

                HttpContext.Session.SetString("CheckoutPayload", JsonConvert.SerializeObject(payload));

                // ⚡ Tatkal single ticket per session restriction
                bool isTatkalWindowOrQuota = (quota?.ToUpper() == "TATKAL" || TatkalHelper.IsTatkalWindow() || TatkalHelper.IsTatkalExclusiveWindow());
                if (isTatkalWindowOrQuota && HttpContext.Session.GetString("HasBookedTatkalInSession") == "true")
                {
                    TempData["Error"] = "As per IRCTC guidelines, only 1 ticket can be booked per user login during Tatkal hours. Please logout and login again to book another ticket.";
                    return RedirectToAction("TrainResults", "Train");
                }

                var trains = Train.GetTrains(connStr, FromStationId, ToStationId, journeyDate, quota);
                Train train = trains.FirstOrDefault(t => t.Id == trainId);
                if (train == null)
                {
                    train = Train.GetTrainById(connStr, trainId, classId, journeyDate);
                }

                if (train != null)
                {
                    if (!train.IsBookingAllowed)
                    {
                        TempData["Error"] = "BOOKINGs are NOT ALLOWED at this TIME";
                        return RedirectToAction("TrainResults", "Train");
                    }

                    var validation = TrainRouteValidator.Validate(userfromid, usertoid, train, FSM, TSM, FromStation, ToStation);

                    if (!validation.IsValid)
                    {
                        TempData["RouteMismatch"] = true;
                        TempData["ActualFrom"] = validation.ActualFrom;
                        TempData["ActualTo"] = validation.ActualTo;
                        TempData["SearchedFrom"] = validation.SearchedFrom;
                        TempData["SearchedTo"] = validation.SearchedTo;
                        TempData["ActualFromId"] = validation.ActualFromId;
                        TempData["ActualToId"] = validation.ActualToId;

                        // Keep original searched station IDs so search state is not corrupted
                        TempData["fromStationId"] = userfromid;
                        TempData["toStationId"] = usertoid;
                        TempData["FromStation"] = FromStation;
                        TempData["ToStation"] = ToStation;
                        TempData["JourneyDate"] = journeyDate;

                        TempData["trainId"] = trainId;
                        TempData["classId"] = classId;
                        TempData["classCode"] = classCode;
                        TempData["journeyDate"] = journeyDate;

                        return RedirectToAction("TrainResults", "Train");
                    }
                }

                // Auth check
                if (!User.Identity.IsAuthenticated)
                {
                    return RedirectToAction("Login", "Account",
                        new { returnUrl = Url.Action("Checkout", "Booking") });
                }

                return RedirectToAction("Checkout", "Booking", new
                {
                    FromStationId = FromStationId,
                    ToStationId = ToStationId
                });

            }

            catch (Exception ex)
            {
                TempData["Error"] = "Failed to prepare checkout: " + ex.Message;
                return RedirectToAction("Index", "Home");
            }

        }

        // ---------- GET: reads payload from TempData and runs DB logic to build view model ----------
        [Authorize]
        [HttpGet]
        public IActionResult Checkout(int? FromStationId, int? ToStationId)
        {
            try
            {
                if (FromStationId == null || ToStationId == null)
                {
                    return RedirectToAction("TrainResults", "Train");
                }

                // Get JSON string from session
                var payloadJson = HttpContext.Session.GetString("CheckoutPayload");

                if (string.IsNullOrEmpty(payloadJson))
                {
                    TempData["Error"] = "Session Expired. Please search and select a train again.";

                    return RedirectToAction("Index", "Home"); // redirect to train search page
                }

                var payload = JsonConvert.DeserializeObject<CheckoutPayload>(payloadJson);

                // ⚡ Tatkal single ticket check
                string q = payload?.Quota ?? "";
                bool isTatkalWindowOrQuota = (q.ToUpper() == "TATKAL" || TatkalHelper.IsTatkalWindow() || TatkalHelper.IsTatkalExclusiveWindow());
                if (isTatkalWindowOrQuota && HttpContext.Session.GetString("HasBookedTatkalInSession") == "true")
                {
                    TempData["Error"] = "As per IRCTC guidelines, only 1 ticket can be booked per user login during Tatkal hours. Please logout and login again to book another ticket.";
                    return RedirectToAction("TrainResults", "Train");
                }

                // override station ids if modal sent new ones
                if (FromStationId.HasValue)
                {
                    payload.FromStationId = FromStationId.Value;
                }

                if (ToStationId.HasValue)
                {
                    payload.ToStationId = ToStationId.Value;
                }

                // Now you can use 'payload' as before
                int trainId = payload.TrainId;
                int classId = payload.ClassId;
                string journeyDate = payload.JourneyDate;
                int fromStationId = payload.FromStationId;
                int toStationId = payload.ToStationId;
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

                string Quota = payload.Quota;

                string diffare = payload.diffare;

                string textPart = "";
                int numberPart = 0;

                if (!string.IsNullOrEmpty(SeatStatus) && SeatStatus.Contains("-"))
                {
                    string[] parts = SeatStatus.Split('-');

                    if (parts.Length > 1)
                    {
                        textPart = parts[0].Trim();

                        int.TryParse(parts[1].Trim(), out numberPart);
                    }
                }
                else
                {
                    textPart = SeatStatus.Trim(); // e.g. "NOT AVAILABLE"
                }

                int SeatStatuscount = 0;
                int RacSeatStatuscount = 0;
                int WLSeatStatuscount = 0;


                if (textPart == "AVAILABLE" || textPart == "CURR_AVBL")
                {
                    SeatStatuscount = numberPart;
                }


                if (textPart == "RAC")
                {
                    RacSeatStatuscount = numberPart;

                }
                else if ((textPart == "WL"))
                {

                    WLSeatStatuscount = numberPart;

                }


                ViewBag.SeatStatusCount = SeatStatuscount;
                ViewBag.SeatStatus = SeatStatus;

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

                        DateTime journeyDateParsed;

                        if (!DateTime.TryParse(journeyDate, out journeyDateParsed))
                        {
                            journeyDateParsed = DateTime.Today;
                        }

                        cmd.Parameters.Add("@JourneyDate", SqlDbType.Date).Value = journeyDateParsed;

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
                                    SeatsAvailable = reader.GetInt32(reader.GetOrdinal("SeatsAvailable")),
                                    RACSeats = reader.GetInt32(reader.GetOrdinal("RACSeats"))
                                };

                                string classCode = cls.Code;

                                if (classCode.Contains("(") && classCode.Contains(")"))
                                {
                                    int start = classCode.IndexOf("(") + 1;
                                    int end = classCode.IndexOf(")");
                                    classCode = classCode.Substring(start, end - start);
                                }

                                decimal fare = 0;

                                using (var cmd2 = new SqlCommand("spCalculateFare", conn))
                                {
                                    cmd2.CommandType = CommandType.StoredProcedure;

                                    cmd2.Parameters.AddWithValue("@TrainId", trainId);
                                    cmd2.Parameters.AddWithValue("@FromStationId", FromStationId);
                                    cmd2.Parameters.AddWithValue("@ToStationId", ToStationId);
                                    cmd2.Parameters.AddWithValue("@ClassCode", classCode);
                                    cmd2.Parameters.AddWithValue("@IsSuperfast", 1);   // ADD THIS

                                    using (var reader2 = cmd2.ExecuteReader())
                                    {
                                        if (reader2.Read())
                                        {
                                            fare = reader2["Fare"] == DBNull.Value ? 0 : Convert.ToDecimal(reader2["Fare"]);
                                        }
                                    }
                                }

                                cls.TotalFare = fare;

                                if (fare == 0)
                                {
                                    if (decimal.TryParse(payload.diffare, out decimal parsedFare))
                                    {
                                        fare = parsedFare;
                                        cls.TotalFare = fare; // assign parsed value to BaseFare
                                    }
                                }

                            }
                        }
                    }
                }

                if (train == null || cls == null)
                {
                    TempData["Error"] = "Train or class details not found.";
                    return RedirectToAction("Index", "Home");
                }

                // 🔥 Check 8-Hour Charting Rule
                var indiaTimeCheck = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                    DateTime.UtcNow,
                    "India Standard Time"
                );
                DateTime depDateParsed = DateTime.TryParse(journeyDate, out var jParsed) ? jParsed.Date : DateTime.Today;
                DateTime departureDateTime = depDateParsed + train.Departure;
                TimeSpan timeToDeparture = departureDateTime - indiaTimeCheck;

                if (timeToDeparture.TotalSeconds <= 0)
                {
                    TempData["Error"] = "Booking closed. This train has already departed.";
                    return RedirectToAction("TrainResults", "Train");
                }

                if (timeToDeparture.TotalMinutes <= 30)
                {
                    TempData["Error"] = "Booking closed. Final charting has already been prepared for this train.";
                    return RedirectToAction("TrainResults", "Train");
                }

                // 🔥 OVERRIDE DB VALUES WITH USER-SELECTED VALUES

                if (!string.IsNullOrEmpty(payload.Departure))
                {
                    train.Departure = TimeSpan.Parse(payload.Departure);
                }

                if (!string.IsNullOrEmpty(payload.Arrival))
                {
                    train.Arrival = TimeSpan.Parse(payload.Arrival);
                }

                if (!string.IsNullOrEmpty(payload.Duration))
                {
                    train.Duration = payload.Duration;
                }

                // Override station names also
                if (!string.IsNullOrEmpty(payload.FromStation))
                {
                    train.FromStation.Name = payload.FromStation;
                }

                if (!string.IsNullOrEmpty(payload.ToStation))
                {
                    train.ToStation.Name = payload.ToStation;
                }

                // Override route based on user search if needed
                train.FromStationId = fromStationId;
                train.ToStationId = toStationId;
                train.FromStation = _bookingService.GetStationById(fromStationId);
                train.ToStation = _bookingService.GetStationById(toStationId);

                // ---------- CNF / RAC / WL logic ----------
                booking.TrainId = trainId;
                booking.TrainClassId = classId;
                booking.JourneyDate = DateTime.TryParse(journeyDate, out var jd)
                    ? jd
                    : DateTime.Today;
                booking.Quota = Quota;
                var seatStatus = _bookingService.GetSeatStatus(booking.TrainId, booking.TrainClassId, booking.JourneyDate, booking.Quota);
                //int remainingSeats = seatStatus.SeatsAvailable - seatStatus.ConfirmedCount;

                int remainingSeats;

                if (Quota == "TATKAL")
                {
                    remainingSeats = seatStatus.TatkalSeats;
                }
                else
                {
                    remainingSeats = seatStatus.SeatsAvailable;
                }

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
                    else
                    {
                        if (Quota == "TATKAL")
                        {
                            wlCount++;
                            status = "WL";
                            position = wlCount;
                            passenger.Position = position;
                        }
                        else
                        {
                            if (seatStatus.RACSeats >= 1)
                            {
                                racCount++;
                                status = "RAC";
                                position = racCount;
                                passenger.Position = position;
                            }
                            else
                            {
                                // wlCount++;
                                status = "WL";
                                position = wlCount;
                                passenger.Position = position;
                            }
                        }
                    }
                    /*                else if (racCount < seatStatus.RACSeats)   // RESTORED ORIGINAL WORKING LOGIC
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
                    */
                    passenger.BookingStatus = status;
                    ViewBag.BookingStatus = status;
                }

                bool isFirstAc = (cls?.Code ?? "").Contains("1A") || (cls?.Code ?? "").Contains("EA");
                if (Quota == "TATKAL" && isFirstAc)
                {
                    bool isTatkalOpen = TatkalHelper.IsTatkalOpen("1A");
                    ViewBag.SeatStatus = isTatkalOpen ? "NOT AVAILABLE" : "NOT AVAILABLE#";
                }
                else if (string.IsNullOrWhiteSpace(ViewBag.SeatStatus as string))
                {
                    ViewBag.SeatStatus = remainingSeats > 0 ? $"AVAILABLE - {remainingSeats}" : (racCount > 0 ? $"RAC - {racCount}" : (wlCount > 0 ? $"WL - {wlCount}" : "AVAILABLE"));
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
                ViewBag.Quota = Quota;

                // LOAD TRAIN ROUTE STOPS
                var routeStops = new List<dynamic>();

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spGetFullTrainRouteForBooking", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@TrainId", trainId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                routeStops.Add(new
                                {
                                    StopNumber = reader["StopNumber"],
                                    StationName = reader["StationName"]?.ToString(),
                                    ArrivalTime = reader["ArrivalTime"]?.ToString(),
                                    DepartureTime = reader["DepartureTime"]?.ToString(),
                                    DayNumber = reader["Day"],
                                    Distance = reader["DistanceFromSource"]
                                });
                            }
                        }
                    }
                }

                // Find selected boarding station
                var boardingIndex = routeStops.FindIndex(x =>
                    string.Equals(
                        x.StationName?.ToString(),
                        FromStationName1,
                        StringComparison.OrdinalIgnoreCase
                    ));

                // Find destination station
                var destinationIndex = routeStops.FindIndex(x =>
                    string.Equals(
                        x.StationName?.ToString(),
                        ToStationName1,
                        StringComparison.OrdinalIgnoreCase
                    ));

                int departureDay = 1;
                int arrivalDay = 1;

                if (boardingIndex >= 0)
                {
                    departureDay = routeStops[boardingIndex].DayNumber != null ? Convert.ToInt32(routeStops[boardingIndex].DayNumber) : 1;
                }
                if (destinationIndex >= 0)
                {
                    arrivalDay = routeStops[destinationIndex].DayNumber != null ? Convert.ToInt32(routeStops[destinationIndex].DayNumber) : 1;
                }

                if (boardingIndex >= 0 && destinationIndex > boardingIndex)
                {
                    // IRCTC Rule: Boarding points must be between Source and strictly BEFORE Destination.
                    // By taking the difference, we automatically exclude the destination itself.
                    int allowedStationsCount = destinationIndex - boardingIndex;

                    routeStops = routeStops
                                    .Skip(boardingIndex)
                                    .Take(allowedStationsCount)
                                    .ToList<dynamic>();
                }
                else if (boardingIndex >= 0)
                {
                    // Fallback just in case the destination is not found
                    routeStops = routeStops
                                    .Skip(boardingIndex)
                                    .ToList<dynamic>();
                }

                ViewBag.RouteStops = routeStops;
                ViewBag.DepartureDay = departureDay;
                ViewBag.ArrivalDay = arrivalDay;

                ViewBag.CurrentStep = 1;

                // Load authentic user profile mobile and username from database
                string loggedInUserEmail = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email) ?? "";
                string dynamicMobile = "";
                string dynamicUsername = "";

                string sessionIrctcUser = User.FindFirstValue("IRCTCUsername") ?? HttpContext.Session.GetString("irctc_username") ?? "";
                try
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        conn.Open();

                        // 1. Fetch authentic username from Usrs table FIRST (master authentication source)
                        try
                        {
                            using (var uCmd = new SqlCommand(@"
                                SELECT TOP 1 Username FROM Usrs 
                                WHERE LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@E))) 
                                   OR LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@E)))
                                   OR (@S <> '' AND (LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@S))) OR LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@S)))))", conn))
                            {
                                uCmd.Parameters.AddWithValue("@E", loggedInUserEmail ?? "");
                                uCmd.Parameters.AddWithValue("@S", sessionIrctcUser ?? "");
                                var uVal = uCmd.ExecuteScalar();
                                if (uVal != null && uVal != DBNull.Value && !string.IsNullOrWhiteSpace(uVal.ToString()))
                                {
                                    dynamicUsername = uVal.ToString().Trim();
                                }
                            }
                        }
                        catch { }

                        // 2. Fetch mobile and profile data from UserProfiles table
                        using (var pCmd = new SqlCommand(@"
                            SELECT TOP 1 MobileNumber, Username FROM UserProfiles 
                            WHERE LOWER(LTRIM(RTRIM(UserId))) = LOWER(LTRIM(RTRIM(@U))) 
                               OR LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@U)))
                               OR (@S <> '' AND (LOWER(LTRIM(RTRIM(UserId))) = LOWER(LTRIM(RTRIM(@S))) OR LOWER(LTRIM(RTRIM(Username))) = LOWER(LTRIM(RTRIM(@S)))))", conn))
                        {
                            pCmd.Parameters.AddWithValue("@U", loggedInUserEmail ?? "");
                            pCmd.Parameters.AddWithValue("@S", sessionIrctcUser ?? "");
                            using (var pReader = pCmd.ExecuteReader())
                            {
                                if (pReader.Read())
                                {
                                    if (pReader["MobileNumber"] != DBNull.Value && !string.IsNullOrWhiteSpace(pReader["MobileNumber"].ToString()))
                                    {
                                        dynamicMobile = pReader["MobileNumber"].ToString().Trim();
                                    }
                                    if (string.IsNullOrWhiteSpace(dynamicUsername) && pReader["Username"] != DBNull.Value && !string.IsNullOrWhiteSpace(pReader["Username"].ToString()))
                                    {
                                        dynamicUsername = pReader["Username"].ToString().Trim();
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Notice loading checkout user profile: " + ex.Message);
                }

                // Clean mobile to pure 10 digits
                if (!string.IsNullOrWhiteSpace(dynamicMobile))
                {
                    string digitsOnly = System.Text.RegularExpressions.Regex.Replace(dynamicMobile, @"[^\d]", "");
                    if (digitsOnly.Length > 10 && digitsOnly.StartsWith("91"))
                    {
                        digitsOnly = digitsOnly.Substring(digitsOnly.Length - 10);
                    }
                    if (digitsOnly.Length == 10)
                    {
                        dynamicMobile = digitsOnly;
                    }
                }

                ViewBag.UserMobile = dynamicMobile;
                ViewBag.UserEmail = loggedInUserEmail;
                ViewBag.UserDbUsername = !string.IsNullOrWhiteSpace(dynamicUsername) ? dynamicUsername : (!string.IsNullOrWhiteSpace(sessionIrctcUser) ? sessionIrctcUser : (loggedInUserEmail.Contains("@") ? loggedInUserEmail.Split('@')[0] : loggedInUserEmail));

                return View("Checkout", booking);
            }

            catch (Exception ex)
            {
                TempData["Error"] = "Checkout failure: " + ex.Message;
                return RedirectToAction("Index", "Home");
            }

        }

        [EnableRateLimiting("BookingLimiter")]
        [HttpPost]
        public async Task<IActionResult> Confirm(
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
            string paymentCode,
            string otp = null,
            string boardingStation = null,
            string boardingDeparture = null,
            string boardingDay = null,
            string preferredCoach = null
            )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(preferredCoach))
                {
                    preferredCoach = Request.Form["preferredCoach"].FirstOrDefault();
                }
                // ================= AUTH =================
                string userid = User.FindFirstValue(ClaimTypes.NameIdentifier); // email
                string username = User.Identity.Name ?? "Passenger";

                // ================= OTP VERIFICATION (MANDATORY) =================
                /*            if (string.IsNullOrEmpty(otp))
                            {
                                TempData["Error"] = "OTP is required to complete booking.";
                                TempData["ShowOtp"] = true;
                                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                            }

                            bool isOtpValid = VerifyOTP(userid, otp, "PAYMENT");

                            if (!isOtpValid)
                            {
                                TempData["OtpError"] = "Invalid or expired OTP.";
                                TempData["ShowOtp"] = true;
                                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                            }
                */

                var otpVerified = HttpContext.Session.GetString("OtpVerified");

                if (otpVerified != "true")
                {
                    TempData["OtpError"] = "Please verify OTP first.";
                    return RedirectToAction("Checkout");
                }

                if (string.IsNullOrEmpty(userid))
                {
                    TempData["Error"] = "Please log in to continue.";
                    return RedirectToAction("Login", "Account");
                }

                // ⚡ Tatkal single ticket check
                var tatkalPayloadJson = HttpContext.Session.GetString("CheckoutPayload");
                var tatkalPayloadTemp = !string.IsNullOrEmpty(tatkalPayloadJson) ? JsonConvert.DeserializeObject<CheckoutPayload>(tatkalPayloadJson) : null;
                string currentQuota = tatkalPayloadTemp?.Quota ?? "";
                bool isTatkalWindowOrQuota = (currentQuota.ToUpper() == "TATKAL" || TatkalHelper.IsTatkalWindow() || TatkalHelper.IsTatkalExclusiveWindow());
                if (isTatkalWindowOrQuota && HttpContext.Session.GetString("HasBookedTatkalInSession") == "true")
                {
                    TempData["Error"] = "As per IRCTC guidelines, only 1 ticket can be booked per user login during Tatkal hours. Please logout and login again to book another ticket.";
                    return RedirectToAction("Checkout");
                }

                // ================= BASIC VALIDATION =================
                int availableCount = SeatStatuscount;
                if (passengerNames == null || passengerNames.Count == 0)
                {
                    TempData["Error"] = "Please add at least one passenger.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                if (passengerNames == null || passengerNames.Count == 0)
                    return BadRequest("At least one passenger is required.");

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

                    using var transaction = conn.BeginTransaction();

                    try
                    {
                        // Get FROM Station
                        using (var cmd = new SqlCommand("GetStationById", conn, transaction))
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
                        using (var cmd = new SqlCommand("GetStationById", conn, transaction))
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
                        using (var cmd = new SqlCommand("GetTrainClassById", conn, transaction))
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
                                        SeatsAvailable = Convert.ToInt32(reader["SeatsAvailable"]),
                                        SeatPrefix = reader["SeatPrefix"]?.ToString(),
                                        TotalSeats = Convert.ToInt32(reader["TotalSeats"])
                                    };
                                }
                            }
                        }

                        if (cls == null || string.IsNullOrEmpty(cls.Code))
                        {
                            TempData["Error"] = "Train class not found.";
                            return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                        }

                        // 🔥 Check 8-Hour Charting Rule
                        var indiaTimeInit = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                            DateTime.UtcNow,
                            "India Standard Time"
                        );
                        TimeSpan departureTimeInit = TimeSpan.Zero;
                        using (SqlCommand timeCmd = new SqlCommand("spGetTrainDepartureTime", conn, transaction))
                        {
                            timeCmd.CommandType = CommandType.StoredProcedure;
                            timeCmd.Parameters.AddWithValue("@TrainId", trainId);
                            var timeRes = timeCmd.ExecuteScalar();
                            if (timeRes != null && timeRes != DBNull.Value) departureTimeInit = (TimeSpan)timeRes;
                        }
                        DateTime departureDateTimeInit = DateTime.Parse(journeyDate).Date + departureTimeInit;
                        TimeSpan timeToDepartureInit = departureDateTimeInit - indiaTimeInit;
                        if (timeToDepartureInit.TotalSeconds <= 0)
                        {
                            TempData["Error"] = "Booking closed. This train has already departed.";
                            return RedirectToAction("TrainResults", "Train");
                        }
                        if (timeToDepartureInit.TotalMinutes <= 30)
                        {
                            TempData["Error"] = "Booking closed. Final charting has already been prepared for this train.";
                            return RedirectToAction("TrainResults", "Train");
                        }

                        int passengerCount = passengerNames.Count;
                        int bookedSeats = _bookingService.GetBookedSeatsCount(trainId, classId);

                        string quota = Request.Form["Quota"].FirstOrDefault();
                        quota = string.IsNullOrWhiteSpace(quota) ? null : quota.ToUpper();

                        string paymentMode = Request.Form["paymentMode"].FirstOrDefault();
                        paymentMode = string.IsNullOrWhiteSpace(paymentMode) ? "UPI" : paymentMode.ToUpper();

                        decimal quotaCharge = 0;

                        // Base fare
                        string classCode = cls.Code;

                        if (classCode.Contains("(") && classCode.Contains(")"))
                        {
                            int start = classCode.IndexOf("(") + 1;
                            int end = classCode.IndexOf(")");
                            classCode = classCode.Substring(start, end - start);
                        }

                        string payloadJson = HttpContext.Session.GetString("CheckoutPayload");

                        var payload = JsonConvert.DeserializeObject<CheckoutPayload>(payloadJson);

                        decimal fare = CalculateFare(
                            trainId,
                            FromStationId,
                            ToStationId,
                            classCode
                        );

                        cls.TotalFare = fare;

                        if (fare == 0)
                        {
                            if (decimal.TryParse(payload.diffare, out decimal parsedFare))
                            {
                                fare = parsedFare;
                                cls.TotalFare = fare; // assign parsed value to BaseFare
                            }
                        }

                        decimal backendFare = fare * passengerCount;
                        // Add quota charge
                        decimal fareAfterQuota = _fareService.AddQuotaCharge(
                            backendFare,
                            cls,
                            passengerCount,
                            quota,
                            out quotaCharge
                        );

                        // Calculate convenience fee based on payment mode
                        decimal convenienceFee = _fareService.CalculateConvenienceFee(paymentMode);

                        // Insurance fixed
                        decimal insurancePerPassenger = 0.45m;
                        decimal insurance = insurancePerPassenger * passengerCount;

                        // Surge currently not used
                        decimal surge = 0;

                        // Extract GST from convenience fee (18%)
                        decimal gst = Math.Round(convenienceFee - (convenienceFee / 1.18m), 2);

                        // Final total
                        decimal totalFare = fareAfterQuota + convenienceFee + insurance;

                        decimal totalBaseFare = backendFare;
                        decimal totalQuotaCharge = quotaCharge;
                        decimal totalSurge = surge;

                        string status = BookingStatus;

                        // ================= OTP VERIFIED =================

                        var seatStatus = _bookingService.GetSeatStatus(trainId, classId, DateTime.Parse(journeyDate), quota);

                        // Update seats availability
                        // 1️⃣ Update seat counts
                        using (var cmd = new SqlCommand(
                            "UpdateTrainClassSeats",
                            conn,
                            transaction))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("@ClassId", cls.Id);
                            cmd.Parameters.AddWithValue("@Status", BookingStatus);
                            cmd.Parameters.AddWithValue("@Quota", quota);
                            cmd.Parameters.AddWithValue("@Count", passengerCount);

                            cmd.ExecuteNonQuery();
                        }

                        int bookingId;
                        string pnr = GeneratePnr();

                        string effectiveBoardingStation = !string.IsNullOrWhiteSpace(boardingStation) ? boardingStation : Request.Form["boardingStation"].FirstOrDefault();
                        if (string.IsNullOrWhiteSpace(effectiveBoardingStation)) effectiveBoardingStation = fromStation.Name;

                        string effectiveBoardingDeparture = !string.IsNullOrWhiteSpace(boardingDeparture) ? boardingDeparture : Request.Form["boardingDeparture"].FirstOrDefault();
                        if (string.IsNullOrWhiteSpace(effectiveBoardingDeparture)) effectiveBoardingDeparture = payload.Departure;

                        // Insert Booking
                        using (var cmd = new SqlCommand("InsertBooking", conn, transaction))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@PNR", pnr);
                            cmd.Parameters.AddWithValue("@UserId", userId);
                            cmd.Parameters.AddWithValue("@TrainId", trainId);
                            cmd.Parameters.AddWithValue("@TrainClassId", cls.Id);
                            cmd.Parameters.AddWithValue("@JourneyDate", DateTime.Parse(journeyDate));
                            cmd.Parameters.AddWithValue("@BookingDate", DateTime.UtcNow);
                            cmd.Parameters.AddWithValue("@BaseFare", totalBaseFare);
                            cmd.Parameters.AddWithValue("@ConvenienceFee", convenienceFee);
                            cmd.Parameters.AddWithValue("@Insurance", insurance);
                            cmd.Parameters.AddWithValue("@Status", status);
                            cmd.Parameters.AddWithValue("@TrainNumber", trainNumber);
                            cmd.Parameters.AddWithValue("@TrainName", trainName);
                            cmd.Parameters.AddWithValue("@Class", Class);
                            cmd.Parameters.AddWithValue("@Frmst", effectiveBoardingStation);
                            cmd.Parameters.AddWithValue("@Departure", effectiveBoardingDeparture);
                            cmd.Parameters.AddWithValue("@Arrival", payload.Arrival);
                            cmd.Parameters.AddWithValue("@Duration", payload.Duration);
                            cmd.Parameters.AddWithValue("@Tost", toStation.Name);
                            cmd.Parameters.AddWithValue("@Quota", quota);
                            cmd.Parameters.AddWithValue("@GST", gst);
                            cmd.Parameters.AddWithValue("@QuotaCharge", totalQuotaCharge);
                            cmd.Parameters.AddWithValue("@SurgeAmount", totalSurge);
                            cmd.Parameters.AddWithValue("@TotalFare", totalFare);

                            bookingId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        EnsureBoardingColumnsExist(conn);
                        if (!string.IsNullOrWhiteSpace(effectiveBoardingStation) && !effectiveBoardingStation.Equals(fromStation.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            using (var bChangeCmd = new SqlCommand(@"
                                UPDATE Bookings 
                                SET OriginalBoardingStation = @Orig,
                                    IsBoardingChanged = 1,
                                    BoardingChangedAt = GETUTCDATE()
                                WHERE Id = @BId", conn, transaction))
                            {
                                bChangeCmd.Parameters.AddWithValue("@Orig", fromStation.Name);
                                bChangeCmd.Parameters.AddWithValue("@BId", bookingId);
                                bChangeCmd.ExecuteNonQuery();
                            }
                        }

                        // ---------------------------- ALLOCATE SEATS RANDOMLY ----------------------------

                        // Before calling AllocateAndInsertPassengers

                        // GetSeatStatus should call spGetSeatStatusCounts and return an object with SeatsAvailable and RACSeats


                        _allocationService.AllocateAndInsertPassengers(
                            conn,
                            transaction,
                            bookingId,
                            trainId,
                            classId,
                            DateTime.Parse(journeyDate),
                            passengerNames,
                            passengerAges,
                            passengerGenders,
                            passengerBerths,
                            seatsCapacity: cls.TotalSeats,
                            raccount: seatStatus.RACCount,
                            racSeats: seatStatus.RACSeats,
                            quota: quota,
                            preferredCoach: preferredCoach
                        );

                        // Persist catering / meal options for passengers (e.g. Vande Bharat)
                        try
                        {
                            var cateringList = Request.Form["passengerCatering"].ToList();
                            bool optOutFood = Request.Form["optOutMeals"] == "true";

                            using (var mealCheckCmd = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Passengers') AND name = 'Meal') ALTER TABLE Passengers ADD Meal NVARCHAR(50) NULL", conn, transaction))
                            {
                                mealCheckCmd.ExecuteNonQuery();
                            }

                            using (var getPassCmd = new SqlCommand("SELECT Id FROM Passengers WHERE BookingId = @BId ORDER BY Id", conn, transaction))
                            {
                                getPassCmd.Parameters.AddWithValue("@BId", bookingId);
                                var passIds = new List<int>();
                                using (var pRdr = getPassCmd.ExecuteReader())
                                {
                                    while (pRdr.Read()) passIds.Add(pRdr.GetInt32(0));
                                }

                                for (int i = 0; i < passIds.Count; i++)
                                {
                                    string mealVal = optOutFood ? "No Food" : ((cateringList != null && i < cateringList.Count && !string.IsNullOrWhiteSpace(cateringList[i])) ? cateringList[i] : "Veg");
                                    using (var upCmd = new SqlCommand("UPDATE Passengers SET Meal = @Meal WHERE Id = @PId", conn, transaction))
                                    {
                                        upCmd.Parameters.AddWithValue("@Meal", mealVal);
                                        upCmd.Parameters.AddWithValue("@PId", passIds[i]);
                                        upCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                        catch { }

                        // ✅ ONLY ONE COMMIT
                        transaction.Commit();

                        string userEmail = User.FindFirstValue(ClaimTypes.NameIdentifier);

                        var emailQueueMessage =
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                BookingId = bookingId,
                                UserId = userId,
                                Email = userEmail,
                                PNR = pnr
                            });

                        _publisher.Publish(
                            "email-queue",
                            emailQueueMessage
                        );


                        var updatedStatus =
                        _bookingService.GetSeatStatus(
                            trainId,
                            classId,
                            DateTime.Parse(journeyDate),
                            quota
                        );

                        await _hub.Clients.All.SendAsync(
                            "AvailabilityUpdated",
                            new
                            {
                                trainId,
                                classId,
                                availableSeats = updatedStatus.SeatsAvailable,
                                racCount = updatedStatus.RACCount,
                                wlCount = updatedStatus.WLCount,
                                status =
                                    updatedStatus.SeatsAvailable > 0
                                        ? "AVAILABLE"
                                        : updatedStatus.RACCount < updatedStatus.RACSeats
                                            ? "RAC"
                                            : "WL"
                            });

                        // 🔴 ADD HERE — NOW BOOKING IS FINAL
                        //await _hub.Clients
                        //    .Group($"TRAIN_{trainId}")
                        //    .SendAsync("SeatUpdated", trainId, classId);

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
                        //string userEmail = User.FindFirstValue(ClaimTypes.NameIdentifier);
                        string userName = User.Identity.Name ?? "Passenger";
                        var pdfResult = _emailService.GeneratePdf(bookingId, userId);
                        if (pdfResult.pdfBytes == null)
                        {
                            //handle error
                            return NotFound("PDF generation failed");
                        }
                        var pdfBytes = pdfResult.pdfBytes;
                        string pnrLocal = pdfResult.PNR;
                        var coachNumbers = new List<string>();
                        var seatNumbers = new List<string>();
                        var currentStatuses = new List<string>();

                        using (var cmd = new SqlCommand("spGetBookingDetails", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;

                            cmd.Parameters.AddWithValue("@BookingId", bookingId);
                            cmd.Parameters.AddWithValue("@UserId", userId);

                            using (var reader = cmd.ExecuteReader())
                            {
                                // skip booking result
                                if (reader.Read())
                                {
                                }

                                // passenger result set
                                if (reader.NextResult())
                                {
                                    while (reader.Read())
                                    {
                                        string seat = reader["SeatNumber"]?.ToString() ?? "-";

                                        seatNumbers.Add(seat);

                                        // LOCATE THIS BLOCK AROUND LINE 1334:
                                        string coach = "-";

                                        if (!string.IsNullOrEmpty(seat))
                                        {
                                            // ✅ UPGRADED COACH EXTRACTION
                                            if (seat.Contains("-"))
                                            {
                                                coach = seat.Split('-')[0]; // Extract "S1" or "S2"
                                            }
                                            else
                                            {
                                                // Legacy backward compatibility fallback
                                                coach = new string(seat.TakeWhile(char.IsLetter).ToArray());
                                                if (string.IsNullOrEmpty(coach))
                                                    coach = cls.SeatPrefix ?? "-";
                                            }
                                        }

                                        coachNumbers.Add(coach);

                                        currentStatuses.Add(
                                            reader["CurrentStatus"]?.ToString() ?? "CNF"
                                        );
                                    }
                                }
                            }
                        }

                        string body = $@"

                        <!DOCTYPE html>
                        <html>
                        <head>

                        <meta charset='UTF-8'>

                        <style>

                        @media only screen and (max-width:600px)
                        {{
                            .main-container{{
                                width:100% !important;
                                padding:10px !important;
                            }}

                            .responsive-table{{
                                width:100% !important;
                            }}

                            .responsive-table tr{{
                                display:block !important;
                                width:100% !important;
                            }}

                            .responsive-table td{{
                                display:block !important;
                                width:100% !important;
                                box-sizing:border-box;
                                text-align:left !important;
                            }}

                            .passenger-table{{
                                font-size:12px !important;
                            }}

                            .heading{{
                                font-size:24px !important;
                            }}

                            .mobile-padding{{
                                padding:12px !important;
                            }}
                        }}

                        </style>

                        </head>

                        <body style='margin:0;padding:0;background:#f2f2f2;'>


                        <div class='main-container'
                                style='font-family:Arial,sans-serif;
                                    width:100%;
                                    max-width:900px;
                                    margin:auto;
                                    border:1px solid #dcdcdc;
                                    padding:20px;
                                    background:#ffffff;
                                    box-sizing:border-box;'>

                        <div class='heading'
                                style='text-align:center;
                                    background:#0b3d91;
                                    color:white;
                                    padding:15px;
                                    font-size:28px;
                                    font-weight:bold;'>

                            IRCTC CLONE E-Ticket
                        </div>

                        <div style='padding:20px;'>

                            <h2 style='color:green;'>Booking Confirmed ✅</h2>

                            <p>
                                Dear <strong>{userName}</strong>,
                            </p>

                            <p>
                                Thank you for booking with IRCTC Clone.
                                Your ticket has been successfully booked.
                            </p>

                            <hr/>

                            <table class='responsive-table'
                                    style='width:100%;
                                            border-collapse:collapse;
                                            margin-top:15px;
                                            table-layout:fixed;
                                            word-break:break-word;'>

                                <tr>
                                    <td style='padding:8px;'><strong>PNR Number</strong></td>
                                    <td style='padding:8px;'>{pnrLocal}</td>

                                    <td style='padding:8px;'><strong>Train</strong></td>
                                    <td style='padding:8px;'>
                                        {trainNumber} - {trainName}
                                    </td>
                                </tr>

                                <tr style='background:#f5f5f5;'>
                                    <td style='padding:8px;'><strong>From</strong></td>
                                    <td style='padding:8px;'>
                                        {fromStation?.Name} ({fromStation?.Code})
                                    </td>

                                    <td style='padding:8px;'><strong>To</strong></td>
                                    <td style='padding:8px;'>
                                        {toStation?.Name} ({toStation?.Code})
                                    </td>
                                </tr>

                                <tr>
                                    <td style='padding:8px;'><strong>Date of Journey</strong></td>
                                    <td style='padding:8px;'>
                                        {DateTime.Parse(journeyDate):dd-MMM-yyyy}
                                    </td>

                                    <td style='padding:8px;'><strong>Class</strong></td>
                                    <td style='padding:8px;'>{Class}</td>
                                </tr>

                                <tr style='background:#f5f5f5;'>
                                    <td style='padding:8px;'><strong>Quota</strong></td>
                                    <td style='padding:8px;'>{quota}</td>

                                    <td style='padding:8px;'><strong>Status</strong></td>
                                    <td style='padding:8px;color:green;'>
                                        {status}
                                    </td>
                                </tr>

                            </table>

                            <br/>

                            <h3 style='background:#0b3d91;
                                        color:white;
                                        padding:10px;'>

                                Passenger Details
                            </h3>

                            <table class='passenger-table responsive-table'
                                    style='width:100%;
                                            border-collapse:collapse;
                                            border:1px solid #ccc;
                                            table-layout:fixed;
                                            word-break:break-word;'>

                            <thead style='background:#f2f2f2;'>

                                <tr>
                                    <th style='padding:8px;border:1px solid #ccc;'>Name</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Age</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Gender</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Coach</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Seat</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Berth</th>
                                    <th style='padding:8px;border:1px solid #ccc;'>Status</th>
                                </tr>

                            </thead>

                            <tbody>

                                {string.Join("", passengerNames.Select((p, i) => $@"
                                    <tr>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {p}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {passengerAges[i]}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {passengerGenders[i]}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {(coachNumbers.Count > i ? coachNumbers[i] : "-")}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {(seatNumbers.Count > i ? seatNumbers[i] : "-")}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc;'>
                                            {(passengerBerths.Count > i ? passengerBerths[i] : "-")}
                                        </td>

                                        <td style='padding:8px;border:1px solid #ccc; color:green; font-weight:bold;'>
                                            {(currentStatuses.Count > i ? currentStatuses[i] : "CNF")}
                                        </td>

                                    </tr>
                                "))}

                            </tbody>
                            </table>

                            <br/>

                            <h3 style='background:#0b3d91;
                                        color:white;
                                        padding:10px;'>

                                Fare Details
                            </h3>

                            <table style='width:100%;
                                            border-collapse:collapse;'>

                                <tr>
                                    <td style='padding:8px;'>Base Fare</td>
                                    <td style='padding:8px;'>₹ {totalBaseFare:F2}</td>
                                </tr>

                                <tr style='background:#f5f5f5;'>
                                    <td style='padding:8px;'>Convenience Fee</td>
                                    <td style='padding:8px;'>₹ {convenienceFee:F2}</td>
                                </tr>

                                <tr>
                                    <td style='padding:8px;'>Insurance</td>
                                    <td style='padding:8px;'>₹ {insurance:F2}</td>
                                </tr>

                                <tr style='background:#f5f5f5;'>
                                    <td style='padding:8px;'>GST</td>
                                    <td style='padding:8px;'>₹ {gst:F2}</td>
                                </tr>

                                <tr>
                                    <td style='padding:8px;'>Quota Charges</td>
                                    <td style='padding:8px;'>₹ {totalQuotaCharge:F2}</td>
                                </tr>

                                <tr style='background:#0b3d91;
                                            color:white;
                                            font-weight:bold;'>

                                    <td style='padding:10px;'>Total Paid</td>

                                    <td style='padding:10px;'>
                                        ₹ {totalFare:F2}
                                    </td>

                                </tr>

                            </table>

                            <br/>

                            <p>
                                Your e-ticket PDF is attached with this email.
                            </p>

                            <p>
                                Please carry valid ID proof during journey.
                            </p>

                            <hr/>

                            <div style='font-size:12px;
                                        color:#666;'>

                                This is a system generated email.
                                Please do not reply.

                                <br/><br/>

                                © 2026 IRCTC Clone
                            </div>

                        </div>

                    </div>
                    </body>
                    </html>
                    ";

                        // Fire-and-forget email
                        await _emailService.SendEmailWithAttachment(
                            userEmail,
                            $"Booking Confirmation - {pnrLocal}",
                            body,
                            pdfBytes,
                            $"Ticket_{pnrLocal}.pdf"
                        );

                        if (isTatkalWindowOrQuota)
                        {
                            HttpContext.Session.SetString("HasBookedTatkalInSession", "true");
                        }

                        TempData["BookingSuccess"] = "Ticket booked successfully and sent to your email!";
                        TempData["BookingId"] = bookingId;
                        return RedirectToAction("Confirmation", new { id = bookingId, userId = userId });
                    }

                    catch
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch
                        {

                        }

                        throw;
                    }
                }
            }

            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Critical: The booking confirmation failed. System details: " + ex.Message;
                return View("Error");
            }
        }

        // GET: /Booking/Confirmation
        [HttpGet]
        public IActionResult Confirmation()
        {
            if (TempData["BookingId"] == null)
                return RedirectToAction("Index", "Home");

            int id = Convert.ToInt32(TempData["BookingId"]);
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetBookingDetails", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.Parameters.AddWithValue("UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // 1️⃣ Read Booking details
                        if (reader.Read())
                        {
                            booking = new Booking()
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("BookingId")),
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
                                FromStationCode = reader["FromStationcode"].ToString(),
                                ToStationCode = reader["ToStationCode"].ToString(),
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
                                    SeatPrefix = booking.SeatPrefix, // or reader if coming from DB
                                    EditCount = reader.IsDBNull(reader.GetOrdinal("EditCount"))
                                                ? 0
                                                : reader.GetInt32(reader.GetOrdinal("EditCount"))
                                });

                            }
                        }

                        // 3️⃣ Stations
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                string stationType = reader["StationType"].ToString();
                                string code = reader["Code"].ToString();
                                string name = reader["Name"].ToString();

                                if (stationType == "FROM")
                                    booking.FromStationCode = code;
                                else if (stationType == "TO")
                                    booking.ToStationCode = code;

                                booking.Stations.Add(new Station
                                {
                                    Code = code,
                                    Name = name
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
                                TicketStatus = reader.GetString(reader.GetOrdinal("TicketStatus")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
                                FromStation = reader.GetString(reader.GetOrdinal("FromStation")),
                                ToStation = reader.GetString(reader.GetOrdinal("ToStation")),
                                Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                Duration = reader.GetString(reader.GetOrdinal("Duration"))
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
                                SeatPrefix = reader.GetString(reader.GetOrdinal("SeatPrefix")),
                                FromStationCode = reader.GetString(reader.GetOrdinal("FromStationCode")),
                                ToStationCode = reader.GetString(reader.GetOrdinal("ToStationCode")),
                                Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival"))
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
                                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
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

                                    SeatPrefix = booking.SeatPrefix,
                                    EditCount = reader.IsDBNull(reader.GetOrdinal("EditCount"))
                                    ? 0
                                    : reader.GetInt32(reader.GetOrdinal("EditCount")),
                                    CurrentStatus = reader.IsDBNull(reader.GetOrdinal("CurrentStatus"))
                                                        ? null
                                                        : reader.GetString(reader.GetOrdinal("CurrentStatus")),
                                });
                            }
                        }
                    }
                }
            }

            booking.Passengers = passengers;
            return PartialView(booking);
        }

        [HttpPost]
        public IActionResult DeletePassenger(int id)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("spDeletePassengerAndShift", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@PassengerId", id);

                        cmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Passenger cancelled successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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
        public async Task<IActionResult> Cancel(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string email = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new
                {
                    success = false,
                    message = "Please login again"
                });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // ✅ GET BOOKING BEFORE CANCELLATION
                    var booking = GetBookingById(id);
                    if (booking == null)
                    {
                        return Json(new { success = false, message = "Booking not found" });
                    }

                    DateTime departureDateTime = booking.JourneyDate.Date + booking.Departure;
                    var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                    TimeSpan timeToDeparture = departureDateTime - indiaTime;

                    if (timeToDeparture.TotalSeconds <= 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"FLUSHED PNR / PNR NOT YET GENERATED- ({booking.PNR})"
                        });
                    }

                    if (timeToDeparture.TotalMinutes <= 30)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Cancellation is Not Allowed as ticket is beyond permissible limit of cancellation"
                        });
                    }

                    // Collect active passengers before cancelling for dynamic promotion
                    var cancelledPax = new List<(string status, string seat, string berth, int? pos)>();
                    string getPaxSql = "SELECT CurrentStatus, SeatNumber, Berth, Position FROM Passengers WHERE BookingId = @BookingId AND CurrentStatus != 'CAN'";
                    using (var paxCmd = new SqlCommand(getPaxSql, conn))
                    {
                        paxCmd.Parameters.AddWithValue("@BookingId", id);
                        using (var r = await paxCmd.ExecuteReaderAsync())
                        {
                            while (await r.ReadAsync())
                            {
                                string cStatus = r["CurrentStatus"]?.ToString() ?? "";
                                string seat = r["SeatNumber"]?.ToString() ?? "";
                                string berth = r["Berth"]?.ToString() ?? "";
                                int? pos = r.IsDBNull(r.GetOrdinal("Position")) ? (int?)null : r.GetInt32(r.GetOrdinal("Position"));
                                cancelledPax.Add((cStatus, seat, berth, pos));
                            }
                        }
                    }

                    string pnr = "";

                    using (var cmd = new SqlCommand("spCancelBooking", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@BookingId", id);
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        var pnrParam = new SqlParameter("@PNR", SqlDbType.NVarChar, 50)
                        {
                            Direction = ParameterDirection.Output
                        };

                        cmd.Parameters.Add(pnrParam);

                        await cmd.ExecuteNonQueryAsync();

                        pnr = pnrParam.Value?.ToString();
                    }

                    // ✅ DYNAMIC QUEUE PROMOTIONS (RAC -> CNF, WL -> RAC)
                    await ProcessDynamicQueuePromotions(conn, booking.TrainId, booking.TrainClassId, booking.JourneyDate, cancelledPax);

                    // ✅ SEND EMAIL
                    var refundResult = CalculateRefund(booking);
                    decimal refundAmount = refundResult.refund;

                    try
                    {
                        await SendCancellationEmail(email, pnr, refundAmount);
                    }
                    catch (Exception emailEx)
                    {
                        Console.WriteLine("Email failed: " + emailEx.Message);
                    }

                    // Real-time broadcast
                    try
                    {
                        var updatedStatus = _bookingService.GetSeatStatus(booking.TrainId, booking.TrainClassId, booking.JourneyDate, booking.Quota);
                        await _hub.Clients.All.SendAsync("AvailabilityUpdated", new
                        {
                            trainId = booking.TrainId,
                            classId = booking.TrainClassId,
                            availableSeats = updatedStatus.SeatsAvailable,
                            racCount = updatedStatus.RACCount,
                            wlCount = updatedStatus.WLCount,
                            status = updatedStatus.SeatsAvailable > 0 ? "AVAILABLE" : (updatedStatus.RACCount < updatedStatus.RACSeats ? "RAC" : "WL")
                        });
                    }
                    catch { }

                    return Json(new
                    {
                        success = true,
                        message = "Ticket cancelled successfully"
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelPassengers([FromBody] List<int> passengerIds)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string email = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(userId) || passengerIds == null || !passengerIds.Any())
                return Json(new { success = false, message = "Invalid passenger selection" });

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    int firstPaxId = passengerIds.First();
                    int bkgId = 0;
                    string bkgPnr = "";
                    DateTime journeyDate = DateTime.MinValue;
                    TimeSpan departure = TimeSpan.Zero;
                    int trainId = 0;
                    int trainClassId = 0;
                    string quota = "General";

                    string getBkgSql = @"
                        SELECT b.Id, b.PNR, b.JourneyDate, b.Departure, b.TrainId, b.TrainClassId, b.Quota
                        FROM Passengers p
                        JOIN Bookings b ON p.BookingId = b.Id
                        WHERE p.Id = @PaxId";

                    using (var bkgCmd = new SqlCommand(getBkgSql, conn))
                    {
                        bkgCmd.Parameters.AddWithValue("@PaxId", firstPaxId);
                        using (var r = await bkgCmd.ExecuteReaderAsync())
                        {
                            if (await r.ReadAsync())
                            {
                                bkgId = Convert.ToInt32(r["Id"]);
                                bkgPnr = r["PNR"].ToString();
                                journeyDate = Convert.ToDateTime(r["JourneyDate"]);
                                if (r["Departure"] != DBNull.Value && TimeSpan.TryParse(r["Departure"].ToString(), out var depTime))
                                    departure = depTime;
                                trainId = Convert.ToInt32(r["TrainId"]);
                                trainClassId = Convert.ToInt32(r["TrainClassId"]);
                                quota = r["Quota"]?.ToString() ?? "General";
                            }
                        }
                    }

                    if (bkgId == 0)
                    {
                        return Json(new { success = false, message = "Booking details not found." });
                    }

                    DateTime departureDateTime = journeyDate.Date + departure;
                    var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                    TimeSpan timeToDeparture = departureDateTime - indiaTime;

                    if (timeToDeparture.TotalSeconds <= 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"FLUSHED PNR / PNR NOT YET GENERATED- ({bkgPnr})"
                        });
                    }

                    if (timeToDeparture.TotalMinutes <= 30)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Cancellation is Not Allowed as ticket is beyond permissible limit of cancellation"
                        });
                    }

                    // Collect details of passengers being cancelled
                    var cancelledPax = new List<(string status, string seat, string berth, int? pos)>();
                    string inClause = string.Join(",", passengerIds);
                    string getPaxDetailsSql = $"SELECT CurrentStatus, SeatNumber, Berth, Position FROM Passengers WHERE Id IN ({inClause}) AND CurrentStatus != 'CAN'";
                    using (var pDetailsCmd = new SqlCommand(getPaxDetailsSql, conn))
                    {
                        using (var r = await pDetailsCmd.ExecuteReaderAsync())
                        {
                            while (await r.ReadAsync())
                            {
                                string cStatus = r["CurrentStatus"]?.ToString() ?? "";
                                string seat = r["SeatNumber"]?.ToString() ?? "";
                                string berth = r["Berth"]?.ToString() ?? "";
                                int? pos = r.IsDBNull(r.GetOrdinal("Position")) ? (int?)null : r.GetInt32(r.GetOrdinal("Position"));
                                cancelledPax.Add((cStatus, seat, berth, pos));
                            }
                        }
                    }

                    string pnr = "";

                    foreach (var id in passengerIds)
                    {
                        using (var cmd = new SqlCommand("spCancelPassenger", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@PassengerId", id);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // ✅ Update booking status
                    using (var cmd = new SqlCommand("spUpdateBookingStatusAfterPartialCancel", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@PassengerIds", string.Join(",", passengerIds));

                        var pnrParam = new SqlParameter("@PNR", SqlDbType.NVarChar, 50)
                        {
                            Direction = ParameterDirection.Output
                        };

                        cmd.Parameters.Add(pnrParam);
                        await cmd.ExecuteNonQueryAsync();

                        pnr = pnrParam.Value?.ToString();
                    }

                    // ✅ DYNAMIC QUEUE PROMOTIONS (RAC -> CNF, WL -> RAC)
                    await ProcessDynamicQueuePromotions(conn, trainId, trainClassId, journeyDate, cancelledPax);

                    var booking = GetBookingByPNR(pnr);
                    if (booking != null)
                    {
                        var refundResult = CalculateRefund(booking);
                        decimal refundAmount = refundResult.refund;

                        try
                        {
                            await SendCancellationEmail(email, pnr, refundAmount);
                        }
                        catch (Exception emailEx)
                        {
                            Console.WriteLine("Email failed: " + emailEx.Message);
                        }
                    }

                    // Real-time broadcast
                    try
                    {
                        var updatedStatus = _bookingService.GetSeatStatus(trainId, trainClassId, journeyDate, quota);
                        await _hub.Clients.All.SendAsync("AvailabilityUpdated", new
                        {
                            trainId = trainId,
                            classId = trainClassId,
                            availableSeats = updatedStatus.SeatsAvailable,
                            racCount = updatedStatus.RACCount,
                            wlCount = updatedStatus.WLCount,
                            status = updatedStatus.SeatsAvailable > 0 ? "AVAILABLE" : (updatedStatus.RACCount < updatedStatus.RACSeats ? "RAC" : "WL")
                        });
                    }
                    catch { }

                    return Json(new { success = true, message = "Passenger cancelled successfully" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task ProcessDynamicQueuePromotions(
            SqlConnection conn, 
            int trainId, 
            int classId, 
            DateTime journeyDate, 
            List<(string status, string seat, string berth, int? position)> cancelledPaxList)
        {
            foreach (var pax in cancelledPaxList)
            {
                try
                {
                    if (pax.status == "CNF" && !string.IsNullOrWhiteSpace(pax.seat))
                    {
                        // 1. Find next RAC passenger
                        int racPaxId = 0;
                        string racUserEmail = "";
                        string racPaxName = "";
                        string racPnr = "";
                        string trainName = "";
                        string trainNo = "";

                        string findRacSql = @"
                            SELECT TOP 1 p.Id, p.Name, b.PNR, b.UserId, b.TrainName, b.TrainNumber
                            FROM Passengers p
                            JOIN Bookings b ON p.BookingId = b.Id
                            WHERE b.TrainId = @TrainId 
                              AND b.TrainClassId = @ClassId 
                              AND CAST(b.JourneyDate AS DATE) = @JourneyDate
                              AND p.CurrentStatus = 'RAC'
                              AND b.Status != 'CANCELLED'
                            ORDER BY p.Id ASC";

                        using (var cmd = new SqlCommand(findRacSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@TrainId", trainId);
                            cmd.Parameters.AddWithValue("@ClassId", classId);
                            cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                if (await r.ReadAsync())
                                {
                                    racPaxId = Convert.ToInt32(r["Id"]);
                                    racPaxName = r["Name"]?.ToString() ?? "Passenger";
                                    racPnr = r["PNR"]?.ToString() ?? "";
                                    racUserEmail = r["UserId"]?.ToString() ?? "";
                                    trainName = r["TrainName"]?.ToString() ?? "";
                                    trainNo = r["TrainNumber"]?.ToString() ?? "";
                                }
                            }
                        }

                        if (racPaxId > 0)
                        {
                            // Promote RAC -> CNF with this freed seat & berth
                            string updateRacSql = @"
                                UPDATE Passengers 
                                SET CurrentStatus = 'CNF', 
                                    SeatNumber = @SeatNumber, 
                                    Berth = @Berth 
                                WHERE Id = @PassengerId";

                            using (var upCmd = new SqlCommand(updateRacSql, conn))
                            {
                                upCmd.Parameters.AddWithValue("@SeatNumber", pax.seat);
                                upCmd.Parameters.AddWithValue("@Berth", string.IsNullOrWhiteSpace(pax.berth) ? "LB" : pax.berth);
                                upCmd.Parameters.AddWithValue("@PassengerId", racPaxId);
                                await upCmd.ExecuteNonQueryAsync();
                            }

                            // Send Email to promoted RAC -> CNF passenger
                            string targetEmail = racUserEmail;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _emailService.SendStatusUpgradeEmailAsync(
                                        targetEmail, racPnr, trainNo, trainName, racPaxName,
                                        "RAC", "CNF", $"{pax.seat} ({pax.berth})", journeyDate);
                                }
                                catch { }
                            });

                            // 2. Now promote next WL -> RAC
                            await PromoteNextWlToRac(conn, trainId, classId, journeyDate);
                        }
                    }
                    else if (pax.status == "RAC")
                    {
                        // RAC cancelled -> Promote next WL to RAC
                        await PromoteNextWlToRac(conn, trainId, classId, journeyDate);
                    }
                    else if (pax.status == "WL" && pax.position.HasValue)
                    {
                        // Decrement positions of subsequent WL passengers
                        string decWlSql = @"
                            UPDATE p
                            SET p.Position = p.Position - 1
                            FROM Passengers p
                            JOIN Bookings b ON p.BookingId = b.Id
                            WHERE b.TrainId = @TrainId 
                              AND b.TrainClassId = @ClassId 
                              AND CAST(b.JourneyDate AS DATE) = @JourneyDate
                              AND p.CurrentStatus = 'WL'
                              AND p.Position > @CancelledPos
                              AND b.Status != 'CANCELLED'";

                        using (var cmd = new SqlCommand(decWlSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@TrainId", trainId);
                            cmd.Parameters.AddWithValue("@ClassId", classId);
                            cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                            cmd.Parameters.AddWithValue("@CancelledPos", pax.position.Value);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in ProcessDynamicQueuePromotions: " + ex.Message);
                }
            }
        }

        private async Task PromoteNextWlToRac(SqlConnection conn, int trainId, int classId, DateTime journeyDate)
        {
            try
            {
                int wlPaxId = 0;
                string wlUserEmail = "";
                string wlPaxName = "";
                string wlPnr = "";
                string trainName = "";
                string trainNo = "";
                int? oldWlPos = null;

                string findWlSql = @"
                    SELECT TOP 1 p.Id, p.Name, p.Position, b.PNR, b.UserId, b.TrainName, b.TrainNumber
                    FROM Passengers p
                    JOIN Bookings b ON p.BookingId = b.Id
                    WHERE b.TrainId = @TrainId 
                      AND b.TrainClassId = @ClassId 
                      AND CAST(b.JourneyDate AS DATE) = @JourneyDate
                      AND p.CurrentStatus = 'WL'
                      AND b.Status != 'CANCELLED'
                    ORDER BY p.Position ASC, p.Id ASC";

                using (var cmd = new SqlCommand(findWlSql, conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            wlPaxId = Convert.ToInt32(r["Id"]);
                            wlPaxName = r["Name"]?.ToString() ?? "Passenger";
                            wlPnr = r["PNR"]?.ToString() ?? "";
                            wlUserEmail = r["UserId"]?.ToString() ?? "";
                            trainName = r["TrainName"]?.ToString() ?? "";
                            trainNo = r["TrainNumber"]?.ToString() ?? "";
                            if (r["Position"] != DBNull.Value) oldWlPos = Convert.ToInt32(r["Position"]);
                        }
                    }
                }

                if (wlPaxId > 0)
                {
                    // Promote WL -> RAC
                    string updateWlSql = @"
                        UPDATE Passengers 
                        SET CurrentStatus = 'RAC'
                        WHERE Id = @PassengerId";

                    using (var upCmd = new SqlCommand(updateWlSql, conn))
                    {
                        upCmd.Parameters.AddWithValue("@PassengerId", wlPaxId);
                        await upCmd.ExecuteNonQueryAsync();
                    }

                    // Decrement subsequent WL positions
                    if (oldWlPos.HasValue)
                    {
                        string decSubsequentWl = @"
                            UPDATE p
                            SET p.Position = p.Position - 1
                            FROM Passengers p
                            JOIN Bookings b ON p.BookingId = b.Id
                            WHERE b.TrainId = @TrainId 
                              AND b.TrainClassId = @ClassId 
                              AND CAST(b.JourneyDate AS DATE) = @JourneyDate
                              AND p.CurrentStatus = 'WL'
                              AND p.Position > @OldPos
                              AND b.Status != 'CANCELLED'";

                        using (var decCmd = new SqlCommand(decSubsequentWl, conn))
                        {
                            decCmd.Parameters.AddWithValue("@TrainId", trainId);
                            decCmd.Parameters.AddWithValue("@ClassId", classId);
                            decCmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                            decCmd.Parameters.AddWithValue("@OldPos", oldWlPos.Value);
                            await decCmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Send Email to promoted WL -> RAC passenger
                    string targetEmail = wlUserEmail;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _emailService.SendStatusUpgradeEmailAsync(
                                targetEmail, wlPnr, trainNo, trainName, wlPaxName,
                                "WL", "RAC", "RAC Status Confirmed", journeyDate);
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in PromoteNextWlToRac: " + ex.Message);
            }
        }

        public class ChangeBoardingRequest
        {
            public int BookingId { get; set; }
            public string NewStation { get; set; }
            public string DepartureTime { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetBoardingStations(int bookingId)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Please login to change boarding point." });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    EnsureBoardingColumnsExist(conn);

                    string sql = @"
                        SELECT b.Id, b.PNR, b.TrainId, b.TrainNumber, b.TrainName, b.Frmst, b.Tost, b.JourneyDate, b.Departure,
                               b.Status, ISNULL(b.IsBoardingChanged, 0) AS IsBoardingChanged
                        FROM Bookings b
                        WHERE b.Id = @BookingId AND b.UserId = @UserId";

                    int trainId = 0;
                    string pnr = "";
                    string trainName = "";
                    string trainNumber = "";
                    string currentBoarding = "";
                    string destination = "";
                    DateTime journeyDate = DateTime.MinValue;
                    TimeSpan currentDeparture = TimeSpan.Zero;
                    string status = "";
                    bool isBoardingChanged = false;

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@BookingId", bookingId);
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        using (var r = await cmd.ExecuteReaderAsync())
                        {
                            if (!await r.ReadAsync())
                            {
                                return Json(new { success = false, message = "Booking not found." });
                            }

                            trainId = Convert.ToInt32(r["TrainId"]);
                            pnr = r["PNR"].ToString();
                            trainName = r["TrainName"]?.ToString() ?? "";
                            trainNumber = r["TrainNumber"]?.ToString() ?? "";
                            currentBoarding = r["Frmst"]?.ToString() ?? "";
                            destination = r["Tost"]?.ToString() ?? "";
                            journeyDate = Convert.ToDateTime(r["JourneyDate"]);
                            if (r["Departure"] != DBNull.Value && TimeSpan.TryParse(r["Departure"].ToString(), out var depTime))
                                currentDeparture = depTime;
                            status = r["Status"]?.ToString() ?? "";
                            isBoardingChanged = Convert.ToBoolean(r["IsBoardingChanged"]);
                        }
                    }

                    if (status == "CANCELLED")
                    {
                        return Json(new { success = false, message = "Ticket is cancelled. Boarding station cannot be changed." });
                    }

                    if (isBoardingChanged)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Boarding station can be changed only once. Second chance is not permitted as per IRCTC rules.",
                            code = "ALREADY_CHANGED"
                        });
                    }

                    var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                    DateTime departureDateTime = journeyDate.Date + currentDeparture;
                    TimeSpan timeToDeparture = departureDateTime - indiaTime;

                    if (timeToDeparture.TotalSeconds <= 0)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"FLUSHED PNR / PNR NOT YET GENERATED- ({pnr})",
                            code = "DEPARTED"
                        });
                    }

                    if (timeToDeparture.TotalMinutes <= 30)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Boarding station change is not allowed within 30 minutes of departure.",
                            code = "BEYOND_LIMIT"
                        });
                    }

                    var routeStops = new List<object>();
                    using (var cmd = new SqlCommand("spGetFullTrainRouteForBooking", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TrainId", trainId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string stName = reader["StationName"]?.ToString() ?? "";
                                string depTime = reader["DepartureTime"]?.ToString() ?? reader["ArrivalTime"]?.ToString() ?? "";

                                routeStops.Add(new
                                {
                                    StopNumber = reader["StopNumber"],
                                    StationName = stName,
                                    DepartureTime = depTime,
                                    DayNumber = reader["Day"],
                                    Distance = reader["DistanceFromSource"]
                                });
                            }
                        }
                    }

                    return Json(new
                    {
                        success = true,
                        pnr = pnr,
                        bookingId = bookingId,
                        trainNo = trainNumber,
                        trainName = trainName,
                        currentBoarding = currentBoarding,
                        destination = destination,
                        currentDeparture = currentDeparture.ToString(@"hh\:mm"),
                        journeyDate = journeyDate.ToString("dd MMM yyyy"),
                        stations = routeStops
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error loading boarding stations: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ChangeBoardingPoint([FromBody] ChangeBoardingRequest req)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string userEmail = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Please login again." });
            }

            if (req == null || req.BookingId <= 0 || string.IsNullOrWhiteSpace(req.NewStation))
            {
                return Json(new { success = false, message = "Invalid request details." });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    EnsureBoardingColumnsExist(conn);

                    string checkSql = @"
                        SELECT b.Id, b.PNR, b.TrainId, b.TrainNumber, b.TrainName, b.Frmst, b.Tost, b.JourneyDate, b.Departure,
                               b.Status, ISNULL(b.IsBoardingChanged, 0) AS IsBoardingChanged, b.UserId
                        FROM Bookings b
                        WHERE b.Id = @BookingId AND b.UserId = @UserId";

                    string pnr = "";
                    string trainName = "";
                    string trainNumber = "";
                    string oldBoarding = "";
                    DateTime journeyDate = DateTime.MinValue;
                    TimeSpan currentDeparture = TimeSpan.Zero;
                    bool isBoardingChanged = false;

                    using (var cmd = new SqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@BookingId", req.BookingId);
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        using (var r = await cmd.ExecuteReaderAsync())
                        {
                            if (!await r.ReadAsync())
                            {
                                return Json(new { success = false, message = "Booking not found." });
                            }

                            pnr = r["PNR"].ToString();
                            trainName = r["TrainName"]?.ToString() ?? "";
                            trainNumber = r["TrainNumber"]?.ToString() ?? "";
                            oldBoarding = r["Frmst"]?.ToString() ?? "";
                            journeyDate = Convert.ToDateTime(r["JourneyDate"]);
                            if (r["Departure"] != DBNull.Value && TimeSpan.TryParse(r["Departure"].ToString(), out var depTime))
                                currentDeparture = depTime;
                            isBoardingChanged = Convert.ToBoolean(r["IsBoardingChanged"]);
                        }
                    }

                    if (isBoardingChanged)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Boarding station can be changed only once. Second chance is not permitted as per IRCTC rules."
                        });
                    }

                    var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                    DateTime departureDateTime = journeyDate.Date + currentDeparture;
                    TimeSpan timeToDeparture = departureDateTime - indiaTime;

                    if (timeToDeparture.TotalSeconds <= 0)
                    {
                        return Json(new { success = false, message = $"FLUSHED PNR / PNR NOT YET GENERATED- ({pnr})" });
                    }

                    if (timeToDeparture.TotalMinutes <= 30)
                    {
                        return Json(new { success = false, message = "Boarding station change is not allowed within 30 minutes of departure." });
                    }

                    TimeSpan newDepTime = currentDeparture;
                    if (!string.IsNullOrWhiteSpace(req.DepartureTime) && TimeSpan.TryParse(req.DepartureTime, out var parsedDep))
                    {
                        newDepTime = parsedDep;
                    }

                    string updateSql = @"
                        UPDATE Bookings
                        SET Frmst = @NewStation,
                            Departure = @NewDeparture,
                            IsBoardingChanged = 1,
                            BoardingChangedAt = GETUTCDATE(),
                            OriginalBoardingStation = CASE WHEN OriginalBoardingStation IS NULL THEN @OldStation ELSE OriginalBoardingStation END
                        WHERE Id = @BookingId AND UserId = @UserId";

                    using (var updateCmd = new SqlCommand(updateSql, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@NewStation", req.NewStation);
                        updateCmd.Parameters.AddWithValue("@NewDeparture", newDepTime);
                        updateCmd.Parameters.AddWithValue("@OldStation", oldBoarding);
                        updateCmd.Parameters.AddWithValue("@BookingId", req.BookingId);
                        updateCmd.Parameters.AddWithValue("@UserId", userId);

                        await updateCmd.ExecuteNonQueryAsync();
                    }

                    // Send email confirmation
                    string recipientEmail = !string.IsNullOrWhiteSpace(userEmail) ? userEmail : userId;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _emailService.SendBoardingPointChangedEmailAsync(
                                recipientEmail, pnr, trainNumber, trainName, oldBoarding, req.NewStation,
                                journeyDate, newDepTime);
                        }
                        catch (Exception emailEx)
                        {
                            Console.WriteLine("Boarding change email error: " + emailEx.Message);
                        }
                    });

                    return Json(new
                    {
                        success = true,
                        message = $"Boarding station changed successfully to {req.NewStation}.",
                        newStation = req.NewStation,
                        newDeparture = newDepTime.ToString(@"hh\:mm")
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update boarding station: " + ex.Message });
            }
        }

        private void EnsureBoardingColumnsExist(SqlConnection conn)
        {
            try
            {
                string sql = @"
                    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Bookings' AND COLUMN_NAME = 'IsBoardingChanged')
                    BEGIN
                        ALTER TABLE Bookings ADD IsBoardingChanged BIT NOT NULL DEFAULT 0;
                    END
                    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Bookings' AND COLUMN_NAME = 'BoardingChangedAt')
                    BEGIN
                        ALTER TABLE Bookings ADD BoardingChangedAt DATETIME NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Bookings' AND COLUMN_NAME = 'OriginalBoardingStation')
                    BEGIN
                        ALTER TABLE Bookings ADD OriginalBoardingStation NVARCHAR(100) NULL;
                    END";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        [HttpGet]
        public IActionResult GetBookingForCancel(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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
                        object booking = null;
                        var passengers = new List<object>();

                        if (reader.Read())
                        {
                            DateTime jDate = Convert.ToDateTime(reader["JourneyDate"]);
                            TimeSpan depTime = reader["Departure"] != DBNull.Value ? (TimeSpan)reader["Departure"] : TimeSpan.Zero;
                            var indiaTimeNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                            DateTime depDateTime = jDate.Date + depTime;
                            TimeSpan diff = depDateTime - indiaTimeNow;

                            string pnrStr = reader["PNR"].ToString();
                            string userEmailStr = reader["UserId"]?.ToString() ?? userId;

                            if (diff.TotalSeconds <= 0)
                            {
                                return Json(new
                                {
                                    success = false,
                                    message = $"FLUSHED PNR / PNR NOT YET GENERATED- ({pnrStr})"
                                });
                            }

                            if (diff.TotalMinutes <= 30)
                            {
                                return Json(new
                                {
                                    success = false,
                                    message = "Cancellation is Not Allowed as ticket is beyond permissible limit of cancellation"
                                });
                            }

                            bool isChartPrepared = diff.TotalHours <= 8;
                            string chartingStatus = isChartPrepared ? "Chart Prepared" : "Chart Not Prepared";

                            // Immediate trigger if chart prepared
                            if (isChartPrepared && diff.TotalHours >= -4)
                            {
                                int bkgIdCopy = id;
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        using (var checkConn = new SqlConnection(_connectionString))
                                        {
                                            await checkConn.OpenAsync();
                                            var checkCmd = new SqlCommand("SELECT COUNT(1) FROM ChartNotificationLogs WHERE BookingId = @BookingId", checkConn);
                                            checkCmd.Parameters.AddWithValue("@BookingId", bkgIdCopy);
                                            int count = (int)await checkCmd.ExecuteScalarAsync();
                                            if (count == 0)
                                            {
                                                bool sent = await _emailService.SendChartPreparedEmailAsync(bkgIdCopy);
                                                if (sent)
                                                {
                                                    var insertCmd = new SqlCommand("INSERT INTO ChartNotificationLogs (BookingId, PNR, RecipientEmail, SentAt) VALUES (@BookingId, @PNR, @RecipientEmail, GETDATE())", checkConn);
                                                    insertCmd.Parameters.AddWithValue("@BookingId", bkgIdCopy);
                                                    insertCmd.Parameters.AddWithValue("@PNR", pnrStr);
                                                    insertCmd.Parameters.AddWithValue("@RecipientEmail", userEmailStr);
                                                    await insertCmd.ExecuteNonQueryAsync();
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                });
                            }

                            booking = new
                            {
                                bookingId = id,
                                pnr = pnrStr,
                                trainName = reader["TrainName"].ToString(),
                                trainNo = reader["TrainNumber"].ToString(),
                                from = reader["Frmst"].ToString(),
                                to = reader["Tost"].ToString(),

                                journeyDate = jDate.ToString("dd MMM yyyy"),
                                rawJourneyDate = jDate.ToString("yyyy-MM-dd"),
                                journeyFull = reader["JourneyFull"] != DBNull.Value ? reader["JourneyFull"].ToString() : "",
                                bookingDate = reader["BookingDate"] != DBNull.Value ? Convert.ToDateTime(reader["BookingDate"]).ToString("dd MMM yyyy | hh:mm tt") : "",

                                quota = reader["Quota"] != DBNull.Value ? reader["Quota"].ToString() : "General",
                                convenienceFees = reader["ConvenienceFee"] != DBNull.Value ? Convert.ToDecimal(reader["ConvenienceFee"]) : 0.00m,
                                insurance = reader["Insurance"] != DBNull.Value ? Convert.ToDecimal(reader["Insurance"]) : 0.00m,
                                className = reader["ClassCode"].ToString(),

                                baseFare = reader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(reader["BaseFare"]) : 0.00m,
                                gst = reader["GST"] != DBNull.Value ? Convert.ToDecimal(reader["GST"]) : 0.00m,
                                quotaCharge = reader["QuotaCharge"] != DBNull.Value ? Convert.ToDecimal(reader["QuotaCharge"]) : 0.00m,
                                surge = reader["SurgeAmount"] != DBNull.Value ? Convert.ToDecimal(reader["SurgeAmount"]) : 0.00m,
                                fare = reader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(reader["TotalFare"]) : 0.00m,
                                departure = reader["Departure"] != DBNull.Value ? reader["Departure"].ToString() : "",
                                arrival = reader["Arrival"] != DBNull.Value ? reader["Arrival"].ToString() : "",
                                duration = reader["Duration"] != DBNull.Value ? reader["Duration"].ToString() : "",
                                status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : "CONFIRMED",
                                isChartPrepared = isChartPrepared,
                                chartingStatus = chartingStatus
                            };
                        }

                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                string bookingStatus = reader["BookingStatus"] != DBNull.Value ? reader["BookingStatus"].ToString() : "";
                                string currentStatus = reader["CurrentStatus"] != DBNull.Value ? reader["CurrentStatus"].ToString() : bookingStatus;
                                string seatNum = reader["SeatNumber"] != DBNull.Value ? reader["SeatNumber"].ToString() : "";
                                string berth = reader["Berth"] != DBNull.Value ? reader["Berth"].ToString() : "";

                                passengers.Add(new
                                {
                                    id = reader["Id"],
                                    name = reader["Name"].ToString(),
                                    age = reader["Age"],
                                    gender = reader["Gender"].ToString(),
                                    bookingStatus = bookingStatus,
                                    currentStatus = currentStatus,
                                    berth = berth,
                                    seat = seatNum,
                                    Position = reader.IsDBNull(reader.GetOrdinal("Position"))
                                    ? (int?)null
                                    : reader.GetInt32(reader.GetOrdinal("Position"))
                                });
                            }
                        }

                        return Json(new { booking, passengers });
                    }
                }
            }
        }

        [HttpGet]
        [ActionName("GetBookingDetails")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBookingDetailsAsync(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("spGetBookingDtls", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BookingId", id);
                        cmd.Parameters.AddWithValue("@UserId", userId ?? "");

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            object booking = null;
                            var passengers = new List<object>();

                            if (await reader.ReadAsync())
                            {
                                DateTime jDate = Convert.ToDateTime(reader["JourneyDate"]);
                                TimeSpan depTime = reader["Departure"] != DBNull.Value ? (TimeSpan)reader["Departure"] : TimeSpan.Zero;
                                var indiaTimeNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                                DateTime depDateTime = jDate.Date + depTime;
                                TimeSpan diff = depDateTime - indiaTimeNow;

                                string pnrStr = reader["PNR"].ToString();
                                bool isChartPrepared = diff.TotalHours <= 8 && diff.TotalHours >= -24;
                                string chartingStatus = isChartPrepared ? "Chart Prepared" : "Chart Not Prepared";

                                bool canCancel = diff.TotalMinutes > 30 && diff.TotalSeconds > 0 && reader["Status"].ToString() != "CANCELLED";

                                booking = new
                                {
                                    bookingId = id,
                                    pnr = pnrStr,
                                    trainName = reader["TrainName"].ToString(),
                                    trainNo = reader["TrainNumber"].ToString(),
                                    from = reader["Frmst"].ToString(),
                                    to = reader["Tost"].ToString(),

                                    journeyDate = jDate.ToString("dd MMM yyyy"),
                                    rawJourneyDate = jDate.ToString("yyyy-MM-dd"),
                                    journeyFull = reader["JourneyFull"] != DBNull.Value ? reader["JourneyFull"].ToString() : "",
                                    bookingDate = reader["BookingDate"] != DBNull.Value ? Convert.ToDateTime(reader["BookingDate"]).ToString("dd MMM yyyy | hh:mm tt") : "",

                                    quota = reader["Quota"] != DBNull.Value ? reader["Quota"].ToString() : "General",
                                    convenienceFees = reader["ConvenienceFee"] != DBNull.Value ? Convert.ToDecimal(reader["ConvenienceFee"]) : 0.00m,
                                    insurance = reader["Insurance"] != DBNull.Value ? Convert.ToDecimal(reader["Insurance"]) : 0.00m,
                                    className = reader["ClassCode"].ToString(),

                                    baseFare = reader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(reader["BaseFare"]) : 0.00m,
                                    gst = reader["GST"] != DBNull.Value ? Convert.ToDecimal(reader["GST"]) : 0.00m,
                                    quotaCharge = reader["QuotaCharge"] != DBNull.Value ? Convert.ToDecimal(reader["QuotaCharge"]) : 0.00m,
                                    surge = reader["SurgeAmount"] != DBNull.Value ? Convert.ToDecimal(reader["SurgeAmount"]) : 0.00m,
                                    fare = reader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(reader["TotalFare"]) : 0.00m,
                                    departure = reader["Departure"] != DBNull.Value ? reader["Departure"].ToString() : "",
                                    arrival = reader["Arrival"] != DBNull.Value ? reader["Arrival"].ToString() : "",
                                    duration = reader["Duration"] != DBNull.Value ? reader["Duration"].ToString() : "",
                                    status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : "CONFIRMED",
                                    isChartPrepared = isChartPrepared,
                                    chartingStatus = chartingStatus,
                                    canCancel = canCancel
                                };
                            }

                            if (await reader.NextResultAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    string bookingStatus = reader["BookingStatus"] != DBNull.Value ? reader["BookingStatus"].ToString() : "";
                                    string currentStatus = reader["CurrentStatus"] != DBNull.Value ? reader["CurrentStatus"].ToString() : bookingStatus;
                                    string seatNum = reader["SeatNumber"] != DBNull.Value ? reader["SeatNumber"].ToString() : "";
                                    string berth = reader["Berth"] != DBNull.Value ? reader["Berth"].ToString() : "";

                                    string meal = "";
                                    try
                                    {
                                        int mealOrd = reader.GetOrdinal("Meal");
                                        if (mealOrd >= 0 && reader["Meal"] != DBNull.Value)
                                        {
                                            meal = reader["Meal"].ToString();
                                        }
                                    }
                                    catch { }

                                    passengers.Add(new
                                    {
                                        id = reader["Id"],
                                        name = reader["Name"].ToString(),
                                        age = reader["Age"],
                                        gender = reader["Gender"].ToString(),
                                        bookingStatus = bookingStatus,
                                        currentStatus = currentStatus,
                                        berth = berth,
                                        seat = seatNum,
                                        meal = meal,
                                        Position = reader.IsDBNull(reader.GetOrdinal("Position"))
                                            ? (int?)null
                                            : reader.GetInt32(reader.GetOrdinal("Position"))
                                    });
                                }
                            }

                            if (booking != null)
                            {
                                return Json(new { success = true, booking, passengers });
                            }
                        }
                    }

                    // Fallback to direct query if spGetBookingDtls returned nothing (e.g. UserId mismatch or past trip)
                    using (var fallbackCmd = new SqlCommand(@"
                        SELECT TOP 1 b.Id, b.PNR, b.TrainId, b.TrainNumber, b.TrainName, b.Frmst, b.Tost, 
                                     b.JourneyDate, b.Departure, b.Arrival, b.Duration, b.Quota, b.Class, 
                                     b.Status, b.TicketStatus, b.BaseFare, b.TotalFare, b.CreatedDate AS BookingDate
                        FROM Bookings b WHERE b.Id = @BId", conn))
                    {
                        fallbackCmd.Parameters.AddWithValue("@BId", id);
                        using (var fbReader = await fallbackCmd.ExecuteReaderAsync())
                        {
                            if (await fbReader.ReadAsync())
                            {
                                DateTime jDate = Convert.ToDateTime(fbReader["JourneyDate"]);
                                TimeSpan depTime = fbReader["Departure"] != DBNull.Value ? (TimeSpan)fbReader["Departure"] : TimeSpan.Zero;
                                var indiaTimeNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                                DateTime depDateTime = jDate.Date + depTime;
                                TimeSpan diff = depDateTime - indiaTimeNow;

                                string pnrStr = fbReader["PNR"].ToString();
                                bool isChartPrepared = diff.TotalHours <= 8 && diff.TotalHours >= -24;
                                string chartingStatus = isChartPrepared ? "Chart Prepared" : "Chart Not Prepared";
                                bool canCancel = diff.TotalMinutes > 30 && diff.TotalSeconds > 0 && fbReader["Status"].ToString() != "CANCELLED";

                                var fallbackBooking = new
                                {
                                    bookingId = id,
                                    pnr = pnrStr,
                                    trainName = fbReader["TrainName"].ToString(),
                                    trainNo = fbReader["TrainNumber"].ToString(),
                                    from = fbReader["Frmst"].ToString(),
                                    to = fbReader["Tost"].ToString(),
                                    journeyDate = jDate.ToString("dd MMM yyyy"),
                                    rawJourneyDate = jDate.ToString("yyyy-MM-dd"),
                                    journeyFull = "",
                                    bookingDate = fbReader["BookingDate"] != DBNull.Value ? Convert.ToDateTime(fbReader["BookingDate"]).ToString("dd MMM yyyy | hh:mm tt") : "",
                                    quota = fbReader["Quota"] != DBNull.Value ? fbReader["Quota"].ToString() : "General",
                                    convenienceFees = 17.70m,
                                    insurance = 0.45m,
                                    className = fbReader["Class"].ToString(),
                                    baseFare = fbReader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(fbReader["BaseFare"]) : 0.00m,
                                    gst = 0.00m,
                                    quotaCharge = 0.00m,
                                    surge = 0.00m,
                                    fare = fbReader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(fbReader["TotalFare"]) : 0.00m,
                                    departure = fbReader["Departure"] != DBNull.Value ? fbReader["Departure"].ToString() : "",
                                    arrival = fbReader["Arrival"] != DBNull.Value ? fbReader["Arrival"].ToString() : "",
                                    duration = fbReader["Duration"] != DBNull.Value ? fbReader["Duration"].ToString() : "",
                                    status = fbReader["Status"] != DBNull.Value ? fbReader["Status"].ToString() : "CONFIRMED",
                                    isChartPrepared = isChartPrepared,
                                    chartingStatus = chartingStatus,
                                    canCancel = canCancel
                                };

                                fbReader.Close();

                                var fallbackPassengers = new List<object>();
                                using (var pCmd = new SqlCommand("SELECT * FROM Passengers WHERE BookingId = @BId ORDER BY Id", conn))
                                {
                                    pCmd.Parameters.AddWithValue("@BId", id);
                                    using (var pRdr = await pCmd.ExecuteReaderAsync())
                                    {
                                        while (await pRdr.ReadAsync())
                                        {
                                            string meal = "";
                                            try { if (pRdr["Meal"] != DBNull.Value) meal = pRdr["Meal"].ToString(); } catch { }

                                            fallbackPassengers.Add(new
                                            {
                                                id = pRdr["Id"],
                                                name = pRdr["Name"].ToString(),
                                                age = pRdr["Age"],
                                                gender = pRdr["Gender"].ToString(),
                                                bookingStatus = pRdr["BookingStatus"] != DBNull.Value ? pRdr["BookingStatus"].ToString() : "CNF",
                                                currentStatus = pRdr["CurrentStatus"] != DBNull.Value ? pRdr["CurrentStatus"].ToString() : "CNF",
                                                berth = pRdr["Berth"] != DBNull.Value ? pRdr["Berth"].ToString() : "",
                                                seat = pRdr["SeatNumber"] != DBNull.Value ? pRdr["SeatNumber"].ToString() : "",
                                                meal = meal,
                                                Position = pRdr.IsDBNull(pRdr.GetOrdinal("Position")) ? (int?)null : pRdr.GetInt32(pRdr.GetOrdinal("Position"))
                                            });
                                        }
                                    }
                                }

                                return Json(new { success = true, booking = fallbackBooking, passengers = fallbackPassengers });
                            }
                        }
                    }

                    return Json(new { success = false, message = "Booking details not found." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> UpdateMealPreference(int bookingId, string meal)
        {
            if (string.IsNullOrWhiteSpace(meal))
            {
                return Json(new { success = false, message = "Please select a valid meal preference." });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var chkCmd = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Passengers') AND name = 'Meal') ALTER TABLE Passengers ADD Meal NVARCHAR(50) NULL", conn))
                    {
                        await chkCmd.ExecuteNonQueryAsync();
                    }

                    using (var upCmd = new SqlCommand("UPDATE Passengers SET Meal = @Meal WHERE BookingId = @BId", conn))
                    {
                        upCmd.Parameters.AddWithValue("@Meal", meal);
                        upCmd.Parameters.AddWithValue("@BId", bookingId);
                        int rows = await upCmd.ExecuteNonQueryAsync();
                        return Json(new { success = true, message = $"Meal preference successfully updated to {meal} for {rows} passenger(s)." });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update meal: " + ex.Message });
            }
        }

        //private async Task SendCancellationEmail(string userEmail, string pnr)
        //{
        //    string subject = "IRCTC Clone – Ticket Cancellation Confirmation";

        //    string body = $@"
        //    <h3>Ticket Cancelled Successfully</h3>
        //    <p>Dear Customer,</p>
        //    <p>We wish to inform you that your ticket against PNR Number: {pnr} has been cancelled successfully as per your request.</p>
        //    <p>The refund amount of Rs. will be refunded back to your respective account shortly.</p>
        //    <p>In case you require any further assistance, please raise your query at https://equery.irctc.co.in or call us at 14646 / 08044647999 / 08035734999 ( 24*7 Hrs ; Language: Hindi or English).</p>
        //    <br/>
        //    <p>Regards,<br/>IRCTC Clone Team</p>
        //    <p>Time: {DateTime.Now}</p>";

        //    await _emailService.SendEmail(userEmail, subject, body);
        //}

        private async Task SendCancellationEmail(string userEmail, string pnr, decimal refundAmount)
        {
            string subject = "IRCTC Clone – Ticket Cancellation Confirmation";

            string body = $@"
            <div style='font-family: Arial, Helvetica, sans-serif;
                        background:#f4f7fb;
                        padding:30px;
                        color:#333;'>

                <div style='max-width:700px;
                            margin:auto;
                            background:white;
                            border-radius:10px;
                            overflow:hidden;
                            box-shadow:0 4px 12px rgba(0,0,0,0.12);'>

                    <!-- HEADER -->
                    <div style='background:#0b3d91;
                                color:white;
                                padding:20px 30px;'>

                        <h2 style='margin:0;'>
                            🚆 IRCTC Clone
                        </h2>

                        <p style='margin-top:8px;
                                    font-size:14px;
                                    opacity:0.9;'>
                            Ticket Cancellation Confirmation
                        </p>
                    </div>

                    <!-- BODY -->
                    <div style='padding:30px;'>

                        <div style='background:#fff3f3;
                                    border-left:5px solid #dc3545;
                                    padding:15px;
                                    border-radius:6px;
                                    margin-bottom:25px;'>

                            <h3 style='margin:0;
                                        color:#dc3545;'>
                                Ticket Cancelled Successfully
                            </h3>

                            <p style='margin-top:10px;
                                        font-size:14px;'>
                                Your train ticket has been cancelled successfully.
                            </p>
                        </div>

                        <p style='font-size:15px;'>
                            Dear Customer,
                        </p>

                        <p style='font-size:15px;
                                    line-height:1.7;'>
                            Your booking with
                            <b>PNR Number: {pnr}</b>
                            has been cancelled successfully.
                        </p>

                        <!-- DETAILS BOX -->
                        <table style='width:100%;
                                        border-collapse:collapse;
                                        margin-top:20px;
                                        margin-bottom:25px;'>

                            <tr style='background:#f8f9fa;'>
                                <td style='padding:12px;
                                            border:1px solid #ddd;
                                            font-weight:bold;'>
                                    Refund Amount
                                </td>

                                <td style='padding:12px;
                                            border:1px solid #ddd;
                                            color:green;
                                            font-weight:bold;'>
                                    ₹{refundAmount:F2}
                                </td>
                            </tr>

                            <tr>
                                <td style='padding:12px;
                                            border:1px solid #ddd;
                                            font-weight:bold;'>
                                    Refund Status
                                </td>

                                <td style='padding:12px;
                                            border:1px solid #ddd;'>
                                    Amount will be credited shortly
                                </td>
                            </tr>

                            <tr style='background:#f8f9fa;'>
                                <td style='padding:12px;
                                            border:1px solid #ddd;
                                            font-weight:bold;'>
                                    Cancellation Time
                                </td>

                                <td style='padding:12px;
                                            border:1px solid #ddd;'>
                                    {DateTime.Now:dd-MMM-yyyy hh:mm tt}
                                </td>
                            </tr>

                        </table>

                        <!-- NOTE -->
                        <div style='background:#fff8e6;
                                    border-left:5px solid #ff9800;
                                    padding:15px;
                                    border-radius:6px;
                                    margin-bottom:20px;'>

                            <p style='margin:0;
                                        font-size:14px;
                                        line-height:1.6;'>

                                <b>Important:</b>
                                For chart prepared tickets,
                                please file a TDR request as per railway rules.
                            </p>
                        </div>

                        <p style='margin-top:30px;
                                    font-size:14px;'>
                            Regards,
                            <br/>
                            <b>IRCTC Clone Team</b>
                        </p>

                    </div>

                    <!-- FOOTER -->
                    <div style='background:#f1f1f1;
                                padding:15px;
                                text-align:center;
                                font-size:12px;
                                color:#666;'>

                        © 2026 IRCTC Clone • Safe • Secure • Reliable
                    </div>

                </div>

            </div>";

            await _emailService.SendEmail(userEmail, subject, body);
        }

        [HttpGet]
        public IActionResult GetRefundDetails(string pnr)
        {
            var booking = GetBookingByPNR(pnr);

            if (booking == null)
                return Json(new { success = false, message = "Booking not found" });

            var result = CalculateRefund(booking);

            return Json(new
            {
                success = true,
                pnr = booking.PNR,
                fare = booking.BaseFare,
                deduction = result.deduction,
                refundAmount = result.refund,
                message = result.message
            });
        }

        private (decimal deduction, decimal refund, string message) CalculateRefund(Booking booking)
        {
            decimal fare = booking.BaseFare;
            string classCode = booking.Class?.ToUpper()?.Trim();

            // 🔧 Extract code from "Sleeper (SL)"
            if (!string.IsNullOrEmpty(classCode) && classCode.Contains("("))
            {
                int start = classCode.IndexOf("(") + 1;
                int end = classCode.IndexOf(")");
                classCode = classCode.Substring(start, end - start);
            }
            string status = booking.Status?.ToUpper();

            DateTime journeyDateTime = booking.JourneyDate.Add(booking.Departure);
            double hoursLeft = (journeyDateTime - DateTime.Now).TotalHours;

            decimal flat = GetFlatDeduction(classCode);
            decimal deduction = 0;

            // 🟡 WAITLIST / RAC (handled first)
            if (status == "WL" || status == "RAC")
            {
                deduction = 60;
                return (deduction, fare - deduction, "Clerkage charge applied (WL/RAC).");
            }

            // 🔴 TATKAL (confirmed)
            if (booking.Quota == "TATKAL" && status == "CNF")
            {
                return (fare, 0, "No refund for confirmed Tatkal ticket.");
            }

            // 🚫 AFTER CHART PREPARATION
            if (hoursLeft < 8)
            {
                return (fare, 0, "Chart prepared. Please file TDR.");
            }

            // 🟢 CONFIRMED TICKETS
            if (hoursLeft > 48)
            {
                deduction = flat;
            }
            else if (hoursLeft > 12)
            {
                deduction = Math.Max(fare * 0.25m, flat);
            }
            else // 12 to 4 hours
            {
                deduction = Math.Max(fare * 0.50m, flat);
            }

            decimal refund = fare - deduction;

            return (deduction, refund, "Refund calculated as per IRCTC rules.");
        }
        
        private decimal GetFlatDeduction(string classCode)
        {
            return classCode switch
            {
                "1A" or "EC" => 240,
                "2A" or "FC" => 200,
                "3A" or "CC" or "3E" => 180,
                "SL" => 120,
                "2S" => 60,
                _ => 60
            };
        }

        private Booking GetBookingByPNR(string pnr)
        {
            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string query = "SELECT TOP 1 * FROM Bookings WHERE PNR = @PNR";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@PNR", pnr);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                PNR = reader["PNR"].ToString(),
                                BaseFare = Convert.ToDecimal(reader["BaseFare"]),
                                JourneyDate = Convert.ToDateTime(reader["JourneyDate"]),
                                TicketStatus = reader["TicketStatus"]?.ToString(),
                                Status = reader["Status"]?.ToString(),

                                // ✅ ADD THESE (VERY IMPORTANT)
                                Class = reader["Class"]?.ToString(),
                                Quota = reader["Quota"]?.ToString(),
                                Departure = reader["Departure"] != DBNull.Value
                                ? (TimeSpan)reader["Departure"]
                                : TimeSpan.Zero
                            };
                        }
                    }
                }
            }

            return booking;
        }

        [HttpGet]
        public IActionResult DownloadTicket(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var result = _emailService.GeneratePdf(id, userId);

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

        private List<Booking> GetUpcomingJourneys(string userId)
        {
            var bookings = new List<Booking>();
            if (string.IsNullOrEmpty(userId)) return bookings;

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("spGetUserBookingHistory", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@UserId", userId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var journeyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate"));
                                var status = reader.GetString(reader.GetOrdinal("Status"));

                                if (journeyDate.Date >= DateTime.Today && status != "CANCELLED")
                                {
                                    bookings.Add(new Booking
                                    {
                                        BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                        PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                        JourneyDate = journeyDate,
                                        BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                        Status = status,
                                        TicketStatus = reader.GetString(reader.GetOrdinal("TicketStatus")),
                                        TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                        TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                        ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
                                        FromStation = reader.GetString(reader.GetOrdinal("FromStation")),
                                        ToStation = reader.GetString(reader.GetOrdinal("ToStation")),
                                        Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                        Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                        Duration = reader.GetString(reader.GetOrdinal("Duration"))
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Gracefully ignore database connection errors for upcoming journeys panel
            }

            return bookings;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult CheckPNR()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.UpcomingJourneys = GetUpcomingJourneys(userId);
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult CheckPNR(string pnr)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.UpcomingJourneys = GetUpcomingJourneys(userId);

            if (string.IsNullOrEmpty(pnr))
            {
                ViewBag.Error = "Please enter a valid PNR number.";
                return View("CheckPNR");
            }

            pnr = pnr.Trim();
            Booking booking = null;

            try
            {
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
                            if (booking != null && reader.NextResult())
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
            }
            catch (Exception)
            {
                // Fallback to mock data if db query fails
            }

            // Mock details for demonstration PNRs if they are not in the database
            if (booking == null)
            {
                if (pnr == "4758973261")
                {
                    booking = new Booking
                    {
                        Id = 9991,
                        PNR = "4758973261",
                        TrainNumber = 12797,
                        TrainName = "VENKATADRI SF",
                        Frmst = "KACHEGUDA",
                        Tost = "RENIGUNTA JN",
                        ClassCode = "SL",
                        JourneyDate = new DateTime(2026, 7, 31),
                        BaseFare = 800.00m,
                        TotalFare = 800.00m,
                        Quota = "SS (LOWER BERTH/SR.CITIZEN)",
                        Status = "Chart Not Prepared",
                        Passengers = new List<Passenger>
                        {
                            new Passenger { Name = "V SASANK", Age = 61, Gender = "M", Coach = "S3", SeatNumber = "S3-17", Berth = "LB", BookingStatus = "CNF/S3/17/LB", CurrentStatus = "CNF" },
                            new Passenger { Name = "T KANAKA DURGA", Age = 52, Gender = "F", Coach = "S3", SeatNumber = "S3-20", Berth = "LB", BookingStatus = "CNF/S3/20/LB", CurrentStatus = "CNF" }
                        }
                    };
                }
                else if (pnr == "4551299941")
                {
                    booking = new Booking
                    {
                        Id = 9992,
                        PNR = "4551299941",
                        TrainNumber = 12798,
                        TrainName = "VENKATADRI SF",
                        Frmst = "RENIGUNTA JN",
                        Tost = "KACHEGUDA",
                        ClassCode = "SL",
                        JourneyDate = new DateTime(2026, 8, 2),
                        BaseFare = 800.00m,
                        TotalFare = 800.00m,
                        Quota = "SS (LOWER BERTH/SR.CITIZEN)",
                        Status = "Chart Not Prepared",
                        Passengers = new List<Passenger>
                        {
                            new Passenger { Name = "V SASANK", Age = 61, Gender = "M", Coach = "S3", SeatNumber = "S3-17", Berth = "LB", BookingStatus = "CNF/S3/17/LB", CurrentStatus = "CNF" },
                            new Passenger { Name = "T KANAKA DURGA", Age = 52, Gender = "F", Coach = "S3", SeatNumber = "S3-20", Berth = "LB", BookingStatus = "CNF/S3/20/LB", CurrentStatus = "CNF" }
                        }
                    };
                }
            }

            if (booking == null)
            {
                ViewBag.Error = " ❌ PNR Flushed / Booking Cancelled. ";
                return View("CheckPNR");
            }

            return View("CheckPNR", booking);
        }

        [HttpGet]
        public async Task<IActionResult> GetNext7Days(int trainId, string travelClass)
        {
            var data = await _availabilityService.GetNext7DaysAsync(trainId, travelClass);
            return Json(data);
        }


        [HttpPost]
        public async Task<IActionResult> Reschedule([FromBody] RescheduleRequest request)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand("spRescheduleBooking", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@BookingId", request.BookingId);
                        cmd.Parameters.AddWithValue("@NewTravelDate", request.NewTravelDate);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "Ticket rescheduled successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetAvailability(int trainId, string classCode, DateTime travelDate)
        {
            int availableSeats = 0;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();

                using (SqlCommand cmd = new SqlCommand("spGetSeatAvailability", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassCode", classCode);
                    cmd.Parameters.AddWithValue("@TravelDate", travelDate);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            int col = reader.GetOrdinal("AvailableSeats");

                            if (!reader.IsDBNull(col))
                                availableSeats = reader.GetInt32(col);
                        }
                    }
                }
            }

            return Json(new { availableSeats = availableSeats });
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult GetSeatAvailability(int trainId, int classId, DateTime journeyDate, string quota)
        {
            quota = quota?.ToUpper() ?? "GENERAL";
            string status = "";
            int count = 0;

            if (quota == "TATKAL" && TatkalHelper.IsTatkalWindow())
            {
                int tatkalClickCount =
                    HttpContext.Session.GetInt32("TatkalAvailabilityClicks") ?? 0;

                // 🔴 BLOCK AFTER 10 CLICKS
                if (tatkalClickCount >= 10)
                {
                    return RedirectToAction("Error429", "Error");
                }

                // ✅ INCREMENT
                tatkalClickCount++;

                HttpContext.Session.SetInt32(
                    "TatkalAvailabilityClicks",
                    tatkalClickCount
                );
            }

            if (!ReservationHelper.IsDateAllowed(journeyDate))
            {
                return Json(new
                {
                    status = "ARP_BLOCKED",
                    message = "This action not allowed as the Date given is Outside Advance Reservation Period",
                    code = 80012
                });
            }

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // 🔥 CHECK SERVICE VALIDITY AND 3-DAY ADVANCE BOOKING RULE
                try
                {
                    using (SqlCommand serviceCmd = new SqlCommand("SELECT ServiceStartDate, ServiceEndDate FROM Trains WHERE Id = @TrainId", conn))
                    {
                        serviceCmd.Parameters.AddWithValue("@TrainId", trainId);
                        using (SqlDataReader sReader = serviceCmd.ExecuteReader())
                        {
                            if (sReader.Read())
                            {
                                DateTime? serviceStart = sReader.IsDBNull(0) ? null : sReader.GetDateTime(0);
                                DateTime? serviceEnd = sReader.IsDBNull(1) ? null : sReader.GetDateTime(1);

                                var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");

                                if (serviceEnd.HasValue && journeyDate.Date > serviceEnd.Value.Date)
                                {
                                    return Json(new
                                    {
                                        status = "BOOKING_NOT_ALLOWED",
                                        message = "BOOKINGs are NOT ALLOWED at this TIME"
                                    });
                                }

                                if (serviceStart.HasValue)
                                {
                                    DateTime bookingOpenDate = serviceStart.Value.Date.AddDays(-3);
                                    if (istNow.Date < bookingOpenDate)
                                    {
                                        return Json(new
                                        {
                                            status = "BOOKING_NOT_ALLOWED",
                                            message = "BOOKINGs are NOT ALLOWED at this TIME"
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error checking service dates: " + ex.Message);
                }

                // 🔥 CHECK TRAIN CANCELLED
                using (SqlCommand cancelCmd = new SqlCommand("spCheckTrainCancelled", conn))
                {
                    cancelCmd.CommandType = CommandType.StoredProcedure;

                    cancelCmd.Parameters.AddWithValue("@TrainId", trainId);
                    cancelCmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    Console.WriteLine("TrainId: " + trainId);
                    Console.WriteLine("JourneyDate from UI: " + journeyDate.ToString("yyyy-MM-dd"));

                    int cancelCount = (int)cancelCmd.ExecuteScalar();
                    Console.WriteLine("CancelCount: " + cancelCount);

                    if (cancelCount > 0)
                    {
                        return Json(new { status = "TRAIN_CANCELLED" });
                    }
                }

                // 🔥 STEP 1: Get departure time
                TimeSpan departureTime = TimeSpan.Zero;

                using (SqlCommand timeCmd = new SqlCommand("spGetTrainDepartureTime", conn))
                {
                    timeCmd.CommandType = CommandType.StoredProcedure;

                    timeCmd.Parameters.AddWithValue("@TrainId", trainId);

                    var result = timeCmd.ExecuteScalar();

                    if (result != null)
                    {
                        departureTime = (TimeSpan)result;
                    }
                }

                // 🔥 STEP 2: Combine date + time
                DateTime departureDateTime = journeyDate.Date + departureTime;

                // 🔥 STEP 3: Check 8-hour rule
                // 🔥 CURRENT TIME (IST)
                var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                    DateTime.UtcNow,
                    "India Standard Time"
                );

                // 🔥 TIME LEFT
                TimeSpan timeToDeparture = departureDateTime - indiaTime;

                // 🚫 If departed
                if (timeToDeparture.TotalSeconds <= 0)
                {
                    return Json(new
                    {
                        status = "TRAIN_DEPARTED",
                        count = 0,
                        isTatkalOpen = false
                    });
                }

                // 🚫 Final Charting Rule: Within 30 minutes of departure (Booking Closed)
                if (timeToDeparture.TotalMinutes <= 30)
                {
                    return Json(new
                    {
                        status = "CHART_PREPARED",
                        count = 0,
                        isTatkalOpen = false
                    });
                }

                if (quota == "TATKAL")
                {
                    bool isAllowed = TatkalHelper.IsTatkalAllowed(journeyDate);

                    if (!isAllowed)
                    {
                        return Json(new
                        {
                            status = "TATKAL_NOT_ALLOWED",
                            message = "Date outside Tatkal ARP",
                            code = 50018
                        });
                    }
                }

                string classCode = "";

                using (SqlCommand classCmd = new SqlCommand("spGetTrainClassCode", conn))
                {
                    classCmd.CommandType = CommandType.StoredProcedure;

                    classCmd.Parameters.AddWithValue("@ClassId", classId);
                    var result = classCmd.ExecuteScalar();
                    if (result != null)
                        classCode = result.ToString();
                }

                string extractedCode = "";

                if (classCode.Contains("(") && classCode.Contains(")"))
                {
                    int start = classCode.IndexOf("(") + 1;
                    int end = classCode.IndexOf(")");
                    extractedCode = classCode.Substring(start, end - start);
                }
                else
                {
                    extractedCode = classCode;
                }

                extractedCode = extractedCode.Trim().ToUpper();

                bool isTatkalOpen = TatkalHelper.IsTatkalOpen(extractedCode);

                int available = 0;
                int rac = 0;
                int wl = 0;

                using (SqlCommand cmd = new SqlCommand("spGetSeatStatusCounts", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);
                    cmd.Parameters.AddWithValue("@Quota", quota);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            if (quota == "TATKAL")
                            {
                                int tatkalSeats = (reader["TatkalSeats"] != null && reader["TatkalSeats"] != DBNull.Value) ? Convert.ToInt32(reader["TatkalSeats"]) : 0;
                                available = tatkalSeats;
                                wl = (reader["WLCount"] != null && reader["WLCount"] != DBNull.Value) ? Convert.ToInt32(reader["WLCount"]) : 0;
                            }
                            else
                            {
                                available = (reader["SeatsAvailable"] != null && reader["SeatsAvailable"] != DBNull.Value) ? Convert.ToInt32(reader["SeatsAvailable"]) : 0;
                                rac = (reader["RACCount"] != null && reader["RACCount"] != DBNull.Value) ? Convert.ToInt32(reader["RACCount"]) : 0;
                                wl = (reader["WLCount"] != null && reader["WLCount"] != DBNull.Value) ? Convert.ToInt32(reader["WLCount"]) : 0;
                            }
                        }
                    }
                }

                if (quota == "TATKAL")
                {
                    bool isFirstAc = extractedCode == "1A" || classCode.Contains("1A") || extractedCode == "EA" || classCode.Contains("FIRST AC");
                    if (isFirstAc)
                    {
                        string tatkalStatus = isTatkalOpen ? "NOT AVAILABLE" : "NOT AVAILABLE#";
                        return Json(new
                        {
                            status = tatkalStatus,
                            count = 0,
                            isTatkalOpen = isTatkalOpen
                        });
                    }

                    if (available > 0)
                    {
                        status = "AVAILABLE";
                        count = available;
                    }
                    else if (wl > 0)
                    {
                        status = "WL";
                        count = wl;
                    }
                    else
                    {
                        status = "NOT AVAILABLE";
                        count = 0;
                    }
                    return Json(new
                    {
                        status = status,
                        count = count,
                        isTatkalOpen = isTatkalOpen
                    });
                }

                // 🟢 NORMAL BOOKING - CHECK BOOKING OPEN CONTROL
                try
                {
                    using (SqlCommand bookingCmd = new SqlCommand("spFetchBookingSettings", conn))
                    {
                        bookingCmd.CommandType = CommandType.StoredProcedure;
                        bookingCmd.Parameters.AddWithValue("@TrainId", trainId);

                        using (SqlDataReader bReader = bookingCmd.ExecuteReader())
                        {
                            if (bReader.Read())
                            {
                                if (bReader["BookingOpenDate"] != DBNull.Value
                                    && bReader["IsBookingEnabled"] != DBNull.Value
                                    && Convert.ToBoolean(bReader["IsBookingEnabled"]))
                                {
                                    DateTime bookingOpenDate = Convert.ToDateTime(bReader["BookingOpenDate"]);
                                    TimeSpan bookingTime = bReader["BookingWindowTime"] == DBNull.Value
                                        ? new TimeSpan(8, 0, 0)
                                        : (TimeSpan)bReader["BookingWindowTime"];

                                    DateTime openDateTime = bookingOpenDate.Date + bookingTime;

                                    if (indiaTime < openDateTime)
                                    {
                                        return Json(new
                                        {
                                            status = "BOOKING_NOT_ALLOWED"
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore if settings stored procedure is not configured
                }

                var seatStatus = GetSeatStatus(trainId, classId, journeyDate, quota);

                int remainingSeats = seatStatus?.SeatsAvailable ?? available;
                int racRemaining = seatStatus?.RACSeats ?? rac;
                int finalWl = seatStatus?.WLCount ?? wl;

                bool isFirstChart = timeToDeparture.TotalHours <= 8 && timeToDeparture.TotalMinutes > 30;

                if (isFirstChart)
                {
                    // Current booking opens for vacant seats until final charting window
                    if (remainingSeats > 0 && quota != "TATKAL")
                    {
                        status = "CURR_AVBL";
                        count = remainingSeats;
                    }
                    else
                    {
                        status = "CHART_PREPARED";
                        count = 0;
                    }
                }
                else if (remainingSeats > 0)
                {
                    status = "AVAILABLE";
                    count = remainingSeats;
                }
                else if (quota != "TATKAL" && racRemaining > 0)
                {
                    status = "RAC";
                    count = racRemaining;
                }
                else if (finalWl > 0)
                {
                    status = "WL";
                    count = finalWl;
                }
                else
                {
                    status = "NOT AVAILABLE";
                    count = 0;
                }

                return Json(new
                {
                    status = status,
                    count = count,
                    isTatkalOpen = isTatkalOpen
                });
            }

            return Json(new { seatsAvailable = 0 });
        }


        [HttpGet]
        public IActionResult GetLatestSeatAvailability(
            int trainId,
            int classId,
            DateTime journeyDate,
            string quota)
        {
            try
            {
                return GetSeatAvailability(
                    trainId,
                    classId,
                    journeyDate,
                    quota
                );
            }
            catch
            {
                return Json(new
                {
                    status = "Unavailable",
                    count = 0
                });
            }
        }

        private decimal CalculateFare(int trainId, int fromStationId, int toStationId, string classCode)
        {
            decimal fare = 0;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand("spCalculateFare", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add("@TrainId", SqlDbType.Int).Value = trainId;
                cmd.Parameters.Add("@FromStationId", SqlDbType.Int).Value = fromStationId;
                cmd.Parameters.Add("@ToStationId", SqlDbType.Int).Value = toStationId;
                cmd.Parameters.Add("@ClassCode", SqlDbType.NVarChar, 10).Value = classCode;

                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        fare = reader["Fare"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["Fare"]);
                    }
                }
            }

            return fare;
        }

        [HttpPost]
        public async Task<IActionResult> EditPassenger([FromBody] PassengerEditRequest request)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand("spEditPassenger", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@PassengerId", request.Id);
                        cmd.Parameters.AddWithValue("@Name", request.Name);
                        cmd.Parameters.AddWithValue("@Age", request.Age);
                        cmd.Parameters.AddWithValue("@Gender", request.Gender);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return Json(new { success = true, message = "Passenger updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private Booking GetBookingById(int bookingId)
        {
            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetBookingById", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@BookingId", bookingId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                PNR = reader["PNR"].ToString(),
                                BaseFare = reader["BaseFare"] != DBNull.Value
                                    ? Convert.ToDecimal(reader["BaseFare"])
                                    : 0,

                                JourneyDate = reader["JourneyDate"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["JourneyDate"])
                                    : DateTime.Now,

                                Status = reader["Status"]?.ToString(),

                                TotalFare = reader["TotalFare"] != DBNull.Value
                                    ? Convert.ToDecimal(reader["TotalFare"])
                                    : 0
                            };
                        }
                    }
                }
            }

            return booking;
        }

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

        public string GetBerth(int seatNumber)
        {
            string[] berthCycle = { "LB", "MB", "UB", "SL", "SU" };

            return berthCycle[(seatNumber - 1) % berthCycle.Length];
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetPnrStatusDetails(string pnr, int bookingId)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(@"
                        SELECT TOP 1 b.Id, b.PNR, b.TrainId, b.TrainNumber, b.TrainName, b.Frmst, b.Tost, 
                                     b.JourneyDate, b.Departure, b.Arrival, b.Duration, b.Quota, b.Class, 
                                     b.Status, b.TicketStatus, b.BaseFare, b.TotalFare
                        FROM Bookings b
                        WHERE (b.PNR = @PNR OR b.Id = @BookingId)
                          AND (@UserId IS NULL OR @UserId = '' OR b.UserId = @UserId)", conn))
                    {
                        cmd.Parameters.AddWithValue("@PNR", pnr ?? "");
                        cmd.Parameters.AddWithValue("@BookingId", bookingId);
                        cmd.Parameters.AddWithValue("@UserId", userId ?? "");

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                            {
                                return Json(new { success = false, message = "PNR record not found or access denied." });
                            }

                            int foundId = Convert.ToInt32(reader["Id"]);
                            string foundPnr = reader["PNR"].ToString();
                            string trainName = reader["TrainName"].ToString();
                            string trainNo = reader["TrainNumber"].ToString();
                            string fromStn = reader["Frmst"].ToString();
                            string toStn = reader["Tost"].ToString();
                            DateTime jDate = Convert.ToDateTime(reader["JourneyDate"]);
                            TimeSpan depTime = reader["Departure"] != DBNull.Value ? (TimeSpan)reader["Departure"] : TimeSpan.Zero;
                            TimeSpan arrTime = reader["Arrival"] != DBNull.Value ? (TimeSpan)reader["Arrival"] : TimeSpan.Zero;
                            string classCode = reader["Class"].ToString();
                            string quota = reader["Quota"] != DBNull.Value ? reader["Quota"].ToString() : "GENERAL";
                            string status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : "CONFIRMED";
                            string ticketStatus = reader["TicketStatus"] != DBNull.Value ? reader["TicketStatus"].ToString() : status;

                            var indiaTimeNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");
                            DateTime depDateTime = jDate.Date + depTime;
                            TimeSpan diff = depDateTime - indiaTimeNow;

                            bool isChartPrepared = diff.TotalHours <= 8 && diff.TotalHours >= -4;
                            string chartingStatus = isChartPrepared ? "Chart Prepared" : "Chart Not Prepared";

                            reader.Close();

                            // Load passengers
                            var passengers = new List<object>();
                            using (var pCmd = new SqlCommand(@"
                                SELECT Id, Name, Age, Gender, BookingStatus, CurrentStatus, SeatNumber, Berth, Position
                                FROM Passengers
                                WHERE BookingId = @BId
                                ORDER BY Id", conn))
                            {
                                pCmd.Parameters.AddWithValue("@BId", foundId);
                                using (var pReader = await pCmd.ExecuteReaderAsync())
                                {
                                    while (await pReader.ReadAsync())
                                    {
                                        string bStatus = pReader["BookingStatus"] != DBNull.Value ? pReader["BookingStatus"].ToString() : "CNF";
                                        string cStatus = pReader["CurrentStatus"] != DBNull.Value ? pReader["CurrentStatus"].ToString() : bStatus;
                                        string seat = pReader["SeatNumber"] != DBNull.Value ? pReader["SeatNumber"].ToString() : "";
                                        string berth = pReader["Berth"] != DBNull.Value ? pReader["Berth"].ToString() : "";
                                        object pos = pReader["Position"] != DBNull.Value ? pReader["Position"] : null;

                                        passengers.Add(new
                                        {
                                            id = pReader["Id"],
                                            name = pReader["Name"].ToString(),
                                            age = pReader["Age"],
                                            gender = pReader["Gender"].ToString(),
                                            bookingStatus = bStatus,
                                            currentStatus = cStatus,
                                            seat = seat,
                                            berth = berth,
                                            position = pos
                                        });
                                    }
                                }
                            }

                            return Json(new
                            {
                                success = true,
                                pnr = foundPnr,
                                bookingId = foundId,
                                trainName = trainName,
                                trainNumber = trainNo,
                                journeyDate = jDate.ToString("dd MMM yyyy"),
                                rawJourneyDate = jDate.ToString("yyyy-MM-dd"),
                                departure = depTime.ToString(@"hh\:mm"),
                                arrival = arrTime.ToString(@"hh\:mm"),
                                from = fromStn,
                                to = toStn,
                                classCode = classCode,
                                quota = quota,
                                ticketStatus = ticketStatus,
                                isChartPrepared = isChartPrepared,
                                chartingStatus = chartingStatus,
                                passengers = passengers
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to fetch PNR status: " + ex.Message });
            }
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
