using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class TrainClass
    {
        public int Id { get; set; }
        public int TrainId { get; set; }
        public Train? Train { get; set; }
        public string Code { get; set; } = null!; // e.g., 1A, 2A, CC, SL
        public decimal Fare { get; set; }
        public int SeatsAvailable { get; set; }


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
                                Fare = reader.GetDecimal(3),
                                SeatsAvailable = reader.GetInt32(4)
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
                    cmd.Parameters.AddWithValue("@Fare", Fare);
                    cmd.Parameters.AddWithValue("@SeatsAvailable", SeatsAvailable);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
