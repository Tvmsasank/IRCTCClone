using Microsoft.AspNetCore.Mvc;
using IRCTCClone.Models;

namespace IRCTCClone.Helpers
{
    public static class DemandPredictionHelper
    {
        public static string GetDemandTag(int SeatsAvailable)
        {
            if (SeatsAvailable <= 10)
                return "High Demand 🔥";

            if (SeatsAvailable <= 30)
                return "Filling Fast ⏳";

            return "Available ✅";
        }

        public static string GetDemandClass(int SeatsAvailable)
        {
            if (SeatsAvailable <= 10)
                return "demand-high";

            if (SeatsAvailable <= 30)
                return "demand-medium";

            return "demand-low";
        }
    }
}