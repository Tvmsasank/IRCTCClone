using IRCTCClone.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
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

        [HttpGet]
        public IActionResult Checkout(int trainId, int classId, string journeyDate)
        {
            Train train = null;
            TrainClass cls = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Get Train
                using (var cmd = new SqlCommand(@"
            SELECT t.Id, t.Number, t.Name, t.Departure, t.Arrival, t.Duration,
                   fs.Id AS FromStationId, fs.Name AS FromStationName, fs.Code AS FromStationCode,
                   ts.Id AS ToStationId, ts.Name AS ToStationName, ts.Code AS ToStationCode
            FROM Trains t
            INNER JOIN Stations fs ON t.FromStationId = fs.Id
            INNER JOIN Stations ts ON t.ToStationId = ts.Id
            WHERE t.Id = @TrainId", conn))
                {
                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                Name = reader.GetString(reader.GetOrdinal("Name")),
                                Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                Duration = reader.GetTimeSpan(reader.GetOrdinal("Duration")),
                                FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                FromStation = new Station
                                {
                                  //  Id = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("FromStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("FromStationCode"))
                                },
                                ToStation = new Station
                                {
                                   // Id = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("ToStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("ToStationCode"))
                                }

                            };
                        }
                    }
                }

                if (train == null)
                    return NotFound();

                // Get Class
                using (var cmdClass = new SqlCommand(
                    "SELECT Id, TrainId, Code, Fare, SeatsAvailable FROM TrainClasses WHERE Id = @ClassId", conn))
                {
                    cmdClass.Parameters.AddWithValue("@ClassId", classId);
                    using (var reader = cmdClass.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            cls = new TrainClass
                            {
                                Id = reader.GetInt32(0),
                                TrainId = reader.GetInt32(1),
                                Code = reader.GetString(2),
                                Fare = reader.GetDecimal(3),
                                SeatsAvailable = reader.GetInt32(4)
                            };
                        }
                    }
                }
            }

            if (cls == null)
                return NotFound();

            ViewBag.Train = train;
            ViewBag.Class = cls;
            ViewBag.JourneyDate = journeyDate;

            return View("Checkout");
        }

        /* [HttpGet]
         public IActionResult Checkout(int trainId, int classId, string journeyDate)
         {
             Train train = null;
             TrainClass cls = null;

             using (var conn = new SqlConnection(_connectionString))
             {
                 conn.Open();

                 // Get Train
                 using (var cmd = new SqlCommand(
                 @"SELECT t.Id, t.Number, t.Name, t.Departure, t.Arrival, t.Duration,
                 fs.Id AS FromStationId, fs.Name AS FromStationName, fs.Code AS FromStationCode,
                 ts.Id AS ToStationId, ts.Name AS ToStationName, ts.Code AS ToStationCode
                 FROM Trains t
                 INNER JOIN Stations fs ON t.FromStationId = fs.Id
                 INNER JOIN Stations ts ON t.ToStationId = ts.Id
                 WHERE t.Id = @TrainId", conn))
                 {
                     cmd.Parameters.AddWithValue("@TrainId", trainId);

                     using (var reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {
                             train = new Train
                             {
                                 Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                 Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                 Name = reader.GetString(reader.GetOrdinal("Name")),
                                 Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                 Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                 Duration = reader.GetTimeSpan(reader.GetOrdinal("Duration")),
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
                     }
                 }


                 if (train == null) return NotFound();

                 // Get Class
                 using (var cmd = new SqlCommand(
                     "SELECT Id, TrainId, Code, Fare, SeatsAvailable FROM TrainClasses WHERE Id = @ClassId", conn))
                 {
                     cmd.Parameters.AddWithValue("@ClassId", classId);
                     using (var reader = cmd.ExecuteReader())
                     {
                         if (reader.Read())
                         {
                             cls = new TrainClass
                             {
                                 Id = reader.GetInt32(0),
                                 TrainId = reader.GetInt32(1),
                                 Code = reader.GetString(2),
                                 Fare = reader.GetDecimal(3),
                                 SeatsAvailable = reader.GetInt32(4)
                             };
                         }
                     }
                 }
             }

             if (cls == null) return NotFound();

             ViewBag.Train = train;
             ViewBag.Class = cls;
             ViewBag.JourneyDate = journeyDate;


             return View("Checkout");
         }
 */
        // POST: /Booking/Confirm
        [HttpPost]
        public IActionResult Confirm(int trainId, int classId, string journeyDate, List<string> passengerNames, List<int> passengerAges, List<string> passengerGenders, string trainName, string trainNumber, string Class,int FromStationId,int ToStationId)
        {
            if (passengerNames == null || passengerNames.Count == 0)
            {
                TempData["Error"] = "Please add at least one passenger.";
                return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
            }

            Station fromStation = null;
            Station toStation = null;

            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();

                // ✅ Get FROM Station 
                string fromQuery = "SELECT * FROM Stations WHERE Id = @fromId";
                using (SqlCommand fromCmd = new SqlCommand(fromQuery, con))
                {
                    fromCmd.Parameters.AddWithValue("@fromId", FromStationId);
                    using (SqlDataReader reader = fromCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            fromStation = new Station
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Code = reader["Code"].ToString(),
                                Name = reader["Name"].ToString()
                            };
                        }
                    }
                }

                // ✅ Get TO Station
                string toQuery = "SELECT * FROM Stations WHERE Id = @toId";
                using (SqlCommand toCmd = new SqlCommand(toQuery, con))
                {
                    toCmd.Parameters.AddWithValue("@toId", ToStationId);
                    using (SqlDataReader reader = toCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            toStation = new Station
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Code = reader["Code"].ToString(),
                                Name = reader["Name"].ToString()
                            };
                        }
                    }
                }
            }

            // ✅ Store values for the next step
            ViewBag.FromStation = fromStation;
            ViewBag.ToStation = toStation;
            ViewBag.JourneyDate = journeyDate;



            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new Exception("Not logged in");

            int seatsAvailable = 0;
            decimal fare = 0;
            TrainClass cls = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Get TrainClass
                using (var cmd = new SqlCommand("SELECT Id, Code, Fare, SeatsAvailable FROM TrainClasses WHERE Id = @ClassId", conn))
                {
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            cls = new TrainClass
                            {
                                Id = reader.GetInt32(0),
                                Code = reader.GetString(1),
                                Fare = reader.GetDecimal(2),
                                SeatsAvailable = reader.GetInt32(3)
                            };
                            seatsAvailable = cls.SeatsAvailable;
                            fare = cls.Fare;
                        }
                        else return NotFound();
                    }
                }

                int passengerCount = passengerNames.Count;
                if (seatsAvailable < passengerCount)
                {
                    TempData["Error"] = "Not enough seats available.";
                    return RedirectToAction("Checkout", new { trainId, classId, journeyDate });
                }

                // Update seats
                using (var cmd = new SqlCommand("UPDATE TrainClasses SET SeatsAvailable = SeatsAvailable - @Count WHERE Id = @ClassId", conn))
                {
                    cmd.Parameters.AddWithValue("@Count", passengerCount);
                    cmd.Parameters.AddWithValue("@ClassId", cls.Id);
                    cmd.ExecuteNonQuery();
                }

                // Insert Booking
                int bookingId;
                string pnr = GeneratePnr();
                using (var cmd = new SqlCommand(
                    @"INSERT INTO Bookings (PNR, UserId, TrainId, TrainClassId, JourneyDate, BookingDate, Amount, Status, TrainNumber, TrainName, Class, Frmst, Tost)
                      VALUES (@PNR, @UserId, @TrainId, @TrainClassId, @JourneyDate, @BookingDate, @Amount, @Status, @TrainNumber, @TrainName, @Class, @Frmst, @Tost);
                      SELECT SCOPE_IDENTITY();", conn))
                {
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
                string seatPrefix = cls.Code switch
                {
                    "SL" => "S",
                    "2A" => "H",
                    "3A" => "A",
                    "1A" => "B",
                    "3E" => "M",
                    "AC CC" => "C",
                    "2S" => "D"
                };

                // Insert passengers
                for (int i = 0; i < passengerCount; i++)
                {
                    var seatNumber = $"{seatPrefix}{seatsAvailable + i + 1}";
                    using (var cmd = new SqlCommand(
                        @"INSERT INTO Passengers (BookingId, Name, Age, Gender, SeatNumber)
                          VALUES (@BookingId, @Name, @Age, @Gender, @SeatNumber)", conn))
                    {
                        cmd.Parameters.AddWithValue("@BookingId", bookingId);
                        cmd.Parameters.AddWithValue("@Name", passengerNames[i]);
                        cmd.Parameters.AddWithValue("@Age", passengerAges[i]);
                        cmd.Parameters.AddWithValue("@Gender", passengerGenders[i]);
                        cmd.Parameters.AddWithValue("@SeatNumber", seatNumber);
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

                // Get Booking
                using (var cmd = new SqlCommand("SELECT * FROM Bookings WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
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
                                ClassCode = reader.GetString(reader.GetOrdinal("Class")),
                                Passengers = new List<Passenger>()
                            };
                        }
                        else return NotFound();
                    }
                }

                // Get Passengers
                using (var cmd = new SqlCommand("SELECT * FROM Passengers WHERE BookingId = @BookingId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", booking.Id);
                    using (var reader = cmd.ExecuteReader())
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

            return View(booking);
        }

        [HttpGet]
        public IActionResult History()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            // Fetch bookings for this user
            var bookings = new List<Booking>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    @"SELECT b.Id, b.PNR, b.JourneyDate, b.Amount, b.Status,
                     t.Number AS TrainNumber, t.Name AS TrainName, tc.Code AS ClassCode
              FROM Bookings b
              JOIN Trains t ON b.TrainId = t.Id
              JOIN TrainClasses tc ON b.TrainClassId = tc.Id
              WHERE b.UserId = @UserId
              ORDER BY b.BookingDate DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            bookings.Add(new Booking
                            {
                                BookingId = reader.GetInt32(0),
                                PNR = reader.GetString(1),
                                JourneyDate = reader.GetDateTime(2),
                                Amount = reader.GetDecimal(3),
                                Status = reader.GetString(4),
                                TrainNumber = reader.GetInt32(5),
                                TrainName = reader.GetString(6),
                                ClassCode = reader.GetString(7)
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

                // Get booking info
                using (var cmd = new SqlCommand(
                    @"SELECT b.Id, b.PNR, b.JourneyDate, b.Amount, b.Status,
                     t.Number AS TrainNumber, t.Name AS TrainName, tc.Code AS ClassCode,b.Frmst, b.Tost
              FROM Bookings b
              JOIN Trains t ON b.TrainId = t.Id
              JOIN TrainClasses tc ON b.TrainClassId = tc.Id
              WHERE b.Id = @BookingId AND b.UserId = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                BookingId = reader.GetInt32(0),
                                PNR = reader.GetString(1),
                                JourneyDate = reader.GetDateTime(2),
                                Amount = reader.GetDecimal(3),
                                Status = reader.GetString(4),
                                TrainNumber = reader.GetInt32(5),
                                TrainName = reader.GetString(6),
                                ClassCode = reader.GetString(7),
                                Frmst = reader.GetString(8),
                                Tost = reader.GetString(9)
                            };
                        }
                    }
                }

                if (booking == null)
                    return NotFound();

                // Get passengers for this booking
                using (var cmd = new SqlCommand(
                    @"SELECT Name, Age, Gender, SeatNumber
              FROM Passengers
              WHERE BookingId = @BookingId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            passengers.Add(new Passenger
                            {
                                Name = reader.GetString(0),
                                Age = reader.GetInt32(1),
                                Gender = reader.GetString(2),
                                SeatNumber = reader.GetString(3)
                            });
                        }
                    }
                }
            }

            var model = new Booking
            {
                Id = booking.Id,  // use Id instead of Booking object
                PNR = booking.PNR,
                JourneyDate = booking.JourneyDate,
                Amount = booking.Amount,
                Status = booking.Status,
                TrainNumber = booking.TrainNumber,
                TrainName = booking.TrainName,
                ClassCode = booking.ClassCode,
                Passengers = passengers,
                Frmst = booking.Frmst,
                Tost = booking.Tost
            };


            return View(model);
        }


        // GET: /Booking/MyBookings
        [HttpGet]
        public IActionResult MyBookings()
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var bookings = new List<Booking>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Get all confirmed bookings for this user
                using (var cmd = new SqlCommand(
                    @"SELECT b.Id, b.PNR, b.JourneyDate, b.Amount, b.Status,
                             t.Id AS TrainId, t.Number, t.Name,
                             c.Id AS ClassId, c.Code
                      FROM Bookings b
                      INNER JOIN Trains t ON b.TrainId = t.Id
                      INNER JOIN TrainClasses c ON b.TrainClassId = c.Id
                      WHERE b.UserId = @UserId AND b.Status = 'CONFIRMED'
                      ORDER BY b.BookingDate DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            bookings.Add(new Booking
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                PNR = reader.GetString(reader.GetOrdinal("PNR")),
                                JourneyDate = reader.GetDateTime(reader.GetOrdinal("JourneyDate")),
                                Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                Train = new Train
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                    Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                    Name = reader.GetString(reader.GetOrdinal("Name"))
                                },
                                TrainClass = new TrainClass
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                    Code = reader.GetString(reader.GetOrdinal("Code"))
                                },
                                Passengers = new List<Passenger>()
                            });
                        }
                    }
                }

                // Get passengers for each booking
                foreach (var booking in bookings)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT Name, Age, Gender, SeatNumber FROM Passengers WHERE BookingId = @BookingId", conn))
                    {
                        cmd.Parameters.AddWithValue("@BookingId", booking.Id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
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

            return View(bookings);
        }

        [HttpPost]
        public IActionResult Cancel(int id)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Step 1: Get booking details
                int trainClassId = 0;
                int passengerCount = 0;
                using (var cmd = new SqlCommand(
                    @"SELECT TrainClassId FROM Bookings WHERE Id = @BookingId AND UserId = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    var result = cmd.ExecuteScalar();
                    if (result == null)
                        return NotFound(); // Booking not found or not owned by this user
                    trainClassId = Convert.ToInt32(result);
                }

                // Step 2: Count how many passengers were booked
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Passengers WHERE BookingId = @BookingId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    passengerCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Step 3: Increase seat availability back
                using (var cmd = new SqlCommand(
                    "UPDATE TrainClasses SET SeatsAvailable = SeatsAvailable + @Count WHERE Id = @ClassId", conn))
                {
                    cmd.Parameters.AddWithValue("@Count", passengerCount);
                    cmd.Parameters.AddWithValue("@ClassId", trainClassId);
                    cmd.ExecuteNonQuery();
                }

                // Step 4: Delete passengers for this booking
                using (var cmd = new SqlCommand("DELETE FROM Passengers WHERE BookingId = @BookingId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.ExecuteNonQuery();
                }

                // Step 5: Delete booking
                using (var cmd = new SqlCommand("DELETE FROM Bookings WHERE Id = @BookingId", conn))
                {
                    cmd.Parameters.AddWithValue("@BookingId", id);
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            TempData["Message"] = "Booking cancelled successfully!";
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
