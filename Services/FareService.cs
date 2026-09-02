using IRCTCClone.Models;

namespace IRCTCClone.Services
{
    public class FareService
    {
        public decimal AddQuotaCharge(
            decimal existingFare,
            TrainClass cls,
            int passengerCount,
            string quota,
            out decimal quotaCharge)
        {
            decimal baseFare = cls.BaseFare;

            quotaCharge = 0;

            decimal quotaChargePerPassenger = 0;

            switch (quota?.ToUpper())
            {
                case "LADIES":
                    quotaChargePerPassenger =
                        baseFare * 0.05m;
                    break;

                case "SC":
                    quotaChargePerPassenger =
                        -baseFare * 0.10m;
                    break;

                default:
                    quotaChargePerPassenger = 0;
                    break;
            }

            quotaCharge =
                quotaChargePerPassenger * passengerCount;

            return existingFare + quotaCharge;
        }

        public decimal CalculateConvenienceFee(
            string paymentMode)
        {
            decimal baseFee = 10m;

            switch (paymentMode?.ToUpper())
            {
                case "CARD":
                case "NETBANKING":
                case "WALLET":
                    baseFee = 20m;
                    break;
            }

            decimal gst = baseFee * 0.18m;

            return Math.Round(baseFee + gst, 2);
        }
    }
}