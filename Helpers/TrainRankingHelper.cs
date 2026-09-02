using Microsoft.AspNetCore.Mvc;
using IRCTCClone.Models;

namespace IRCTCClone.Helpers
{
    public static class TrainRankingHelper
    {
        public static List<Train> RankTrains(List<Train> trains)
        {
            foreach (var train in trains)
            {
                int score = 0;

                // 🕒 Morning preference (6AM–10AM)
                if (train.Departure.Hours >= 6 && train.Departure.Hours <= 10)
                    score += 5;

                // ⏱ Short duration boost
                if (!string.IsNullOrEmpty(train.Duration) && train.Duration.Contains("0"))
                    score += 3;

                // 💺 Seat availability boost
                if (train.Classes != null && train.Classes.Any(c => c.SeatsAvailable > 20))
                    score += 10;

                // 🧠 Premium train boost
                if (train.Name.Contains("EXPRESS") || train.Name.Contains("SUPERFAST"))
                    score += 2;

                train.Score = score;
            }

            return trains.OrderByDescending(t => t.Score).ToList();
        }
    }
}