namespace IRCTCClone.Models
{
    public class Train
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; } = null!;
        public int FromStationId { get; set; }
        public Station? FromStation { get; set; }
        public int ToStationId { get; set; }
        public Station? ToStation { get; set; }
        public TimeSpan Departure { get; set; }
        public TimeSpan Arrival { get; set; }
        public TimeSpan Duration { get; set; }
        public String FromStationName { get; set; } // ✅ use this
        public String ToStationName { get; set; }  
        public String? fscode { get; set; }  
        public String? tscode { get; set; }  
        public String? PNR { get; set; }  
        public DateTime? JourneyDate { get; set; }  
        public int Amount { get; set; }  
        public string Status { get; set; }  
        public List<TrainClass> Classes { get; set; } = new();
    }
}
