namespace IrctcClone.Models
{
    public class CheckoutPayload
    {
        public int TrainId { get; set; }
        public int ClassId { get; set; }
        public string JourneyDate { get; set; }
        public string ClassCode { get; set; }
        public string TrainName { get; set; }
        public int FromStationId { get; set; }
        public int userfromid { get; set; }
        public int usertoid { get; set; }
        public int ToStationId { get; set; }
        public int NumPassengers { get; set; }
        public string  SeatStatus { get; set; }
        public string  FromStation { get; set; }
        public string  FromStation1 { get; set; }
        public string  ToStation { get; set; }
        public string  ToStation1 { get; set; }
        public string Departure { get; set; }
        public string Arrival { get; set; }
        public string Duration { get; set; }

     
    }

}
