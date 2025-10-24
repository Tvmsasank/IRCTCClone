namespace IrctcClone.Models
{
    public class Passenger
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }
        public string Name { get; set; } = null!;
        public int Age { get; set; }
        public string Gender { get; set; } = null!;
        public string? SeatNumber { get; set; } // assigned after booking
    }
}
