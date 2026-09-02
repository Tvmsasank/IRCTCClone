using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Models
{
    public class SevenDayAvailability : Controller
    {
        public DateTime TravelDate { get; set; }
        public int TotalSeats { get; set; }
        public int BookedSeats { get; set; }
        public int AvailableSeats { get; set; }
        public decimal FarePerDay { get; set; }
    }
}
