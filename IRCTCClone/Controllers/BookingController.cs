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
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Claims;
using static System.Net.Mime.MediaTypeNames;
using Font = iTextSharp.text.Font;
using Image = iTextSharp.text.Image;


namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]

    [Authorize]
    public class BookingController : Controller
    {
        private readonly string _connectionString;
/*        private readonly string _baseUrl;*/
        private readonly EmailService _emailService;

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

        private decimal CalculateFare(TrainClass cls, int totalSeats, int bookedSeats, string quota,
            out decimal gst, out decimal surge, out decimal quotaCharge)
        {
            decimal baseFare = cls.BaseFare;
            gst = Math.Round(baseFare * 0.05m, 2); // 5% GST
            surge = 0;
            quotaCharge = 0;

            // ✅ Dynamic pricing (surge)
            if (cls.DynamicPricing && totalSeats > 0)
            {
                decimal occupancyRate = (decimal)bookedSeats / totalSeats;
                if (occupancyRate > 0.7m)
                    surge = Math.Round(baseFare * 0.10m, 2); // 10% surge if >70% seats booked
                else if (occupancyRate > 0.5m)
                    surge = Math.Round(baseFare * 0.05m, 2); // 5% surge if >50%
            }

            // ✅ Official IRCTC Tatkal rates with min & max limits
            var tatkalConfig = new Dictionary<string, (decimal percent, decimal min, decimal max)>(StringComparer.OrdinalIgnoreCase)
            {
                { "2S", (0.10m, 10m, 15m) },    // Second Sitting: 10% or ₹10–₹15
                { "SL", (0.30m, 100m, 200m) },  // Sleeper: 30% or ₹100–₹200
                { "3A", (0.30m, 300m, 400m) },  // 3rd AC: 30% or ₹300–₹400
                { "2A", (0.30m, 400m, 500m) },  // 2nd AC: 30% or ₹400–₹500
                { "1A", (0.30m, 400m, 500m) },  // 1st AC: 30% or ₹400–₹500
                { "CC", (0.30m, 125m, 225m) },  // AC Chair Car: 30% or ₹125–₹225
                { "EC", (0.30m, 400m, 500m) }   // Executive Chair Car: 30% or ₹400–₹500
            };

            // ✅ Quota-based adjustments
            switch (quota.ToUpper())
            {
                case "TATKAL":
                    if (tatkalConfig.TryGetValue(cls.Code.ToUpper(), out var config))
                    {
                        decimal tatkalAmount = baseFare * config.percent;
                        // Apply IRCTC min/max range
                        quotaCharge = Math.Round(Math.Min(Math.Max(tatkalAmount, config.min), config.max), 2);
                    }
                    else
                    {
                        quotaCharge = Math.Round(baseFare * 0.30m, 2); // fallback if unknown class
                    }
                    break;

                case "LADIES":
                    quotaCharge = Math.Round(baseFare * 0.05m, 2); // 5% extra
                    break;

                case "SC": // Senior Citizen
                    quotaCharge = Math.Round(-baseFare * 0.10m, 2); // 10% discount
                    break;

                default:
                    quotaCharge = 0;
                    break;
            }

            // ✅ Final Fare calculation
            decimal finalFare = baseFare + gst + surge + quotaCharge;
            return finalFare;
        }


        public BookingController(IConfiguration configuration, EmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _emailService = emailService;
/*            _baseUrl = configuration["AppSettings:BaseUrl"];*/
        }

        // GET: /Booking/Checkout
        [Authorize]
        [HttpGet]
        public IActionResult Checkout(int trainId, int classId, string journeyDate)
        {
            Train train = null;
            TrainClass cls = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetTrainForCheckout", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // Read train details
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

                        // Move to next result set for class
                        if (reader.NextResult() && reader.Read())
                        {
                            cls = new TrainClass
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                TrainId = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                Code = reader.GetString(reader.GetOrdinal("Code")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                SeatsAvailable = reader.GetInt32(reader.GetOrdinal("SeatsAvailable"))
                            };
                        }
                    }
                }
            }

            if (train == null || cls == null)
                return NotFound();

            ViewBag.Train = train;
            ViewBag.Class = cls;
            ViewBag.JourneyDate = journeyDate;

            return View("Checkout");
        }


        /*        [HttpPost]
                public IActionResult PayViaQR(int trainId, int classId, DateTime journeyDate, decimal fare)
                {
                    // Create view model for QR page
                    var model = new Booking
                    {
                        TrainId = trainId,
                        ClassId = classId,
                        JourneyDate = journeyDate,
                        TotalFare = fare
                    };

                    return View(model);
                }

                [HttpPost]
                public IActionResult ConfirmPayment(Booking model)
                {
                    // ✅ Validate data (optional)
                    if (model == null || model.TotalFare <= 0)
                        return BadRequest("Invalid payment details");

                    // ✅ Pass to confirmation page
                    return View("Confirmation", model);
                }*/


        [EnableRateLimiting("BookingLimiter")]
        // POST: /Booking/Confirm
        [HttpPost]
        public IActionResult Confirm(
            int trainId, int classId, string journeyDate,
            List<string> passengerNames, List<int> passengerAges, List<string> passengerGenders, List<string> passengerBerths,
            string trainName, string trainNumber, string Class,
            int FromStationId, int ToStationId)
        {
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
                int seatsAvailable = 0;
                int totalSeats = 0;
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

                            seatsAvailable = cls.SeatsAvailable;
                            totalSeats = cls.SeatsAvailable;
                        }
                    }
                }


                /*                // 🚆 Calculate fare dynamically
                                int bookedSeats = GetBookedSeatsCount(trainId, classId); // write stored proc
                                string quota = Request.Form["Quota"]; // from dropdown in form
                                fare = CalculateFare(cls, totalSeats, bookedSeats, quota);*/


                if (cls == null || string.IsNullOrEmpty(cls.Code))
                {
                    TempData["Error"] = "Train class not found.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                int passengerCount = passengerNames.Count;
                if (seatsAvailable < passengerCount)
                {
                    TempData["Error"] = "Not enough seats available.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                /*// Get how many seats are already booked for this train+class (stored proc should return INT)
                int bookedSeats = GetBookedSeatsCount(trainId, classId);

                // Get quota selection from form (if not provided default to GENERAL)
                string quota = Request.Form["Quota"];
                if (string.IsNullOrWhiteSpace(quota)) quota = "GENERAL";
               *//* quota = quota.ToUpper();*//*

                // Calculate fare per seat (includes GST and tatkal/surge)
                decimal gstPerSeat;
                decimal surgePerSeat;
                decimal farePerSeat = CalculateFare(cls, cls.SeatsAvailable, bookedSeats, quota, out gstPerSeat, out surgePerSeat);*/


                int bookedSeats = GetBookedSeatsCount(trainId, classId);
                string quota = Request.Form["Quota"].FirstOrDefault();
                quota = string.IsNullOrWhiteSpace(quota) ? null : quota.ToUpper();

                if (string.IsNullOrWhiteSpace(quota))
                {
                    quota = null; // or handle error if quota is mandatory
                }
                else
                {
                    quota = quota.ToUpper();
                }

                // Dynamic fare calculation
                decimal gst, surge, quotaCharge;
                decimal farePerPassenger = CalculateFare(cls, cls.SeatsAvailable, bookedSeats, quota, out gst, out surge, out quotaCharge);

                // Totals per passenger
                decimal totalBaseFare = cls.BaseFare * passengerCount;       // base fare only
                decimal totalQuotaCharge = quotaCharge * passengerCount;    // includes Tatkal, Ladies, Senior, or other quota adjustments
                decimal totalGst = gst * passengerCount;                    // GST total
                decimal totalSurge = surge * passengerCount;                // Surge total

                // Final total fare for all passengers
                decimal totalFare = totalBaseFare + totalQuotaCharge + totalGst + totalSurge;

                // Update TrainClass seats
                using (var cmd = new SqlCommand("UpdateTrainClassSeats", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@ClassId", cls.Id);
                    cmd.Parameters.AddWithValue("@SeatsBooked", passengerCount);
                    cmd.ExecuteNonQuery();
                }

                // Insert Booking
                int bookingId;
                string pnr = GeneratePnr();
/*                decimal totalBaseFare = farePerSeat * passengerCount;*/

                using (var cmd = new SqlCommand("InsertBooking", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PNR", pnr);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainClassId", cls.Id);
                    cmd.Parameters.AddWithValue("@JourneyDate", DateTime.Parse(journeyDate));
                    cmd.Parameters.AddWithValue("@BookingDate", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@BaseFare", totalBaseFare * passengerCount);
                    cmd.Parameters.AddWithValue("@Status", "CONFIRMED");
                    cmd.Parameters.AddWithValue("@TrainNumber", trainNumber);
                    cmd.Parameters.AddWithValue("@TrainName", trainName);
                    cmd.Parameters.AddWithValue("@Class", Class);
                    cmd.Parameters.AddWithValue("@Frmst", fromStation.Name);
                    cmd.Parameters.AddWithValue("@Tost", toStation.Name);
                    cmd.Parameters.AddWithValue("@Quota", quota);
                    cmd.Parameters.AddWithValue("@GST", totalGst);
                    cmd.Parameters.AddWithValue("@QuotaCharge", totalQuotaCharge);
                    cmd.Parameters.AddWithValue("@SurgeAmount", totalSurge);
                    cmd.Parameters.AddWithValue("@TotalFare", totalFare);

                    // ExecuteScalar must return inserted booking id — ensure your stored proc does that
                    bookingId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Determine seat prefix dynamically
                string seatPrefix = !string.IsNullOrWhiteSpace(cls.SeatPrefix)
                    ? cls.SeatPrefix.Trim()
                    : (cls.Code ?? "X").Replace(" ", "").Substring(0, Math.Min(2, (cls.Code ?? "X").Length));

                Random random = new Random();
                HashSet<int> usedSeats = new HashSet<int>();
                // Insert passengers
                for (int i = 0; i < passengerCount; i++)
                {
                    /*string seatNumber = $"{seatPrefix}{seatsAvailable}";*/


                    int seatNumberValue;
                    do
                    {
                        seatNumberValue = random.Next(1, seatsAvailable + 1); // 1 to total seats
                    }
                    while (!usedSeats.Add(seatNumberValue)); // ensure unique seat number

                    string seatNumber = $"{seatPrefix}{seatNumberValue}";

                    using (var cmd = new SqlCommand("InsertPassenger", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BookingId", bookingId);
                        cmd.Parameters.AddWithValue("@Name", passengerNames[i]);
                        cmd.Parameters.AddWithValue("@Age", passengerAges[i]);
                        cmd.Parameters.AddWithValue("@Gender", passengerGenders[i]);
                        cmd.Parameters.AddWithValue("@SeatNumber", seatNumber);
                        cmd.Parameters.AddWithValue("@Berth", passengerBerths[i]);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Build booking object with fare breakdown
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
                    Status = "CONFIRMED",
                    TrainNumber = int.TryParse(trainNumber, out var tn) ? tn : 0,
                    TrainName = trainName,
                    ClassCode = Class,
                    Frmst = fromStation.Name,
                    Tost = toStation.Name,
                    QuotaCharge = totalQuotaCharge,
                    GST = gst,
                    SurgeAmount = surge,
                    FinalFare = farePerPassenger,       // ✅ per passenger
                    TotalFare = totalFare,        // ✅ for all passengers
                };

                // Build email content
                string userEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name;
                string userName = User.Identity.Name ?? "Passenger";
                string pnrLocal = pnr;
                string subject = $"Booking Confirmed - PNR {pnrLocal}";
                string bookingUrl = Url.Action("DownloadTicket", "Booking", new { id = bookingId }, Request.Scheme);
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
                    <li><strong>GST:</strong> ₹{gst}</li>
                    <li><strong>Quota Charge:</strong> ₹{totalQuotaCharge}</li>
                    <li><strong>Surge:</strong> ₹{surge}</li>
                    <li><strong>Passengers:</strong> {passengerCount}</li>
                    <li><strong>Total Fare:</strong> ₹{totalFare}</li>
                </ul>
                <p>Your detailed ticket is attached below.</p>
                <p><a href='{bookingUrl}' download='E-Ticket_{pnr}.pdf' style='display:inline-block;padding:10px 15px;background:#007bff;color:white;text-decoration:none;border-radius:5px;'>📄 Download E-Ticket</a></p>
                <p>Thank you — IRCTC Clone</p>
            ";

                // fire-and-forget email send (method from your EmailService)
                _ = _emailService.SendEmail(userEmail, subject, body);

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
                                Frmst = reader["Frmst"].ToString(),
                                Tost = reader["Tost"].ToString(),
                                Passengers = new List<Passenger>()
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
                                    SeatNumber = reader.GetString(reader.GetOrdinal("SeatNumber"))
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
            List<Passenger> passengers = new List<Passenger>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetBookingDtls", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // First result: booking info
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BaseFare = reader.GetDecimal(reader.GetOrdinal("BaseFare")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                Quota = reader.GetString(reader.GetOrdinal("Quota")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
                                Frmst = reader.GetString(reader.GetOrdinal("Frmst")),
                                Tost = reader.GetString(reader.GetOrdinal("Tost"))
                            };
                        }
                        else
                            return NotFound();

                        // Move to second result set: passengers
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                passengers.Add(new Passenger
                                {
                                    Name = reader.GetString(reader.GetOrdinal("Name")),
                                    Age = reader.GetInt32(reader.GetOrdinal("Age")),
                                    Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                    SeatNumber = reader.GetString(reader.GetOrdinal("SeatNumber")),
                                    Berth = reader.GetString(reader.GetOrdinal("Berth"))
                                });
                            }
                        }
                    }
                }
            }

            booking.Passengers = passengers;
            return View(booking);
        }

        // GET: /Booking/MyBookings
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
        [Authorize]
        public IActionResult DownloadTicket(int id)
        {
            var booking = GetBookingDetails(id); // your method to fetch booking (must include Passengers, fares etc.)
            if (booking == null) return NotFound();

            using (var ms = new MemoryStream())
            {
                // Document setup
                var doc = new iTextSharp.text.Document(PageSize.A4, 36, 36, 36, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                writer.CloseStream = false;
                doc.Open();

                // Fonts
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                var sectionWhite = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.WHITE);
                var sectionBlue = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, new BaseColor(0, 102, 204));
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var normal = FontFactory.GetFont(FontFactory.HELVETICA, 9);
                var small = FontFactory.GetFont(FontFactory.HELVETICA, 8);

                // Colors
                var premiumBlue = new BaseColor(0, 102, 204);
                var lightPanel = new BaseColor(245, 246, 250);
                var frameColor = BaseColor.BLACK;

                var cb = writer.DirectContent;

                // --- Page border/frame ---
                Rectangle page = doc.PageSize;
                page = new Rectangle(doc.PageSize.Left + 15, doc.PageSize.Bottom + 15, doc.PageSize.Right - 15, doc.PageSize.Top - 15);
                page.Border = Rectangle.BOX;
                page.BorderWidth = 1.0f;
                page.BorderColor = frameColor;
                page.GetLeft(page.Left);
                doc.Add(new Chunk()); // ensure content started
                cb.Rectangle(page.Left, page.Bottom, page.Width, page.Height);
                cb.SetLineWidth(1.2f);
                cb.SetColorStroke(frameColor);
                cb.Stroke();

                // --- Light background panel behind ticket content ---
                cb.SetColorFill(lightPanel);
                float panelX = doc.Left + 8;
                float panelY = doc.Top - 300; // adjust height start
                float panelW = doc.PageSize.Width - doc.Left - doc.Right - 0;
                float panelH = 360f;
                cb.RoundRectangle(panelX, panelY, panelW, panelH, 6f);
                cb.Fill();

                // --- Watermark (faint, centered, rotated) ---
                cb.SaveState();
                cb.SetGState(new PdfGState { FillOpacity = 0.06f, StrokeOpacity = 0.06f });
                var wmFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 60);
                ColumnText.ShowTextAligned(cb, Element.ALIGN_CENTER, new Phrase("IRCTC CLONE", wmFont),
                    doc.PageSize.Width / 2, doc.PageSize.Height / 2, 45);
                cb.RestoreState();

                // --- Header: logo + title + PNR/Date
                var headerTbl = new PdfPTable(3) { WidthPercentage = 100f };
                headerTbl.SetWidths(new float[] { 1f, 3f, 1.7f });

                // Logo
                string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "irctc_logo.png");
                if (System.IO.File.Exists(logoPath))
                {
                    var logo = iTextSharp.text.Image.GetInstance(logoPath);
                    logo.ScaleToFit(55f, 55f);
                    headerTbl.AddCell(new PdfPCell(logo)
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        PaddingLeft = 2f
                    });
                }
                else
                {
                    headerTbl.AddCell(new PdfPCell(new Phrase("IRCTC", titleFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE
                    });
                }

                // Title (center)
                headerTbl.AddCell(new PdfPCell(new Phrase("E - TICKET", titleFont))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    VerticalAlignment = Element.ALIGN_MIDDLE,
                    PaddingTop = 12f
                });

                // Right PNR + Date (proper blue color)
                var right = new PdfPTable(1) { WidthPercentage = 100f };

                right.AddCell(new PdfPCell(new Phrase($"PNR: {booking.PNR}",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.BLUE)))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });

                right.AddCell(new PdfPCell(new Phrase($"Booked On: {booking.BookingDate:dd-MMM-yyyy}",
                    FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });

                headerTbl.AddCell(new PdfPCell(right)
                {
                    Border = Rectangle.NO_BORDER,
                    PaddingTop = 5f
                });

                doc.Add(headerTbl);
                doc.Add(new Paragraph("\n"));


                // --- Top info table: Train/Route/Status and QR/barcode
                var topTbl = new PdfPTable(2) { WidthPercentage = 100f, SpacingBefore = 6f };
                topTbl.SetWidths(new float[] { 2.7f, 1f });

                // Left: Train details
                var leftTbl = new PdfPTable(2) { WidthPercentage = 100f };
                leftTbl.DefaultCell.Border = Rectangle.NO_BORDER;
                leftTbl.AddCell(new PdfPCell(new Phrase("Train", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.TrainNumber} - {booking.TrainName}", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("From", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.Frmst} ({booking.Frmst?.Split(' ').FirstOrDefault()})", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("To", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.Tost} ({booking.Tost?.Split(' ').FirstOrDefault()})", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Journey Date", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase(booking.JourneyDate.ToString("dd-MMM-yyyy"), normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Class / Quota", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.ClassCode} / {booking.Quota}", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Status", labelFont)) { Border = Rectangle.NO_BORDER });
                var statusCell = new PdfPCell(new Phrase(booking.Status, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, booking.Status == "CONFIRMED" ? BaseColor.GREEN : BaseColor.RED))) { Border = Rectangle.NO_BORDER };
                leftTbl.AddCell(statusCell);

                topTbl.AddCell(leftTbl);

                // Right: QR + Barcode
                var rightTbl = new PdfPTable(1) { WidthPercentage = 100f };



                // QR code (PNR + Train + Date)
                // Build passenger details
                string passengerInfo = string.Join(";", booking.Passengers.Select(p =>
                    $"{p.Name} | {p.Age} | {p.Gender} | {p.SeatNumber}"
                ));
                // Build QR text
                string qrText =
                $"PNR: {booking.PNR} \nTrain: {booking.TrainNumber} - {booking.TrainName} \nFrom: {booking.Frmst} \nTo: {booking.Tost} \nDOJ: {booking.JourneyDate:yyyy-MM-dd} \nQuota: {booking.Quota} \nPassenger Details: \n\n{passengerInfo}"; 
                var qr = new BarcodeQRCode(qrText, 150, 150, null);
                var qrImage = qr.GetImage();
                qrImage.ScaleToFit(110f, 110f);
                var qrCell = new PdfPCell(qrImage) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, Padding = 2f };
                rightTbl.AddCell(qrCell);

                topTbl.AddCell(rightTbl);

                doc.Add(topTbl);
                doc.Add(new Paragraph("\n"));

                // --- Passenger table (premium style) ---
                var passTbl = new PdfPTable(6) { WidthPercentage = 100f, SpacingBefore = 6f };
                passTbl.SetWidths(new float[] { 3f, 1f, 1f, 1.4f, 1.4f, 1.8f });

                // Header row with blue background
                var hdrCell = new PdfPCell(new Phrase("Passenger Details", sectionWhite))
                {
                    BackgroundColor = premiumBlue,
                    Colspan = 6,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6f,
                    Border = Rectangle.NO_BORDER
                };
                passTbl.AddCell(hdrCell);

                // Column headers
                var cols = new[] { "Name", "Age", "Gender", "Coach", "Seat", "Berth" };
                foreach (var c in cols)
                {
                    passTbl.AddCell(new PdfPCell(new Phrase(c, labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY, HorizontalAlignment = Element.ALIGN_CENTER, Padding = 4f });
                }

                // Rows
                foreach (var p in booking.Passengers)
                {
                    string coach = "-";
                    string seatNo = "-";
                    if (!string.IsNullOrEmpty(p.SeatNumber))
                    {
                        coach = p.SeatNumber.Length > 0 ? p.SeatNumber.Substring(0, 1) : "-";
                        seatNo = p.SeatNumber.Length > 1 ? p.SeatNumber.Substring(1) : "-";
                    }

                    passTbl.AddCell(new PdfPCell(new Phrase(p.Name, normal)) { Padding = 4f });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Age.ToString(), normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Gender, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(coach, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(seatNo, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Berth ?? "-", normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                }

                doc.Add(passTbl);
                doc.Add(new Paragraph("\n"));

                // --- Payment Summary (Appears immediately after Passenger table) ---
                var payTbl = new PdfPTable(2) { WidthPercentage = 50f, HorizontalAlignment = Element.ALIGN_LEFT };
                payTbl.SetWidths(new float[] { 2f, 1f });

                // Header
                payTbl.AddCell(new PdfPCell(new Phrase("Payment Summary", sectionWhite))
                {
                    BackgroundColor = premiumBlue,
                    Colspan = 2,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6f,
                    Border = Rectangle.NO_BORDER
                });

                // Function to add rows
                void AddPay(string label, string val)
                {
                    payTbl.AddCell(new PdfPCell(new Phrase(label, labelFont)) { Border = Rectangle.NO_BORDER, Padding = 5f });
                    payTbl.AddCell(new PdfPCell(new Phrase(val, normal)) { Border = Rectangle.NO_BORDER, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                AddPay("Base Fare:", $"₹ {booking.BaseFare:F2}");
                AddPay("GST (5%):", $"₹ {booking.GST:F2}");
                AddPay("Quota Charge:", $"₹ {booking.QuotaCharge:F2}");
                AddPay("Surge:", $"₹ {booking.SurgeAmount:F2}");

                // Line + Total Fare
                payTbl.AddCell(new PdfPCell(new Phrase("")) { Border = Rectangle.TOP_BORDER, BorderWidthTop = 0.7f, Colspan = 2, Padding = 6f });
                AddPay("Total Paid:", $"₹ {booking.TotalFare:F2}");

                doc.Add(payTbl);

                // Push Signature near bottom of page
                var sigTable = new PdfPTable(1);
                sigTable.TotalWidth = 200f;

                string sigPathF = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "signature_placeholder.png");
                if (System.IO.File.Exists(sigPathF))
                {
                    var sImg = Image.GetInstance(sigPathF);
                    sImg.ScaleToFit(120f, 40f);
                    sigTable.AddCell(new PdfPCell(sImg)
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }
                else
                {
                    sigTable.AddCell(new PdfPCell(new Phrase("Authorized Signatory", labelFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                sigTable.AddCell(new PdfPCell(new Phrase("IRCTC Clone", small)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER });
                sigTable.AddCell(new PdfPCell(new Phrase($"PNR: {booking.PNR}", small)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER });

                // FIXED POSITION (BOTTOM RIGHT)
                sigTable.WriteSelectedRows(0, -1, doc.PageSize.Width - 220, 140, writer.DirectContent);


                // --- Terms & Conditions box ---
                // ---- TERMS & CONDITIONS (Fixed Position Above Footer) ----
                var termsText = new Paragraph();
                termsText.Add(new Chunk("Terms & Conditions\n",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)));
                termsText.Add(new Chunk("• Carry valid ID.\n", small));
                termsText.Add(new Chunk("• Boarding time subject to announcement.\n", small));
                termsText.Add(new Chunk("• Ticket is non-transferable.\n", small));
                termsText.Add(new Chunk("• Berth/coach may change by railway authority.\n", small));
                termsText.Add(new Chunk("• Refund rules as per IRCTC guidelines.\n", small));

                ColumnText ctTerms = new ColumnText(cb);

                // LEFT, BOTTOM-Y, RIGHT, TOP-Y
                ctTerms.SetSimpleColumn(
                    40,                                // X-left
                    doc.PageSize.GetBottom(60),        // bottom (80 px above bottom)
                    doc.PageSize.Width - 40,           // right
                    doc.PageSize.GetBottom(160)        // top of T&C block
                );
                ctTerms.AddElement(termsText);
                ctTerms.Go();
                cb.SetLineWidth(0.5f);
                cb.SetColorStroke(BaseColor.GRAY);

                // Draw line just above footer
                cb.MoveTo(40, doc.PageSize.GetBottom(60));
                cb.LineTo(doc.PageSize.Width - 40, doc.PageSize.GetBottom(60));
                cb.Stroke();

                // --- Footer small print centered
                cb.BeginText();

                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                cb.SetFontAndSize(footerFont.BaseFont, 8);

                float centerX = (doc.PageSize.Left + doc.PageSize.Right) / 2;
                float footerY = doc.PageSize.GetBottom(40);

                cb.ShowTextAligned(
                    Element.ALIGN_CENTER,
                    $"Generated on {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC | IRCTC Clone",
                    centerX,
                    footerY,
                    0
                );

                cb.EndText();

                // finalize
                doc.Close();
                writer.Flush();

                ms.Position = 0;
                var fileName = $"{booking.PNR}_{booking.TrainName}_{booking.TrainNumber}.pdf";
                return File(ms.ToArray(), "application/pdf", fileName);
            }
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
                        // --- 1️⃣ Read Booking Info ---
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                UserId = reader["UserId"]?.ToString(),
                                PNR = reader["PNR"]?.ToString(),
                                TrainName = reader["TrainName"]?.ToString(),
                                TrainNumber = reader["TrainNumber"] != DBNull.Value ? Convert.ToInt32(reader["TrainNumber"]) : 0,
                                Frmst = reader["Frmst"]?.ToString(),
                                Tost = reader["Tost"]?.ToString(),
                                JourneyDate = reader["JourneyDate"] != DBNull.Value ? Convert.ToDateTime(reader["JourneyDate"]) : DateTime.MinValue,
                                BookingDate = reader["BookingDate"] != DBNull.Value ? Convert.ToDateTime(reader["BookingDate"]) : DateTime.MinValue,
                                Status = reader["Status"]?.ToString(),
                                ClassCode = reader["ClassCode"]?.ToString(),
                                Quota = reader["Quota"]?.ToString(),
                                BaseFare = reader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(reader["BaseFare"]) : 0,
                                GST = reader["GST"] != DBNull.Value ? Convert.ToDecimal(reader["GST"]) : 0,
                                SurgeAmount = reader["SurgeAmount"] != DBNull.Value ? Convert.ToDecimal(reader["SurgeAmount"]) : 0,
                                TotalFare = reader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(reader["TotalFare"]) : 0,
                                QuotaCharge = reader["QuotaCharge"] != DBNull.Value ? Convert.ToDecimal(reader["QuotaCharge"]) : 0,
                                Passengers = new List<Passenger>()
                            };
                        }

                        // --- 2️⃣ Read Passenger Info ---
                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
                                    Name = reader["Name"]?.ToString(),
                                    Age = reader["Age"] != DBNull.Value ? Convert.ToInt32(reader["Age"]) : 0,
                                    Gender = reader["Gender"]?.ToString(),
                                    SeatNumber = reader["SeatNumber"]?.ToString(),
                                    Berth = reader["Berth"]?.ToString()
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
                                    SeatNumber = reader.GetString(reader.GetOrdinal("SeatNumber")),
                                    Berth = reader.GetString(reader.GetOrdinal("Berth"))
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

    }

}
