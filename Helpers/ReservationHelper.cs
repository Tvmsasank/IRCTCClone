using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Helpers
{
    public static class ReservationHelper
    {
        public static (DateTime min, DateTime max, bool isLocked) GetReservationWindow()
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            DateTime today = indiaTime.Date;

            DateTime maxDate = today.AddDays(63);   // 63 days window

            bool isBeforeOpeningTime = indiaTime.TimeOfDay < new TimeSpan(8, 0, 0);

            return (today, maxDate, isBeforeOpeningTime);
        }

        public static bool IsDateAllowed(DateTime journeyDate)
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            DateTime today = indiaTime.Date;
            DateTime maxDate = today.AddDays(60);

            // 🔴 If before 8 AM → last date NOT allowed
            if (indiaTime.TimeOfDay < new TimeSpan(8, 0, 0))
            {
                maxDate = maxDate.AddDays(-1);
            }

            return journeyDate.Date >= today && journeyDate.Date <= maxDate;
        }

        //public static bool IsBookingOpen( DateTime? bookingOpenDate, TimeSpan? bookingWindowTime, bool isBookingEnabled)
        //{
        //    if (!isBookingEnabled)
        //        return false;

        //    if (bookingOpenDate == null)
        //        return false;

        //    var indiaTime =
        //        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
        //            DateTime.UtcNow,
        //            "India Standard Time"
        //        );

        //    DateTime openDateTime =
        //        bookingOpenDate.Value.Date +
        //        (bookingWindowTime ?? new TimeSpan(8, 0, 0));

        //    return indiaTime >= openDateTime;
        //}
    }
}
