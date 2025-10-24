using IRCTCClone.Models;
using Microsoft.Data.SqlClient;

namespace IRCTCClone.Data
{
    public static class DbSeeder
    {
        public static void Seed(string connectionString)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Check if Stations exist
                using (SqlCommand checkCmd = new SqlCommand("SELECT COUNT(*) FROM Stations", conn))
                {
                    int count = (int)checkCmd.ExecuteScalar();
                    if (count > 0) return; // Already seeded
                }

                // Insert Stations
                var stations = new[]
                {
                    new Station { Code = "NDLS", Name = "New Delhi" },
                    new Station { Code = "MMCT", Name = "Mumbai Central" },
                    new Station { Code = "CSTM", Name = "Chhatrapati Shivaji Maharaj Terminus" },
                    new Station { Code = "PNBE", Name = "Patna Junction" },
                    new Station { Code = "SC", Name = "Secunderabad Junction"},
                    new Station { Code = "MAS", Name = "Chennai Central"},
                    new Station { Code = "VSKP", Name = "Vishakapatnam"},
                    new Station { Code = "TPTY", Name = "Tirupati"},
                    new Station { Code = "BZA", Name = "Vijaywada"}
                };

                foreach (var s in stations)
                {
                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO Stations (Code, Name) VALUES (@Code, @Name); SELECT SCOPE_IDENTITY();", conn))
                    {
                        cmd.Parameters.AddWithValue("@Code", s.Code);
                        cmd.Parameters.AddWithValue("@Name", s.Name);
                        s.Id = Convert.ToInt32(cmd.ExecuteScalar()); // get inserted ID
                    }
                }

                // Insert Trains
                var t1 = new Train
                {
                    Number = 12424,
                    Name = "Rajdhani Express",
                    FromStationId = stations[0].Id,
                    ToStationId = stations[3].Id,
                    Departure = new TimeSpan(16, 0, 0),
                    Arrival = new TimeSpan(6, 30, 0),
                    Duration = new TimeSpan(14, 0, 0)
                };

                var t2 = new Train
                {
                    Number = 22434,
                    Name = "Duronto Express",
                    FromStationId = stations[0].Id,
                    ToStationId = stations[1].Id,
                    Departure = new TimeSpan(9, 0, 0),
                    Arrival = new TimeSpan(22, 10, 0),
                    Duration = new TimeSpan(16, 0 , 0)
                };

                var trains = new[] { t1, t2 };

                foreach (var t in trains)
                {
                    using (SqlCommand cmd = new SqlCommand(
                        @"INSERT INTO Trains (Number, Name, FromStationId, ToStationId, Departure, Arrival, Duration) 
                          VALUES (@Number, @Name, @FromStationId, @ToStationId, @Departure, @Arrival, @Duration);
                          SELECT SCOPE_IDENTITY();", conn))
                    {
                        cmd.Parameters.AddWithValue("@Number", t.Number);
                        cmd.Parameters.AddWithValue("@Name", t.Name);
                        cmd.Parameters.AddWithValue("@FromStationId", t.FromStationId);
                        cmd.Parameters.AddWithValue("@ToStationId", t.ToStationId);
                        cmd.Parameters.AddWithValue("@Departure", t.Departure);
                        cmd.Parameters.AddWithValue("@Arrival", t.Arrival);
                        cmd.Parameters.AddWithValue("@Duration", t.Duration);
                        t.Id = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                // Insert TrainClasses
                var trainClasses = new[]
                {
                    new TrainClass { TrainId = t1.Id, Code = "1A", Fare = 2500, SeatsAvailable = 8 },
                    new TrainClass { TrainId = t1.Id, Code = "2A", Fare = 1800, SeatsAvailable = 20 },
                    new TrainClass { TrainId = t1.Id, Code = "SL", Fare = 500, SeatsAvailable = 120 },
                    new TrainClass { TrainId = t2.Id, Code = "CC", Fare = 900, SeatsAvailable = 100 },
                    new TrainClass { TrainId = t2.Id, Code = "SL", Fare = 450, SeatsAvailable = 140 }
                };

                foreach (var c in trainClasses)
                {
                    using (SqlCommand cmd = new SqlCommand(
                        @"INSERT INTO TrainClasses (TrainId, Code, Fare, SeatsAvailable) 
                          VALUES (@TrainId, @Code, @Fare, @SeatsAvailable);", conn))
                    {
                        cmd.Parameters.AddWithValue("@TrainId", c.TrainId);
                        cmd.Parameters.AddWithValue("@Code", c.Code);
                        cmd.Parameters.AddWithValue("@Fare", c.Fare);
                        cmd.Parameters.AddWithValue("@SeatsAvailable", c.SeatsAvailable);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
