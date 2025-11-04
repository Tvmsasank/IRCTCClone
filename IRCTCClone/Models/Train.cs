using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class Train
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; } = null!;
        public int FromStationId { get; set; }
        public Station? FromStation { get; set; }
        public int ToStationId { get; set; }
        public Station? ToStation { get; set; }
        public TimeSpan Departure { get; set; }
        public TimeSpan Arrival { get; set; }
        public TimeSpan Duration { get; set; }
        public String FromStationName { get; set; } // ✅ use this
        public String ToStationName { get; set; }  
        public String? fscode { get; set; }  
        public String? tscode { get; set; }  
        public String? PNR { get; set; }  
        public DateTime? JourneyDate { get; set; }  
        public int Amount { get; set; }  
        public string Status { get; set; }  
        public List<TrainClass> Classes { get; set; } = new();


        // ✅ Static method to get trains via stored procedure
        public static List<Train> SearchTrains(string connectionString, string from, string to)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spSearchTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@From", from);
                    cmd.Parameters.AddWithValue("@To", to);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = reader.GetInt32(0),
                                Number = reader.GetInt32(1),
                                Name = reader.GetString(2),
                                FromStationId = reader.GetInt32(3),
                                ToStationId = reader.GetInt32(4),
                                Departure = reader.GetTimeSpan(5),
                                Arrival = reader.GetTimeSpan(6),
                                Duration = reader.GetTimeSpan(7)
                            });
                        }
                    }
                }
            }

            return trains;
        }


        // ✅ Fetch trains between 2 stations (calls spSearchTrains)
        public static List<Train> GetTrains(string connectionString, int fromStationId, int toStationId)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spSearchTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = reader.GetInt32(0),
                                Number = reader.GetInt32(1),
                                Name = reader.GetString(2),
                                FromStationId = reader.GetInt32(3),
                                FromStation = new Station { Code = reader.GetString(4) },
                                ToStationId = reader.GetInt32(5),
                                ToStation = new Station { Code = reader.GetString(6) },
                                Departure = reader.GetTimeSpan(7),
                                Arrival = reader.GetTimeSpan(8),
                                Duration = reader.GetTimeSpan(9),
                                Classes = new List<TrainClass>()
                            });
                        }
                    }
                }

                // ✅ For each train, get class info
                foreach (var train in trains)
                {
                    train.Classes = TrainClass.GetClasses(connectionString, train.Id);
                }
            }

            return trains;
        }


        // ✅ Static method to get train details
        public static Train? GetTrainDetails(string connectionString, int id)
        {
            Train? train = null;

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spGetTrainDetails", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", id);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (train == null)
                            {
                                train = new Train
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                    Number = reader.GetInt32(reader.GetOrdinal("TrainNumber")),
                                    Name = reader.GetString(reader.GetOrdinal("TrainName")),
                                    FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                    FromStation = new Station
                                    {
                                        Name = reader.GetString(reader.GetOrdinal("FromName")),
                                        Code = reader.GetString(reader.GetOrdinal("FromCode"))
                                    },
                                    ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                    ToStation = new Station
                                    {
                                        Name = reader.GetString(reader.GetOrdinal("ToName")),
                                        Code = reader.GetString(reader.GetOrdinal("ToCode"))
                                    },
                                    Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                    Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                    Duration = reader.GetTimeSpan(reader.GetOrdinal("Duration")),
                                    Classes = new List<TrainClass>()
                                };
                            }

                            // Add train class details
                            if (!reader.IsDBNull(reader.GetOrdinal("ClassId")))
                            {
                                train.Classes.Add(new TrainClass
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ClassId")),
                                    TrainId = train.Id,
                                    Code = reader.GetString(reader.GetOrdinal("ClassCode")),
                                    Fare = reader.GetDecimal(reader.GetOrdinal("Fare")),
                                    SeatsAvailable = reader.GetInt32(reader.GetOrdinal("SeatsAvailable"))
                                });
                            }
                        }
                    }
                }
            }

            return train;
        }


        // ✅ Static method to fetch all trains
        public static List<Train> GetAllTrains(string connectionString)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("sp_GetAllTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = reader.GetInt32(0),
                                Number = reader.GetInt32(1),
                                Name = reader.GetString(2),
                                FromStationId = reader.GetInt32(3),
                                ToStationId = reader.GetInt32(4),
                                Departure = reader.GetTimeSpan(5),
                                Arrival = reader.GetTimeSpan(6),
                                Duration = reader.GetTimeSpan(7),
                                FromStation = new Station
                                {
                                    Id = reader.GetInt32(3),
                                    Code = reader.GetString(8),
                                    Name = reader.GetString(9)
                                },
                                ToStation = new Station
                                {
                                    Id = reader.GetInt32(4),
                                    Code = reader.GetString(10),
                                    Name = reader.GetString(11)
                                }
                            });
                        }
                    }
                }
            }

            return trains;
        }


        // ✅ Check if a duplicate train exists
        public static bool CheckDuplicate(string connectionString, int number, int fromStationId, int toStationId)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spCheckDuplicateTrain", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Number", number);
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);

                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    return count > 0;
                }
            }
        }

        // ✅ Insert Train and return new Train ID
        public void InsertTrain(string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand("spInsertTrain", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Number", Number);
                cmd.Parameters.AddWithValue("@Name", Name);
                cmd.Parameters.AddWithValue("@FromStationId", FromStationId);
                cmd.Parameters.AddWithValue("@ToStationId", ToStationId);
                cmd.Parameters.AddWithValue("@Departure", (object?)Departure ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Arrival", (object?)Arrival ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Duration", (object?)Duration ?? DBNull.Value);

                conn.Open();
                var result = cmd.ExecuteScalar(); // 👈 should return the TrainId
                if (result != null)
                    Id = Convert.ToInt32(result);  // 👈 store the TrainId in the object
            }
        }


        public static List<Train> GetTrainsList(string connectionString)
        {
            var trains = new List<Train>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetAllTrains", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Number = Convert.ToInt32(reader["Number"]),
                                Name = reader["Name"].ToString(),
                                FromStationId = Convert.ToInt32(reader["FromStationId"]),
                                ToStationId = Convert.ToInt32(reader["ToStationId"]),
                                Departure = TimeSpan.Parse(reader["Departure"].ToString()!),
                                Arrival = TimeSpan.Parse(reader["Arrival"].ToString()!),
                                Duration = TimeSpan.Parse(reader["Duration"].ToString()!),
                                FromStation = new Station
                                {
                                    Id = Convert.ToInt32(reader["FromStationId"]),
                                    Name = reader["FromStationName"].ToString()!
                                },
                                ToStation = new Station
                                {
                                    Id = Convert.ToInt32(reader["ToStationId"]),
                                    Name = reader["ToStationName"].ToString()!
                                }
                            });
                        }
                    }
                }
            }

            return trains;
        }

    }
}
