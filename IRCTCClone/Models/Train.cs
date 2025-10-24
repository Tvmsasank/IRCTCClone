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
        public String ToStationName { get; set; }   // ✅ use this
        public List<TrainClass> Classes { get; set; } = new();
    }
}
