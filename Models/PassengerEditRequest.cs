using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Models
{
    public class PassengerEditRequest : Controller
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public string Gender { get; set; }
    }
}
