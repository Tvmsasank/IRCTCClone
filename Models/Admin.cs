using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class Admin
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // ✅ Validate login credentials via stored procedure
        public static bool ValidateLogin(string connectionString, string username, string password)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("sp_AdminLogin", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Username", username);
                    cmd.Parameters.AddWithValue("@Password", password);

                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    return count > 0;
                }
            }
        }
    }
}
