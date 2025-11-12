using System.Runtime.CompilerServices;
using IRCTCClone.Models;

namespace IRCTCClone.Models
{
    public class Booking
    {
        public int Id { get; set; }
        public string PNR { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public ApplicationUser? User { get; set; }
        public int TrainId { get; set; }
        public Train? Train { get; set; }
        public int TrainClassId { get; set; }
        public TrainClass? TrainClass { get; set; }
        public DateTime JourneyDate { get; set; }
        public decimal Amount { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; } = "CONFIRMED"; // CONFIRMED / CANCELLED
        public List<Passenger> Passengers { get; set; } = new();
        public int BookingId { get; set; }
        public int TrainNumber { get; set; } 
        public string TrainName { get; set; } = null!;
        public string ClassCode { get; set; }
        public string Frmst { get; set; }
        public string Tost { get; set; }
        public int ClassId { get; set; }
        public decimal TotalFare { get; set; }
        public string PaymentMethod { get; set; }
/*        public string username { get; set; }
        public string password { get; set; }*/
        /*        public string FromStationName { get; set; } = string.Empty;
                public string ToStationName { get; set; } = string.Empty;*/
    }
}
