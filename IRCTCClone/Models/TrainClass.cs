using Microsoft.Data.SqlClient;
using System.Data;

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



        // ✅ Get all classes for a specific train
        public static List<TrainClass> GetClasses(string connectionString, int trainId)
        {
            var classes = new List<TrainClass>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spLoadTrainClasses", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            classes.Add(new TrainClass
                            {
                                Id = reader.GetInt32(0),
                                TrainId = reader.GetInt32(1),
                                Code = reader.GetString(2),
                                SeatPrefix = reader.GetString(3),
                                BaseFare = reader.GetDecimal(4),
                                SeatsAvailable = reader.GetInt32(5)
                            });
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
                    cmd.Parameters.AddWithValue("@BaseFare", BaseFare);
                    cmd.Parameters.AddWithValue("@SeatsAvailable", SeatsAvailable);
                    cmd.ExecuteNonQuery();
                }
            }
        }


        // ✅ Method to delete train via stored procedure
        public static void DeleteTrain(string connectionString, int trainId)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spDeleteTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
