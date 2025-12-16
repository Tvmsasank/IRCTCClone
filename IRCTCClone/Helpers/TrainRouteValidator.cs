using IRCTCClone.Models;
using System;
using Microsoft.Data.SqlClient;

namespace IRCTCClone.Helpers
{



  
public static class TrainRouteValidator
    {


        public static TrainRouteCheckResult Validate(int searchedFromId, int searchedToId, Train selectedTrain)
        {
            var result = new TrainRouteCheckResult
            {
                IsValid = (searchedFromId == selectedTrain.FromStationId &&
                           searchedToId == selectedTrain.ToStationId),

                ActualFrom = selectedTrain.FromStationId,
                ActualTo = selectedTrain.ToStationId,
                SearchedFrom = searchedFromId,
                SearchedTo = searchedToId
            };

            return result;
        }

    public class TrainRouteCheckResult
    {
        public bool IsValid { get; set; }
        public int ActualFrom { get; set; }
        public int ActualTo { get; set; }
        public int SearchedFrom { get; set; }
        public int SearchedTo { get; set; }
}




        public static void ValidateTrainRoute(int searchedFromId, int searchedToId, Train selectedTrain, string connectionString)
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
    }
}
