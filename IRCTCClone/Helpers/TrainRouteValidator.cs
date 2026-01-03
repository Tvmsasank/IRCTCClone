using IRCTCClone.Models;
using System;
using Microsoft.Data.SqlClient;

namespace IRCTCClone.Helpers
{ 
    public static class TrainRouteValidator
    {
        public static TrainRouteCheckResult Validate(int searchedFromId, int searchedToId, Train selectedTrain, string FromStationName1, string ToStationName1, string FromStation, string ToStation)
        {
            var result = new TrainRouteCheckResult
            {
                IsValid = (searchedFromId == selectedTrain.FromStationId &&
                       searchedToId == selectedTrain.ToStationId),

                /* ActualFrom = selectedTrain.FromStationId,
                 ActualTo = selectedTrain.ToStationId,*/


                ActualFrom = FromStationName1,
                ActualTo = ToStationName1,


                SearchedFrom = FromStation.Contains("(")
                ? FromStation[(FromStation.IndexOf('(') + 1)..FromStation.IndexOf(')')]
                : FromStation,

                SearchedTo = ToStation.Contains("(")
                ? ToStation[(ToStation.IndexOf('(') + 1)..ToStation.IndexOf(')')]
                : ToStation,

            };

            return result;
        }

        public class TrainRouteCheckResult
        {
            public bool IsValid { get; set; }
            public string ActualFrom { get; set; }
            public string ActualTo { get; set; }
            public string SearchedFrom { get; set; }
            public string SearchedTo { get; set; }

        }
    }
}




/*        public static void ValidateTrainRoute(int searchedFromId, int searchedToId, Train selectedTrain, string connectionString)
        {
            if (searchedFromId != selectedTrain.FromStationId || searchedToId != selectedTrain.ToStationId)
            {
                throw new InvalidOperationException(
                    $"You searched trains from {GetStationCode(searchedFromId, connectionString)} to {GetStationCode(searchedToId, connectionString)} " +
                    $"but booking from {GetStationCode(selectedTrain.FromStationId, connectionString)} to {GetStationCode(selectedTrain.ToStationId, connectionString)}. " +
                    $"Do you want to continue with it?");
            }
        }

        public static string GetStationCode(int stationId, string connectionString)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT Code FROM Stations WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", stationId);
                    return cmd.ExecuteScalar()?.ToString() ?? "";
                }
            }
        }
    }*/

