namespace IRCTCClone.Models;

public class ConfirmBookingVM
{
    public int TrainId { get; set; }
    public int ClassId { get; set; }
    public DateTime JourneyDate { get; set; }

    public int FromStationId { get; set; }
    public int ToStationId { get; set; }

    public List<string> PassengerNames { get; set; }
    public List<int> PassengerAges { get; set; }
    public List<string> PassengerGenders { get; set; }
    public List<string> PassengerBerths { get; set; }

    public string CaptchaInput { get; set; }
    public string Otp { get; set; }
}
