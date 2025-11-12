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


namespace IRCTCClone.Controllers
{
    [EnableRateLimiting("DefaultPolicy")]

    [Authorize]
    public class BookingController : Controller
    {
        private readonly string _connectionString;
/*        private readonly string _baseUrl;*/
        private readonly EmailService _emailService;

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
                                Duration = reader.GetTimeSpan(reader.GetOrdinal("Duration")),

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
                                Fare = reader.GetDecimal(reader.GetOrdinal("Fare")),
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

                // Get TrainClass
                TrainClass cls = null;
                int seatsAvailable = 0;
                decimal fare = 0;
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
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                Code = reader["Code"]?.ToString(),
                                Fare = reader["Fare"] != DBNull.Value ? Convert.ToDecimal(reader["Fare"]) : 0,
                                SeatsAvailable = reader["SeatsAvailable"] != DBNull.Value ? Convert.ToInt32(reader["SeatsAvailable"]) : 0,
                                SeatPrefix = reader["SeatPrefix"]?.ToString()
                            };
                            seatsAvailable = cls.SeatsAvailable;
                            fare = cls.Fare;
                        }
                    }
                }

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
                using (var cmd = new SqlCommand("InsertBooking", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PNR", pnr);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainClassId", cls.Id);
                    cmd.Parameters.AddWithValue("@JourneyDate", DateTime.Parse(journeyDate));
                    cmd.Parameters.AddWithValue("@BookingDate", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@Amount", fare * passengerCount);
                    cmd.Parameters.AddWithValue("@Status", "CONFIRMED");
                    cmd.Parameters.AddWithValue("@TrainNumber", trainNumber);
                    cmd.Parameters.AddWithValue("@TrainName", trainName);
                    cmd.Parameters.AddWithValue("@Class", Class);
                    cmd.Parameters.AddWithValue("@Frmst", fromStation.Name);
                    cmd.Parameters.AddWithValue("@Tost", toStation.Name);

                    bookingId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Determine seat prefix
                /*   string seatPrefix = cls.Code switch
                   {
                       "SL" => "S",
                       "2A" => "H",
                       "3A" => "A",
                       "1A" => "B",
                       "3E" => "M",
                       "AC CC" => "C",
                       "2S" => "D",
                       "EC CC" => "EC",
                       _ => "X"
                   };
   */

                // Determine seat prefix dynamically from database
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

                Booking booking = new Booking
                {
                    BookingId = bookingId,
                    PNR = pnr,
                    UserId = userId,
                    TrainId = trainId,
                    TrainClassId = cls.Id,
                    JourneyDate = DateTime.Parse(journeyDate),
                    BookingDate = DateTime.UtcNow,
                    Amount = fare * passengerCount,
                    Status = "CONFIRMED",
                    TrainNumber = int.Parse(trainNumber),
                    TrainName = trainName,
                    ClassCode = Class,
                    Frmst = fromStation.Name,
                    Tost = toStation.Name,
/*                    username = "IRCTCClone@gmail.com",
                    password = "Irctc@123"*/
                };
                // --- Build email content ---
                string userEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name; // adjust if you store email elsewhere
                string userName = User.Identity.Name ?? "Passenger";
                string pnrLocal = pnr; // your generated PNR
                string subject = $"Booking Confirmed - PNR {pnrLocal}";
                string bookingUrl = Url.Action("DownloadTicket", "Booking", new { id = bookingId }, Request.Scheme);
                string body = $@"
                        <h3>Booking Confirmed ✅</h3>
                        <p>Hi {userName},</p>
                        <p>Your booking is confirmed. Details below:</p>
                        <ul>
                            <li><strong>PNR:</strong> {pnrLocal}</li>
                            <li><strong>Train:</strong> {trainName} ({trainNumber})</li>
                            <li><strong>From:</strong> {fromStation?.Name}</li>
                            <li><strong>To:</strong> {toStation?.Name}</li>
                            <li><strong>Date:</strong> {DateTime.Parse(journeyDate):dd-MMM-yyyy}</li>
                            <li><strong>Amount:</strong> ₹{fare * passengerCount}</li>
                        </ul>
                            <p>Your detailed ticket is attached below.</p>
                            <p><a href='{bookingUrl}' download='E-Ticket_{pnr}.pdf'
                               style='display:inline-block;padding:10px 15px;background:#007bff;color:white;text-decoration:none;border-radius:5px;'>
                               📄 Download E-Ticket
                            </a></p>
                            <p>Thank you — IRCTC Clone</p>
                        ";

                // fire-and-forget (not awaiting) — safe to await too if you prefer
                _ = _emailService.SendEmail(userEmail, subject, body);
                /*  _ = _emailService.SendEmail(booking);*/


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
                            booking = new Booking
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                UserId = reader.GetString(reader.GetOrdinal("UserId")),
                                TrainId = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                TrainNumber = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                TrainName = reader.GetString(reader.GetOrdinal("TrainName")),
                                TrainClassId = reader.GetInt32(reader.GetOrdinal("TrainClassId")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                BookingDate = reader.GetDateTime(reader.GetOrdinal("BookingDate")),
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                ClassCode = reader.GetString(reader.GetOrdinal("ClassCode")),
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
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
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
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
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
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
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
            var booking = GetBookingDetails(id);
            if (booking == null)
                return NotFound();

            using (MemoryStream stream = new MemoryStream())
            {
                var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 36, 36, 36, 36);
                PdfWriter.GetInstance(doc, stream).CloseStream = false;
                doc.Open();

                // 🎨 Fonts
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 20, BaseColor.BLUE);
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 13, BaseColor.WHITE);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
                var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 11);

                // 🟦 Header
                var headerTable = new PdfPTable(2);
                headerTable.WidthPercentage = 100;
                headerTable.SetWidths(new float[] { 1f, 3f });

                // IRCTC Logo (optional: replace with your app logo)
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "irctc_logo.png");
                if (System.IO.File.Exists(logoPath))
                {
                    var logo = iTextSharp.text.Image.GetInstance(logoPath);
                    logo.ScaleAbsolute(60, 60);
                    headerTable.AddCell(new PdfPCell(logo) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT });
                }
                else
                {
                    headerTable.AddCell(new PdfPCell(new Phrase("IRCTC", titleFont)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT });
                }

                headerTable.AddCell(new PdfPCell(new Phrase("Indian Railway Catering and Tourism Corporation\nE-Ticket", titleFont))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    VerticalAlignment = Element.ALIGN_MIDDLE
                });

                doc.Add(headerTable);
                doc.Add(new Paragraph("\n"));

                // 🔹 PNR Section
                var pnrCell = new PdfPCell(new Phrase($"PNR: {booking.PNR}", sectionFont))
                {
                    BackgroundColor = new BaseColor(0, 102, 204),
                    Padding = 8,
                    Border = Rectangle.NO_BORDER,
                    Colspan = 2,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                var pnrTable = new PdfPTable(1) { WidthPercentage = 100 };
                pnrTable.AddCell(pnrCell);
                doc.Add(pnrTable);

                doc.Add(new Paragraph("\n"));

                // 🚆 Train Details Section
                var trainTable = new PdfPTable(2);
                trainTable.WidthPercentage = 100;
                trainTable.SetWidths(new float[] { 1f, 2f });

                trainTable.AddCell(new PdfPCell(new Phrase("Train Number:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.TrainNumber.ToString(), normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("Train Name:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.TrainName, normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("From:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.Frmst, normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("To:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.Tost, normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("Journey Date:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.JourneyDate.ToString("dd-MMM-yyyy"), normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("Class:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.ClassCode, normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("Status:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase(booking.Status, normalFont)));

                trainTable.AddCell(new PdfPCell(new Phrase("Fare Paid:", labelFont)));
                trainTable.AddCell(new PdfPCell(new Phrase($"₹{booking.Amount}", normalFont)));

                doc.Add(trainTable);
                doc.Add(new Paragraph("\n"));

                // 👥 Passenger Details Section
                var passengerHeader = new PdfPCell(new Phrase("Passenger Details", sectionFont))
                {
                    BackgroundColor = new BaseColor(0, 102, 204),
                    Padding = 8,
                    Border = Rectangle.NO_BORDER,
                    Colspan = 6,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };

                var passengerTable = new PdfPTable(6);
                passengerTable.WidthPercentage = 100;
                passengerTable.SetWidths(new float[] { 2f, 1f, 1f, 2f, 2f, 2f});

                passengerTable.AddCell(passengerHeader);

                passengerTable.AddCell(new PdfPCell(new Phrase("Passenger Name", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                passengerTable.AddCell(new PdfPCell(new Phrase("Age", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                passengerTable.AddCell(new PdfPCell(new Phrase("Gender", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                passengerTable.AddCell(new PdfPCell(new Phrase("Coach", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                passengerTable.AddCell(new PdfPCell(new Phrase("Seat", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                passengerTable.AddCell(new PdfPCell(new Phrase("Berth", labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY });

                foreach (var p in booking.Passengers)
                {
                    string coach = "-";
                    string seatNumber = "-";

                    if (!string.IsNullOrEmpty(p.SeatNumber))
                    {
                        // Example: "H23" → Coach = "H", Seat = "23"
                        coach = p.SeatNumber.Substring(0, 1); // first letter
                        seatNumber = p.SeatNumber.Substring(1);  // rest of the string
                    }

                    passengerTable.AddCell(new Phrase(p.Name, normalFont));
                    passengerTable.AddCell(new Phrase(p.Age.ToString(), normalFont));
                    passengerTable.AddCell(new Phrase(p.Gender, normalFont));
                    passengerTable.AddCell(new Phrase(coach, normalFont));
                    passengerTable.AddCell(new Phrase(seatNumber, normalFont));
                    passengerTable.AddCell(new Phrase(p.Berth ?? "-", normalFont));
                }

                doc.Add(passengerTable);
                doc.Add(new Paragraph("\n\n"));

                // 🧾 Footer
                var footer = new Paragraph("Thank you for booking with IRCTC Clone!\nHave a safe and happy journey.", normalFont);
                footer.Alignment = Element.ALIGN_CENTER;
                doc.Add(footer);

                doc.Close();
                stream.Position = 0;

                return File(stream.ToArray(), "application/pdf", $"{booking.PNR}_{booking.TrainName}_{booking.TrainNumber}.pdf");
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
                                PNR = reader["PNR"]?.ToString(),
                                TrainName = reader["TrainName"]?.ToString(),
                                TrainNumber = reader["TrainNumber"] != DBNull.Value ? Convert.ToInt32(reader["TrainNumber"]) : 0,
                                Frmst = reader["Frmst"]?.ToString(),
                                Tost = reader["Tost"]?.ToString(),
                                JourneyDate = reader["JourneyDate"] != DBNull.Value ? Convert.ToDateTime(reader["JourneyDate"]) : DateTime.MinValue,
                                Status = reader["Status"]?.ToString(),
                                ClassCode = reader["ClassCode"]?.ToString(),
                                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0,
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
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
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
