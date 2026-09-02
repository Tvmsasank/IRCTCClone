using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using IRCTCClone.Helpers;

namespace IRCTCClone.Models
{
    public class TrainClass
    {
        public int Id { get; set; }

        public int TrainId { get; set; }
        public Train? Train { get; set; }
        public string Code { get; set; } = null!;         // e.g., 1A, 2A, SL, CC
        public string SeatPrefix { get; set; } = null!;   // e.g., A, B, S, C — for seat numbering

        // 🧾 Base Fare Details
        public decimal BaseFare { get; set; }             // Core fare (per seat, before extras)
        public int SeatsAvailable { get; set; }
        public int TatkalSeats { get; set; }           // Number of seats reserved for Tatkal quota
        public int TatkalRAC { get; set; }        // Number of RAC seats reserved for Tatkal quota
        public int TatkalWL { get; set; }         // Number of WL seats reserved for Tatkal quota

        // 🎟️ Quota & Pricing Options
        public string? Quota { get; set; }
        public bool DynamicPricing { get; set; } = false; // Enable/disable surge pricing
        public decimal TatkalExtra { get; set; } = 0;     // Extra cost for Tatkal quota

        // 💰 Computed Fields (Not Stored in DB)
        public decimal GST { get; set; } = 0;             // 5% GST calculated dynamically
        public decimal SurgeAmount { get; set; } = 0;     // Surge addition if demand high
        public decimal FinalFare { get; set; } = 0;       // Total fare per passenger
                                                          // ✅ Add these
        public int RACSeats { get; set; }      // Number of RAC seats allowed
        public int WLSeats { get; set; }       // Number of waiting list seats allowed

        // These are computed remaining values (to be populated)
        public int RemainingCNF { get; set; }
        public int RacCount { get; set; }
        public int RemainingWL { get; set; }
        public int ConfirmedCount { get; set; }
        public DateTime JourneyDate { get; set; }
        public List<AvailabilityDto> DateWiseAvailability { get; set; }
        public int TotalSeats { get; set; }
        public decimal BasicFare { get; set; }
        public decimal ReservationCharge { get; set; }
        public decimal SuperfastCharge { get; set; }
        public decimal GSTAmount { get; set; }
        public decimal TotalFare { get; set; }



        // Get all classes for a specific train
        public static List<TrainClass> GetClasses(string connectionString, int trainId, int fromStationId, int toStationId, DateTime journeyDate, string quota)
        {
            var classes = new List<TrainClass>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spLoadTrainClassAvailability", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            decimal fare = reader.IsDBNull(4) ? 0 : Convert.ToDecimal(reader[4]);

                            classes.Add(new TrainClass
                            {
                                Id = reader.GetInt32(0),
                                TrainId = reader.GetInt32(1),
                                Code = reader.GetString(2),
                                SeatPrefix = reader.GetString(3),

                                BaseFare = fare,

                                BasicFare = reader["BasicFare"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["BasicFare"]),
                                ReservationCharge = reader["ReservationCharge"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ReservationCharge"]),
                                SuperfastCharge = reader["SuperFastCharge"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["SuperFastCharge"]),
                                GSTAmount = reader["GST"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["GST"]),
                                TotalFare = reader["TotalFare"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["TotalFare"]),

                                // Temporary hold
                                TotalSeats = reader["TotalSeats"] == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalSeats"]),
                                RACSeats = reader["TotalRACSeats"] == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalRACSeats"]),
                                TatkalSeats = reader["TotalTatkalSeats"] == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalTatkalSeats"]),
                                ConfirmedCount = reader["CNFCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["CNFCount"]),
                                RacCount = reader["RACCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["RACCount"]),
                                WLSeats = reader["WLCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["WLCount"]),
                            });
                        }
                    }
                }

                // Standardize quota parameter
                string targetQuota = string.IsNullOrWhiteSpace(quota) ? "GENERAL" : quota.ToUpper().Trim();
                if (targetQuota == "PT") targetQuota = "PREMIUM TATKAL";
                if (targetQuota == "SS") targetQuota = "SENIOR";

                // Now calculate the exact available seats matching the selected quota
                foreach (var cls in classes)
                {
                    // A. Get total seats in this quota for the train class
                    int totalQuotaSeats = 0;

                    using (var qCmd = new SqlCommand("spGetTotalQuotaSeats", conn))
                    {

                        qCmd.CommandType = CommandType.StoredProcedure;

                        qCmd.Parameters.AddWithValue("@TrainId", trainId);
                        qCmd.Parameters.AddWithValue("@ClassId", cls.Id);
                        qCmd.Parameters.AddWithValue("@Quota", targetQuota);

                        totalQuotaSeats = Convert.ToInt32(qCmd.ExecuteScalar());
                    }

                    // B. Get confirmed passengers for this class, date, and quota
                    int cnfQuotaCount = 0;

                    using (var cCmd = new SqlCommand("spGetConfirmedQuotaCount", conn))
                    {
                        cCmd.CommandType = CommandType.StoredProcedure;

                        cCmd.Parameters.AddWithValue("@TrainId", trainId);
                        cCmd.Parameters.AddWithValue("@ClassId", cls.Id);
                        cCmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);
                        cCmd.Parameters.AddWithValue("@Quota", targetQuota);

                        cnfQuotaCount = Convert.ToInt32(cCmd.ExecuteScalar());
                    }

                    // If coaches are seeded, calculate strictly based on Quota range!
                    if (totalQuotaSeats > 0)
                    {
                        cls.SeatsAvailable = Math.Max(totalQuotaSeats - cnfQuotaCount, 0);
                    }
                    else
                    {
                        // Fallback to legacy behavior if dynamic seats aren't seeded yet
                        if (targetQuota == "TATKAL" || targetQuota == "PREMIUM TATKAL")
                        {
                            cls.SeatsAvailable = cls.TatkalSeats;
                        }
                        else
                        {
                            cls.SeatsAvailable = Math.Max(cls.TotalSeats - cls.ConfirmedCount, 0);
                        }
                    }
                }
            }

            return classes;
        }

        // ✅ Insert TrainClass
        public void Insert(string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spInsertTrainClass", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", TrainId);
                    cmd.Parameters.AddWithValue("@Code", Code);
                    cmd.Parameters.AddWithValue("@SeatPrefix", (object?)SeatPrefix ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SeatsAvailable", SeatsAvailable);

                    cmd.ExecuteNonQuery();
                }
            }
        }


        // ✅ Method to delete train via stored procedure
        public static void DeleteTrain(string connectionString, int trainId, string trainType)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spDeleteTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@TrainType", trainType);

                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
