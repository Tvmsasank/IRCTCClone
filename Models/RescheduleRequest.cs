using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Models
{
    public class RescheduleRequest
    {
        public int BookingId { get; set; }
        public DateTime NewTravelDate { get; set; }
    }
}
