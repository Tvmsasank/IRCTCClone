using IRCTCClone.Models;

namespace IRCTCClone.Services
{
    public interface IBookingService
    {
        SeatStatus GetSeatStatus(
            int trainId,
            int classId,
            DateTime journeyDate,
            string quota
        );

        int GetBookedSeatsCount(
            int trainId,
            int classId
        );

        decimal AddQuotaCharge(
            decimal existingFare,
            TrainClass cls,
            int passengerCount,
            string quota,
            out decimal quotaCharge
        );

        decimal CalculateConvenienceFee(
            string paymentMode
        );

        Station GetStationById(
            int stationId
        );
    }
}