﻿using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Models
{
    public class TrainRoute
    {
        public int Id { get; set; }
        public int TrainId { get; set; }
        public int StationId { get; set; }
        public int StopNumber { get; set; }
        public TimeSpan? ArrivalTime { get; set; }
        public TimeSpan? DepartureTime { get; set; }

        // Navigation properties
        public Train Train { get; set; }
        public Station Station { get; set; }


    }
}
