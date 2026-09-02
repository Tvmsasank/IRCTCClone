using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class MDTrains
    {
        public int Id { get; set; }
        public string? Type { get; set; }
        public bool RunMon { get; set; }
        public bool RunTue { get; set; }
        public bool RunWed { get; set; }
        public bool RunThu { get; set; }
        public bool RunFri { get; set; }
        public bool RunSat { get; set; }
        public bool RunSun { get; set; }


        public static List<MDTrains> GetAllTypes(string connectionString)
        {
            var list = new List<MDTrains>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainTypes", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new MDTrains
                            {
                                Id = (int)reader["Id"],
                                Type = reader["Type"].ToString()
                            });
                        }
                    }
                }
            }

            return list;
        }
    }
}
