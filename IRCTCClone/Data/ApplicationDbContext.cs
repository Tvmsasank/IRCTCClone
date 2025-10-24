using Microsoft.Data.SqlClient;
using IrctcClone.Models;

namespace IrctcClone.Data
{
    public class Database
    {
        private readonly string _connectionString;

        public Database(string connectionString)
        {
            _connectionString = connectionString;
        }

        // Example: Get all stations
        public List<Station> GetStations()
        {
            var stations = new List<Station>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT Id, Code, Name FROM Stations ORDER BY Name", conn))
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

            return stations;
        }

        // Example: Insert a station
        public int AddStation(Station station)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    "INSERT INTO Stations (Code, Name) VALUES (@Code, @Name); SELECT SCOPE_IDENTITY();", conn))
                {
                    cmd.Parameters.AddWithValue("@Code", station.Code);
                    cmd.Parameters.AddWithValue("@Name", station.Name);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        // You can add similar methods for Trains, TrainClasses, Bookings, and Passengers
    }
}
