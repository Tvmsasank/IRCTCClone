namespace IRCTCClone.Models
{
    public class TrainClass
    {
        public int Id { get; set; }
        public int TrainId { get; set; }
        public Train? Train { get; set; }
        public string Code { get; set; } = null!; // e.g., 1A, 2A, CC, SL
        public decimal Fare { get; set; }
        public int SeatsAvailable { get; set; }
    }
}
