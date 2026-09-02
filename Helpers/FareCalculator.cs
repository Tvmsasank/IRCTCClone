using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Helpers
{
    public static class FareCalculator
    {
        public static decimal CalculateFare(string connectionString, int trainId, int fromStationId, int toStationId, string classCode)
        {
            decimal fare = 0;

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (SqlCommand cmd = new SqlCommand("spCalculateFare", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);
                    cmd.Parameters.AddWithValue("@ClassCode", classCode);

                    var result = cmd.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        fare = Convert.ToDecimal(result);
                    }
                }
            }

            return fare;
        }
    }
}