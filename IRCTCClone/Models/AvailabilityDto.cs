using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Models
{
    public class AvailabilityDto
    {
        public DateTime Date { get; set; }
        public int AvailableSeats { get; set; }
        public decimal Fare { get; set; }
        public bool IsWeekend { get; set; }
        public bool IsTatkalWindow { get; set; }
    }
}
