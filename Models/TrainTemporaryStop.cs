using System;

namespace IRCTCClone.Models
{
    public class TrainTemporaryStop
    {
        public int Id { get; set; }
        public int TrainId { get; set; }
        public int StationId { get; set; }
        public string? StationName { get; set; }
        public string? StationCode { get; set; }
        public TimeSpan? ArrivalTime { get; set; }
        public TimeSpan? DepartureTime { get; set; }
        public int RouteDay { get; set; } = 1;
        public int StopNumber { get; set; } = 0;
        public int AfterStationId { get; set; } = 0;
        public int Distance { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string? Reason { get; set; } = "Festival Season";
        public string? DatesInput { get; set; } // Raw string from Flatpickr multi-date calendar
        public Station? Station { get; set; }
    }
}
