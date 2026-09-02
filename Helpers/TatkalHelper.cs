using IrctcClone.Models;
using Microsoft.AspNetCore.Mvc;

namespace IrctcClone.Helpers
{
    public static class TatkalHelper
    {
        private static readonly List<TatkalWindow> TatkalWindows = new()
        {
            // AC Tatkal (10 AM)
            new TatkalWindow
            {
                Start = new TimeSpan(9, 30, 0),
                End   = new TimeSpan(10, 15, 0)
            },

            // Non-AC Tatkal (11 AM)
            new TatkalWindow
            {
                Start = new TimeSpan(10, 30, 0),
                End   = new TimeSpan(11, 15, 0)
            }
        };

        public static bool IsTatkalWindow()
        {
            TimeSpan now = DateTime.Now.TimeOfDay;

            return TatkalWindows.Any(w =>
                now >= w.Start && now <= w.End
            );
        }

        public static bool IsTatkalOpen(string classCode)
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            TimeSpan now = indiaTime.TimeOfDay;

            TimeSpan openingTime = new TimeSpan(11, 00, 0); // Default for Non AC classes

            if (classCode == "3A" 
                
                || classCode == "3E" 
                
                || classCode == "2A" 
                
                || classCode == "1A" 
                
                || classCode == "CC" 
                
                || classCode == "EC" 
                
                || classCode == "EV")
            {
                openingTime = new TimeSpan(10, 00, 0); // AC classes
            }

            return now >= openingTime;
        }

        public static bool IsTatkalAllowed(DateTime journeyDate)
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            DateTime tatkalOpenDate = journeyDate.Date.AddDays(-1);

            return indiaTime.Date >= tatkalOpenDate;
        }

        // 🔴 LOGIN WINDOW (09:45 & 10:45 logic)
        public static bool ShouldForceLogin()
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            TimeSpan now = indiaTime.TimeOfDay;

            return

                (now >= new TimeSpan(7, 30, 0) && now <= new TimeSpan(8, 15, 0))

                ||

                (now >= new TimeSpan(9, 45, 0) && now <= new TimeSpan(10, 0, 0)) 
                
                ||
                
                (now >= new TimeSpan(10, 45, 0) && now <= new TimeSpan(11, 0, 0));
        }

        // 🟡 TATKAL DEFAULT WINDOW (09:50 & 10:50 logic)
        public static bool ShouldAutoSelectTatkal()
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            TimeSpan now = indiaTime.TimeOfDay;

            return

                (now >= new TimeSpan(9, 50, 0) && now <= new TimeSpan(10, 0, 0)) 
                
                ||

                (now >= new TimeSpan(10, 50, 0) && now <= new TimeSpan(11, 0, 0));
        }

        public static (DateTime min, DateTime max) GetTatkalDateRange()
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            var tomorrow = indiaTime.Date.AddDays(1);

            return (tomorrow, tomorrow); // only 1 day
        }

        public static bool IsTatkalExclusiveWindow()
        {
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            TimeSpan now = indiaTime.TimeOfDay;

            return

                (now >= new TimeSpan(10, 00, 0) && now <= new TimeSpan(10, 10, 0))

                ||

                (now >= new TimeSpan(11, 00, 0) && now <= new TimeSpan(11, 10, 0));
        }
    }
}
