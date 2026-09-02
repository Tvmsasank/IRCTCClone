/*using IrctcClone.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace IrctcClone.Controllers
{
    public class PaymentController : Controller
    {
        [HttpGet]
        public IActionResult Payment()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Payment(CheckoutPayload payload)
        {
            if (payload == null)
            {
                return RedirectToAction("TrainResults", "Train");
            }

            HttpContext.Session.SetString("CheckoutPayload", JsonConvert.SerializeObject(payload));

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VerifyQrPayment()
        {
            var payloadJson = HttpContext.Session.GetString("CheckoutPayload");

            if (payloadJson == null)
                return RedirectToAction("TrainResults", "Train");

            TempData["PaymentSuccess"] = true;
            TempData["showOtp"] = true;

            return RedirectToAction("Confirm", "Booking");
        }
    }
}
*/