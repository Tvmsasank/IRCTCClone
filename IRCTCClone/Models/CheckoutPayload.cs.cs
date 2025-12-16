namespace IrctcClone.Models
{
    public class CheckoutPayload
    {
        public int TrainId { get; set; }
        public int ClassId { get; set; }
        public string JourneyDate { get; set; }
        public int FromStationId { get; set; }
        public int ToStationId { get; set; }
        public int NumPassengers { get; set; }
        public string  SeatStatus { get; set; }
    }

}
