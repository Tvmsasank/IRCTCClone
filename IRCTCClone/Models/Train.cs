using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Models
{
    public class Train
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; } = null!;
        public string FSM { get; set; } = null!;
        public string TSM { get; set; } = null!;
        public string FromStationName1  { get; set; } = null!;
        public string ToStationName1 { get; set; } = null!;
        public int FromStationId { get; set; }
        public int userfromid { get; set; }
        public int usertoid { get; set; }
        public Station? FromStation { get; set; }
        public int ToStationId { get; set; }
        public Station? ToStation { get; set; }
        public TimeSpan Departure { get; set; }
        public TimeSpan Arrival { get; set; }
        public string Duration { get; set; }
        public String FromStationName { get; set; } // ✅ use this
        public String ToStationName { get; set; }  
        public String? fscode { get; set; }  
        public String? tscode { get; set; }  
        public String? PNR { get; set; }  
        public DateTime? JourneyDate { get; set; }  
        public int Amount { get; set; }  
        public string Status { get; set; }
        public int RACSeats { get; set; }      // Number of RAC seats allowed
        public int SeatsAvailable { get; set; }
        public List<TrainClass> Classes { get; set; } = new();




        public static Train GetTrainById(string connectionString, int trainId, int classId, string journeyDate)
        {
            Train train = null;

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetTrainForCheckout", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            train = new Train
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("TrainId")),
                                Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                Name = reader.GetString(reader.GetOrdinal("TrainName")),
                                Departure = reader.GetTimeSpan(reader.GetOrdinal("Departure")),
                                Arrival = reader.GetTimeSpan(reader.GetOrdinal("Arrival")),
                                Duration = reader.GetString(reader.GetOrdinal("Duration")),
                                FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                FromStation = new Station
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("FromStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("FromStationCode"))
                                },
                                ToStation = new Station
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("ToStationId")),
                                    Name = reader.GetString(reader.GetOrdinal("ToStationName")),
                                    Code = reader.GetString(reader.GetOrdinal("ToStationCode"))
                                }
                            };
                        }
                    }
                }
            }

            return train;
        }

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
                                Duration = reader.GetString(7)
                            });
                        }
                    }
                }
            }

            return trains;
        }


        // ✅ Fetch trains between 2 stations (calls spSearchTrains)
        public static List<Train> GetTrains(string connectionString, int fromStationId, int toStationId, string journeyDateStr)
        {

            // Parse journey date from query string or default to today
            DateTime journeyDate;
            if (!DateTime.TryParse(journeyDateStr, out journeyDate))
            {
                journeyDate = DateTime.Today;
            }

            var trains = new List<Train>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("spSearchTrainsCluster", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@FromStationId", fromStationId);
                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);
/*                    cmd.Parameters.AddWithValue("@ToStationId", toStationId);
*/
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            trains.Add(new Train
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                Number = reader.GetInt32(reader.GetOrdinal("Number")),
                                Name = reader.GetString(reader.GetOrdinal("Name")),

                                FromStationId = reader.GetInt32(reader.GetOrdinal("FromStationId")),
                                ToStationId = reader.GetInt32(reader.GetOrdinal("ToStationId")),

                                Departure = reader.IsDBNull(reader.GetOrdinal("FromDeparture"))
                                    ? TimeSpan.Zero
                                    : reader.GetTimeSpan(reader.GetOrdinal("FromDeparture")),

                                Arrival = reader.IsDBNull(reader.GetOrdinal("ToArrival"))
                                    ? TimeSpan.Zero
                                    : reader.GetTimeSpan(reader.GetOrdinal("ToArrival")),

                                Duration = reader.IsDBNull(reader.GetOrdinal("Duration"))
                                    ? ""
                                    : reader.GetString(reader.GetOrdinal("Duration")),

                                FromStationName1 = reader.IsDBNull(reader.GetOrdinal("FromStation"))
                                    ? ""
                                    : reader.GetString(reader.GetOrdinal("FromStation")),

                                ToStationName1 = reader.IsDBNull(reader.GetOrdinal("ToStation"))
                                    ? ""
                                    : reader.GetString(reader.GetOrdinal("ToStation")),

                                FSM = reader.GetString(reader.GetOrdinal("FSM")),
                                TSM = reader.GetString(reader.GetOrdinal("TSM")),
                                

                                Classes = new List<TrainClass>()
                            });

                        }
                    }
                }

                // Load classes for each train
                foreach (var train in trains)
                {
                    train.Classes = TrainClass.GetClasses(connectionString, train.Id, journeyDate);
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
                                    Duration = reader.GetString(reader.GetOrdinal("Duration")),
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
                                    BaseFare = reader.GetDecimal(reader.GetOrdinal("Fare")),
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
                                Duration = reader.GetString(7),
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
                                Duration = reader["Duration"].ToString(),
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
