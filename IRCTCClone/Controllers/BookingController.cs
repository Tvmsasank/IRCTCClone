using IRCTCClone.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;

namespace IRCTCClone.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly string _connectionString;

        public BookingController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
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

        private string GeneratePnr()
        {
            var rand = new Random();
            var chars = "0123456789";
            return new string(Enumerable.Range(0, 10).Select(i => chars[rand.Next(chars.Length)]).ToArray());
        }
    }
}
