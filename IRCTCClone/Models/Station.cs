using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class Station
    {
        public int Id { get; set; } // Do not set manually!
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        // ✅ Static method to fetch all stations
        public static List<Station> GetAll(string connectionString)
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spgetallstatns", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Code = reader.GetString(1),
                                Name = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return stations;
        }

        // ✅ New method to search stations
        public static List<Station> SearchStations(string connectionString, string searchText)
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spSearchStations", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@SearchText", searchText ?? string.Empty);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Code = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return stations;
        }


        // ✅ 3️⃣ Autocomplete / GetStations by search term
        public static List<Station> GetStationsByTerm(string connectionString, string term)
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Term", term ?? string.Empty);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Code = reader.GetString(1),
                                Name = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return stations;
        }


        // ✅ Method to fetch all stations for dropdowns (CreateTrain)
        public static List<Station> GetAllStations(string connectionString)
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetAllStations", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            stations.Add(new Station
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1)
                            });
                        }
                    }
                }
            }

            return stations;
        }
    }
}
