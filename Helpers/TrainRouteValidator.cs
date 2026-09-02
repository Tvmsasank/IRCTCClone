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

                ActualFromId = selectedTrain.FromStationId,
                ActualToId = selectedTrain.ToStationId,

                ActualFrom = FromStationName1,
                ActualTo = ToStationName1,


                SearchedFrom = FormatStation(FromStation),
                SearchedTo = FormatStation(ToStation),
            };

            return result;
        }

        private static string FormatStation(string station)
        {
            if (string.IsNullOrEmpty(station))
                return station;

            // Example input:
            // "KACHEGUDA - KCG (SECUNDERABAD)"

            string name = "";
            string code = "";

            // Extract CODE (KCG)
            if (station.Contains("-"))
            {
                var parts = station.Split('-');
                if (parts.Length > 1)
                {
                    var rightPart = parts[1];

                    // rightPart = " KCG (SECUNDERABAD)"
                    code = rightPart.Trim().Split(' ')[0]; // KCG
                }
            }

            // Extract NAME (SECUNDERABAD)
            if (station.Contains("(") && station.Contains(")"))
            {
                name = station[(station.IndexOf('(') + 1)..station.IndexOf(')')];
            }

            // Final IRCTC format
            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                return $"{code} ({name})";

            return station;
        }

        public class TrainRouteCheckResult
        {
            public bool IsValid { get; set; }
            public int ActualFromId { get; set; }
            public int ActualToId { get; set; }
            public string ActualFrom { get; set; }
            public string ActualTo { get; set; }
            public string SearchedFrom { get; set; }
            public string SearchedTo { get; set; }

        }
    }
}