using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace IRCTCClone.Helpers
{
    public static class CoachLayoutHelper
    {
        // Get the Coach size (total seats per coach) based on train class
        public static int GetSeatsPerCoach(string classCode)
        {
            classCode = CleanClassCode(classCode);

            switch (classCode)
            {
                case "2A":
                    return 48; // AC 2-Tier has 48 seats per coach
                case "CC":
                    return 78; // Chair Car has 78 seats per coach
                case "SL":
                case "3A":
                case "3E":
                default:
                    return 80; // Sleeper and AC 3-Tier have 80 seats per coach
            }
        }

        // Map relative seat number inside a coach to berth type (LB, MB, UB, SL, SU, W, M, A)
        public static string GetBerthType(string classCode, int seatNum)
        {
            classCode = CleanClassCode(classCode);

            switch (classCode)
            {
                case "SL":
                case "3A":
                case "3E":
                    int slRem = seatNum % 8;
                    if (slRem == 1 || slRem == 4) return "LB"; // Lower Berth
                    if (slRem == 2 || slRem == 5) return "MB"; // Middle Berth
                    if (slRem == 3 || slRem == 6) return "UB"; // Upper Berth
                    if (slRem == 7) return "SL";              // Side Lower
                    return "SU";                              // Side Upper (0)

                case "2A":
                    int ac2Rem = seatNum % 6;
                    if (ac2Rem == 1 || ac2Rem == 3) return "LB"; // Lower Berth
                    if (ac2Rem == 2 || ac2Rem == 4) return "UB"; // Upper Berth
                    if (ac2Rem == 5) return "SL";              // Side Lower
                    return "SU";                              // Side Upper (0)

                case "CC":
                default:
                    int ccRem = seatNum % 5;
                    if (ccRem == 1 || ccRem == 5) return "W"; // Window Seat
                    if (ccRem == 2 || ccRem == 4) return "A"; // Aisle Seat
                    return "M";                              // Middle Seat
            }
        }

        // Check if an absolute seat belongs to a specific Quota logically in a coach
        public static bool IsSeatInQuota(int seatNum, string quota, int seatsPerCoach)
        {
            if (string.IsNullOrWhiteSpace(quota))
                quota = "GENERAL";

            // Map absolute seat number to relative seat number inside its coach (1 to seatsPerCoach)
            int relativeSeat = ((seatNum - 1) % seatsPerCoach) + 1;

            switch (quota.ToUpper())
            {
                case "SENIOR": // Lower berths (primarily first 8 seats)
                    return relativeSeat >= 1 && relativeSeat <= 8;
                case "LADIES": // Seats 9 to 16
                    return relativeSeat >= 9 && relativeSeat <= 16;
                case "TATKAL":
                case "PT":     // Seats 17 to 32 (AC/Non-AC Tatkal quota)
                    return relativeSeat >= 17 && relativeSeat <= 32;
                case "GENERAL":
                default:       // Seats 33 to capacity are General
                    return relativeSeat >= 33 && relativeSeat <= seatsPerCoach;
            }
        }

        // Helper to clean ClassCode (e.g. "AC 3 Tier (3A)" -> "3A")
        private static string CleanClassCode(string classCode)
        {
            if (string.IsNullOrWhiteSpace(classCode)) return "SL";

            classCode = classCode.ToUpper();
            if (classCode.Contains("(") && classCode.Contains(")"))
            {
                int start = classCode.IndexOf("(") + 1;
                int end = classCode.IndexOf(")");
                classCode = classCode.Substring(start, end - start);
            }
            return classCode;
        }
    }
}
