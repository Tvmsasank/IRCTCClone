using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Models
{
    public class TrainRoute
    {
        public int Id { get; set; }
        public int TrainId { get; set; }
        public int StationId { get; set; }
        public string StationCode { get; set; }
        public string StationName { get; set; }
        public int StopNumber { get; set; }
        public TimeSpan? ArrivalTime { get; set; }
        public TimeSpan? DepartureTime { get; set; }
        public int Day { get; set; }

        // Navigation properties
        public Train Train { get; set; }
        public Station Station { get; set; }
    }
}
