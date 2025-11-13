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
        public DateTime BookingDate { get; set; }

        public string Status { get; set; } = "CONFIRMED"; // CONFIRMED / CANCELLED

        public List<Passenger> Passengers { get; set; } = new();

        public int BookingId { get; set; }
        public int TrainNumber { get; set; }
        public string TrainName { get; set; } = null!;
        public string ClassCode { get; set; } = null!;

        public string Quota { get; set; } // 🆕 General / Tatkal / Ladies / Senior
        public string Frmst { get; set; } = null!;       // 🆕 From Station Name
        public string Tost { get; set; } = null!;        // 🆕 To Station Name

        public int ClassId { get; set; }
        /*        public decimal TotalFare { get; set; }*/
        /*        public string PaymentMethod { get; set; } = "QR"; // Default, can change later*/

        // 💰 Fare details (clean, complete version)
        public decimal BaseFare { get; set; }         // Basic class fare
        public decimal TatkalExtra { get; set; }      // Extra fare for Tatkal quota
        public decimal SurgeAmount { get; set; }      // Dynamic pricing increase
        public decimal GST { get; set; }              // GST amount (5%)
        public decimal TotalFare { get; set; }        // Final fare (Base + extras + GST)
        public decimal FinalFare { get; set; }
        public decimal QuotaCharge { get; set; }  // for Ladies/Senior etc
        public List<TrainClass> bf { get; set; } 
    }
}
