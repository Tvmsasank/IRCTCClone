/*using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using IRCTCClone.Models;
using Microsoft.Data.SqlClient;

namespace IRCTCClone.Data
{
    public static class UserRepository
    {
        public static ViewModels GetUserByEmail(string email, string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("sp_GetUserByEmail", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Email", email);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ViewModels
                            {
                                Id = reader.GetInt32(0),
                                Email = reader.GetString(1),
                                PasswordHash = reader.GetString(2),
                                FullName = reader.GetString(3),
                                AadhaarNumber = reader.IsDBNull(4) ? null : reader.GetString(4),
                                AadhaarVerified = reader.GetBoolean(5)
                            };
                        }
                    }
                }
            }

            return null;
        }


        public static void UpdateAadhaar(string email, string aadhaar, string cs)
        {
            using (var conn = new SqlConnection(cs))
            {
                conn.Open();
                var cmd = new SqlCommand(
                    "UPDATE Users SET AadhaarNumber=@A, AadhaarVerified=1 WHERE Email=@E", conn);

                cmd.Parameters.AddWithValue("@A", aadhaar);
                cmd.Parameters.AddWithValue("@E", email);

                cmd.ExecuteNonQuery();
            }
        }
    }
}

*/