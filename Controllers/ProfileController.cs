using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using IRCTCClone.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IRCTCClone.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly string _connectionString;

        public ProfileController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            EnsureTablesExist();
        }

        private void EnsureTablesExist()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string createTablesSql = @"
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UserProfiles' AND xtype='U')
                        BEGIN
                            CREATE TABLE UserProfiles (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId NVARCHAR(255) NOT NULL UNIQUE,
                                Username NVARCHAR(255) NULL,
                                FullName NVARCHAR(255) NULL,
                                Email NVARCHAR(255) NULL,
                                MobileNumber NVARCHAR(50) NULL,
                                DateOfBirth DATETIME NULL,
                                Gender NVARCHAR(20) NULL,
                                Country NVARCHAR(100) DEFAULT 'India',
                                Address NVARCHAR(1000) NULL,
                                IsAadhaarVerified BIT DEFAULT 1,
                                AadhaarNumber NVARCHAR(50) NULL,
                                WalletBalance DECIMAL(18,2) DEFAULT 0.00,
                                LastPasswordUpdate DATETIME NULL
                            );
                        END

                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='MasterPassengers' AND xtype='U')
                        BEGIN
                            CREATE TABLE MasterPassengers (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId NVARCHAR(255) NOT NULL,
                                Concession NVARCHAR(100) DEFAULT 'Normal User',
                                FullName NVARCHAR(255) NOT NULL,
                                DateOfBirth DATETIME NULL,
                                Age INT NULL,
                                Nationality NVARCHAR(100) DEFAULT 'Indian',
                                Gender NVARCHAR(20) NOT NULL,
                                BerthPreference NVARCHAR(50) DEFAULT 'No Preference',
                                FoodChoice NVARCHAR(50) DEFAULT 'Veg',
                                IdCardType NVARCHAR(50) DEFAULT 'AADHAAR',
                                IdCardNumber NVARCHAR(100) NULL,
                                VerificationStatus NVARCHAR(50) DEFAULT 'Verified',
                                CreatedAt DATETIME DEFAULT GETUTCDATE()
                            );
                        END

                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FavoriteJourneys' AND xtype='U')
                        BEGIN
                            CREATE TABLE FavoriteJourneys (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId NVARCHAR(255) NOT NULL,
                                TrainNumber NVARCHAR(50) NOT NULL,
                                TrainName NVARCHAR(255) NULL,
                                ClassCode NVARCHAR(20) DEFAULT '3A',
                                ClassName NVARCHAR(100) DEFAULT 'AC 3 Tier (3A)',
                                FromStation NVARCHAR(255) NOT NULL,
                                FromStationCode NVARCHAR(50) NULL,
                                ToStation NVARCHAR(255) NOT NULL,
                                ToStationCode NVARCHAR(50) NULL,
                                Quota NVARCHAR(50) DEFAULT 'GENERAL',
                                CreatedAt DATETIME DEFAULT GETUTCDATE()
                            );
                        END
                    ";

                    using (var cmd = new SqlCommand(createTablesSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Table initialization notice: " + ex.Message);
            }
        }

        [HttpGet]
        public IActionResult Index(string tab = "profile")
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var viewModel = new ProfileViewModel
            {
                ActiveTab = string.IsNullOrEmpty(tab) ? "profile" : tab.ToLower()
            };

            string currentFullName = User.Identity?.Name ?? "User";

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1. Fetch or initialize UserProfile
                using (var cmd = new SqlCommand("SELECT * FROM UserProfiles WHERE UserId = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            viewModel.Profile = new UserProfile
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                UserId = reader["UserId"].ToString(),
                                Username = reader["Username"] != DBNull.Value ? reader["Username"].ToString() : (userId.Contains("@") ? userId.Split('@')[0] : userId),
                                FullName = reader["FullName"] != DBNull.Value && !string.IsNullOrWhiteSpace(reader["FullName"].ToString()) ? reader["FullName"].ToString() : currentFullName,
                                Email = reader["Email"] != DBNull.Value ? reader["Email"].ToString() : userId,
                                MobileNumber = reader["MobileNumber"] != DBNull.Value ? reader["MobileNumber"].ToString() : "+91 9000485456",
                                DateOfBirth = reader["DateOfBirth"] != DBNull.Value ? Convert.ToDateTime(reader["DateOfBirth"]) : new DateTime(2002, 2, 13),
                                Gender = reader["Gender"] != DBNull.Value ? reader["Gender"].ToString() : "Male",
                                Country = reader["Country"] != DBNull.Value ? reader["Country"].ToString() : "India",
                                Address = reader["Address"] != DBNull.Value ? reader["Address"].ToString() : "House no 3-2-63/2/276P, street no 17, bhavani nagar, Mallapur, nacharam, Hyderabad 500076, TELANGANA, 500076",
                                IsAadhaarVerified = reader["IsAadhaarVerified"] != DBNull.Value ? Convert.ToBoolean(reader["IsAadhaarVerified"]) : true,
                                AadhaarNumber = reader["AadhaarNumber"] != DBNull.Value ? reader["AadhaarNumber"].ToString() : "XXXXXXXX1234",
                                WalletBalance = reader["WalletBalance"] != DBNull.Value ? Convert.ToDecimal(reader["WalletBalance"]) : 0.00m,
                                LastPasswordUpdate = reader["LastPasswordUpdate"] != DBNull.Value ? Convert.ToDateTime(reader["LastPasswordUpdate"]) : DateTime.Now.AddDays(-140)
                            };
                        }
                        else
                        {
                            // Default mock/initial profile if not created yet
                            viewModel.Profile = new UserProfile
                            {
                                UserId = userId,
                                Username = userId.Contains("@") ? userId.Split('@')[0] : userId,
                                FullName = currentFullName,
                                Email = userId,
                                MobileNumber = "+91 9000485456",
                                DateOfBirth = new DateTime(2002, 2, 13),
                                Gender = "Male",
                                Country = "India",
                                Address = "House no 3-2-63/2/276P, street no 17, bhavani nagar, Mallapur, nacharam, Hyderabad 500076, TELANGANA, 500076",
                                IsAadhaarVerified = true,
                                AadhaarNumber = "XXXXXXXX1234",
                                WalletBalance = 0.00m,
                                LastPasswordUpdate = DateTime.Now.AddDays(-140)
                            };
                        }
                    }
                }

                // 2. Fetch Master Passengers
                using (var cmd = new SqlCommand("SELECT * FROM MasterPassengers WHERE UserId = @UserId ORDER BY Id DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            viewModel.MasterPassengers.Add(new MasterPassenger
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                UserId = reader["UserId"].ToString(),
                                Concession = reader["Concession"].ToString(),
                                FullName = reader["FullName"].ToString(),
                                DateOfBirth = reader["DateOfBirth"] != DBNull.Value ? Convert.ToDateTime(reader["DateOfBirth"]) : (DateTime?)null,
                                Age = reader["Age"] != DBNull.Value ? Convert.ToInt32(reader["Age"]) : (int?)null,
                                Nationality = reader["Nationality"].ToString(),
                                Gender = reader["Gender"].ToString(),
                                BerthPreference = reader["BerthPreference"].ToString(),
                                FoodChoice = reader["FoodChoice"].ToString(),
                                IdCardType = reader["IdCardType"].ToString(),
                                IdCardNumber = reader["IdCardNumber"] != DBNull.Value ? reader["IdCardNumber"].ToString() : null,
                                VerificationStatus = reader["VerificationStatus"].ToString(),
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                            });
                        }
                    }
                }

                // 3. Fetch Favorite Journeys
                using (var cmd = new SqlCommand("SELECT * FROM FavoriteJourneys WHERE UserId = @UserId ORDER BY Id DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            viewModel.FavoriteJourneys.Add(new FavoriteJourney
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                UserId = reader["UserId"].ToString(),
                                TrainNumber = reader["TrainNumber"].ToString(),
                                TrainName = reader["TrainName"] != DBNull.Value ? reader["TrainName"].ToString() : null,
                                ClassCode = reader["ClassCode"].ToString(),
                                ClassName = reader["ClassName"].ToString(),
                                FromStation = reader["FromStation"].ToString(),
                                FromStationCode = reader["FromStationCode"] != DBNull.Value ? reader["FromStationCode"].ToString() : null,
                                ToStation = reader["ToStation"].ToString(),
                                ToStationCode = reader["ToStationCode"] != DBNull.Value ? reader["ToStationCode"].ToString() : null,
                                Quota = reader["Quota"].ToString(),
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                            });
                        }
                    }
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        public IActionResult UpdateProfile([FromBody] UserProfile model)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"
                        IF EXISTS (SELECT 1 FROM UserProfiles WHERE UserId = @UserId)
                        BEGIN
                            UPDATE UserProfiles
                            SET FullName = @FullName,
                                MobileNumber = @MobileNumber,
                                DateOfBirth = @DateOfBirth,
                                Gender = @Gender,
                                Country = @Country,
                                Address = @Address
                            WHERE UserId = @UserId;
                        END
                        ELSE
                        BEGIN
                            INSERT INTO UserProfiles (UserId, Username, FullName, Email, MobileNumber, DateOfBirth, Gender, Country, Address, IsAadhaarVerified, WalletBalance)
                            VALUES (@UserId, @Username, @FullName, @Email, @MobileNumber, @DateOfBirth, @Gender, @Country, @Address, 1, 0.00);
                        END
                    ";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Username", (object)model.Username ?? (userId.Contains("@") ? userId.Split('@')[0] : userId));
                        cmd.Parameters.AddWithValue("@FullName", (object)model.FullName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Email", (object)model.Email ?? userId);
                        cmd.Parameters.AddWithValue("@MobileNumber", (object)model.MobileNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DateOfBirth", (object)model.DateOfBirth ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Gender", (object)model.Gender ?? "Male");
                        cmd.Parameters.AddWithValue("@Country", (object)model.Country ?? "India");
                        cmd.Parameters.AddWithValue("@Address", (object)model.Address ?? DBNull.Value);

                        cmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Profile updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update profile: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddMasterPassenger([FromBody] MasterPassenger model)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                if (string.IsNullOrWhiteSpace(model.FullName))
                    return Json(new { success = false, message = "Passenger name is required." });

                int age = model.Age ?? 25;
                if (model.DateOfBirth.HasValue)
                {
                    age = DateTime.Now.Year - model.DateOfBirth.Value.Year;
                    if (DateTime.Now.DayOfYear < model.DateOfBirth.Value.DayOfYear) age--;
                }

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"
                        INSERT INTO MasterPassengers (UserId, Concession, FullName, DateOfBirth, Age, Nationality, Gender, BerthPreference, FoodChoice, IdCardType, IdCardNumber, VerificationStatus, CreatedAt)
                        VALUES (@UserId, @Concession, @FullName, @DateOfBirth, @Age, @Nationality, @Gender, @BerthPreference, @FoodChoice, @IdCardType, @IdCardNumber, @VerificationStatus, GETUTCDATE());
                    ";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Concession", model.Concession ?? "Normal User");
                        cmd.Parameters.AddWithValue("@FullName", model.FullName);
                        cmd.Parameters.AddWithValue("@DateOfBirth", (object)model.DateOfBirth ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Age", age);
                        cmd.Parameters.AddWithValue("@Nationality", model.Nationality ?? "Indian");
                        cmd.Parameters.AddWithValue("@Gender", model.Gender ?? "Male");
                        cmd.Parameters.AddWithValue("@BerthPreference", model.BerthPreference ?? "No Preference");
                        cmd.Parameters.AddWithValue("@FoodChoice", model.FoodChoice ?? "Veg");
                        cmd.Parameters.AddWithValue("@IdCardType", model.IdCardType ?? "AADHAAR");
                        cmd.Parameters.AddWithValue("@IdCardNumber", (object)model.IdCardNumber ?? "AADHAR ID/VIRTUAL ID");
                        cmd.Parameters.AddWithValue("@VerificationStatus", string.IsNullOrEmpty(model.VerificationStatus) ? "Verified" : model.VerificationStatus);

                        cmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Passenger added to master list successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to add passenger: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DeleteMasterPassenger([FromBody] int id)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("DELETE FROM MasterPassengers WHERE Id = @Id AND UserId = @UserId", conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                            return Json(new { success = true, message = "Passenger deleted from master list." });
                        else
                            return Json(new { success = false, message = "Passenger not found." });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to delete: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetMasterPassenger(int id)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("SELECT * FROM MasterPassengers WHERE Id = @Id AND UserId = @UserId", conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var passenger = new
                                {
                                    id = Convert.ToInt32(reader["Id"]),
                                    concession = reader["Concession"].ToString(),
                                    fullName = reader["FullName"].ToString(),
                                    dob = reader["DateOfBirth"] != DBNull.Value ? Convert.ToDateTime(reader["DateOfBirth"]).ToString("yyyy-MM-dd") : "",
                                    age = reader["Age"] != DBNull.Value ? Convert.ToInt32(reader["Age"]) : (int?)null,
                                    nationality = reader["Nationality"].ToString(),
                                    gender = reader["Gender"].ToString(),
                                    berth = reader["BerthPreference"].ToString(),
                                    food = reader["FoodChoice"].ToString(),
                                    idType = reader["IdCardType"].ToString(),
                                    idNumber = reader["IdCardNumber"] != DBNull.Value ? reader["IdCardNumber"].ToString() : ""
                                };
                                return Json(new { success = true, passenger });
                            }
                            return Json(new { success = false, message = "Passenger not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult UpdateMasterPassenger([FromBody] MasterPassenger model)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                if (string.IsNullOrWhiteSpace(model.FullName))
                    return Json(new { success = false, message = "Passenger name is required." });

                int age = model.Age ?? 25;
                if (model.DateOfBirth.HasValue)
                {
                    age = DateTime.Now.Year - model.DateOfBirth.Value.Year;
                    if (DateTime.Now.DayOfYear < model.DateOfBirth.Value.DayOfYear) age--;
                }

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"
                        UPDATE MasterPassengers
                        SET Concession = @Concession,
                            FullName = @FullName,
                            DateOfBirth = @DateOfBirth,
                            Age = @Age,
                            Nationality = @Nationality,
                            Gender = @Gender,
                            BerthPreference = @BerthPreference,
                            FoodChoice = @FoodChoice,
                            IdCardType = @IdCardType,
                            IdCardNumber = @IdCardNumber
                        WHERE Id = @Id AND UserId = @UserId;
                    ";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", model.Id);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Concession", model.Concession ?? "Normal User");
                        cmd.Parameters.AddWithValue("@FullName", model.FullName);
                        cmd.Parameters.AddWithValue("@DateOfBirth", (object)model.DateOfBirth ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Age", age);
                        cmd.Parameters.AddWithValue("@Nationality", model.Nationality ?? "Indian");
                        cmd.Parameters.AddWithValue("@Gender", model.Gender ?? "Male");
                        cmd.Parameters.AddWithValue("@BerthPreference", model.BerthPreference ?? "No Preference");
                        cmd.Parameters.AddWithValue("@FoodChoice", model.FoodChoice ?? "Veg");
                        cmd.Parameters.AddWithValue("@IdCardType", model.IdCardType ?? "AADHAAR");
                        cmd.Parameters.AddWithValue("@IdCardNumber", (object)model.IdCardNumber ?? DBNull.Value);

                        int rows = cmd.ExecuteNonQuery();
                        if (rows > 0)
                        {
                            return Json(new { success = true, message = "Master passenger updated successfully!" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Passenger record not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to update passenger: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddFavoriteJourney([FromBody] FavoriteJourney model)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                if (string.IsNullOrWhiteSpace(model.TrainNumber) || string.IsNullOrWhiteSpace(model.FromStation) || string.IsNullOrWhiteSpace(model.ToStation))
                    return Json(new { success = false, message = "Train Number, From Station, and To Station are required." });

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"
                        INSERT INTO FavoriteJourneys (UserId, TrainNumber, TrainName, ClassCode, ClassName, FromStation, FromStationCode, ToStation, ToStationCode, Quota, CreatedAt)
                        VALUES (@UserId, @TrainNumber, @TrainName, @ClassCode, @ClassName, @FromStation, @FromStationCode, @ToStation, @ToStationCode, @Quota, GETUTCDATE());
                    ";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@TrainNumber", model.TrainNumber);
                        cmd.Parameters.AddWithValue("@TrainName", (object)model.TrainName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ClassCode", model.ClassCode ?? "3A");
                        cmd.Parameters.AddWithValue("@ClassName", model.ClassName ?? (model.ClassCode == "SL" ? "Sleeper (SL)" : "AC 3 Tier (3A)"));
                        cmd.Parameters.AddWithValue("@FromStation", model.FromStation);
                        cmd.Parameters.AddWithValue("@FromStationCode", (object)model.FromStationCode ?? model.FromStation);
                        cmd.Parameters.AddWithValue("@ToStation", model.ToStation);
                        cmd.Parameters.AddWithValue("@ToStationCode", (object)model.ToStationCode ?? model.ToStation);
                        cmd.Parameters.AddWithValue("@Quota", model.Quota ?? "GENERAL");

                        cmd.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Favorite journey saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to save favorite journey: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DeleteFavoriteJourney([FromBody] int id)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("DELETE FROM FavoriteJourneys WHERE Id = @Id AND UserId = @UserId", conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows > 0)
                            return Json(new { success = true, message = "Favorite journey removed." });
                        else
                            return Json(new { success = false, message = "Journey not found." });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to delete: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddWalletMoney([FromBody] decimal amount)
        {
            try
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(userId))
                    return Json(new { success = false, message = "User not logged in." });

                if (amount <= 0)
                    return Json(new { success = false, message = "Please enter a valid positive amount." });

                decimal newBalance = 0;

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string sql = @"
                        IF EXISTS (SELECT 1 FROM UserProfiles WHERE UserId = @UserId)
                        BEGIN
                            UPDATE UserProfiles
                            SET WalletBalance = WalletBalance + @Amount
                            WHERE UserId = @UserId;
                        END
                        ELSE
                        BEGIN
                            INSERT INTO UserProfiles (UserId, WalletBalance)
                            VALUES (@UserId, @Amount);
                        END

                        SELECT WalletBalance FROM UserProfiles WHERE UserId = @UserId;
                    ";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@Amount", amount);

                        object result = cmd.ExecuteScalar();
                        if (result != null) newBalance = Convert.ToDecimal(result);
                    }
                }

                return Json(new { success = true, newBalance = newBalance, message = $"₹{amount:F2} added to your e-Wallet successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to add money: " + ex.Message });
            }
        }
    }
}
