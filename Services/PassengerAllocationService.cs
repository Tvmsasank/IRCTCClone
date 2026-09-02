using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;

namespace IRCTCClone.Services
{
    public class PassengerAllocationService
    {
        public void AllocateAndInsertPassengers(
            SqlConnection conn,
            SqlTransaction transaction,
            int bookingId,
            int trainId,
            int classId,
            DateTime journeyDate,
            List<string> passengerNames,
            List<int> passengerAges,
            List<string> passengerGenders,
            List<string> passengerBerths,
            int seatsCapacity,
            int raccount,
            int racSeats,
            string quota)
        {
            if (passengerNames == null || passengerNames.Count == 0)
                throw new ArgumentException("No passengers provided.", nameof(passengerNames));

            int passengerCount = passengerNames.Count;
            journeyDate = journeyDate.Date;

            // 1. Fetch current CNF/RAC/WL counts
            int cnfCountCurrent = 0, racCountCurrent = 0, wlCount = 0;
            using (var cmd = new SqlCommand("spGetPassengerCounts", conn, transaction))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@TrainClassId", classId);
                cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        cnfCountCurrent = rdr["CNFCount"] != DBNull.Value ? Convert.ToInt32(rdr["CNFCount"]) : 0;
                        racCountCurrent = rdr["RACCount"] != DBNull.Value ? Convert.ToInt32(rdr["RACCount"]) : 0;
                        wlCount = rdr["WLCount"] != DBNull.Value ? Convert.ToInt32(rdr["WLCount"]) : 0;
                    }
                }
            }

            // 2. Fetch all currently occupied seats on this journey date
            var occupiedSeatStrings = new HashSet<string>(); // stores values like "S1-14"
            using (var cmd = new SqlCommand("spGetConfirmedSeats", conn, transaction))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);
                cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var seatStr = rdr["SeatNumber"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(seatStr))
                        {
                            occupiedSeatStrings.Add(seatStr.ToUpper().Trim());
                        }
                    }
                }
            }

            // 3. Load all dynamically configured seats from the DB for this TrainClass
            var allDbSeats = new List<DbSeatDetail>();
            string sqlQuery = @"
                SELECT c.CoachName, cs.SeatNumber, cs.BerthType, cs.QuotaType
                FROM Coaches c
                JOIN CoachSeats cs ON c.Id = cs.CoachId
                WHERE c.TrainId = @TrainId AND c.ClassId = @ClassId
                ORDER BY c.CoachName, cs.SeatNumber";

            using (var cmd = new SqlCommand(sqlQuery, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        allDbSeats.Add(new DbSeatDetail
                        {
                            CoachName = rdr["CoachName"].ToString(),
                            SeatNumber = Convert.ToInt32(rdr["SeatNumber"]),
                            BerthType = rdr["BerthType"].ToString(),
                            QuotaType = rdr["QuotaType"].ToString()
                        });
                    }
                }
            }

            // 4. Passenger Allocation Loop
            for (int i = 0; i < passengerCount; i++)
            {
                string name = passengerNames[i];
                int age = (passengerAges != null && passengerAges.Count > i) ? passengerAges[i] : 0;
                string gender = (passengerGenders != null && passengerGenders.Count > i) ? passengerGenders[i] : null;
                string requestedBerth = (passengerBerths != null && passengerBerths.Count > i && !string.IsNullOrWhiteSpace(passengerBerths[i]))
                                        ? passengerBerths[i].ToUpper().Trim() : "NONE";

                string bookingStatus = null;
                int? position = null;
                string seatNumber = null;
                string berthAssigned = null;

                if (cnfCountCurrent < seatsCapacity && allDbSeats.Count > 0)
                {
                    DbSeatDetail allocatedSeat = null;

                    // Match Quotas
                    string targetQuota = string.IsNullOrWhiteSpace(quota) ? "GENERAL" : quota.ToUpper().Trim();
                    if (targetQuota == "PT") targetQuota = "PREMIUM TATKAL"; // Premium Tatkal has its own physical seats

                    // Step A: Scan for requested berth type inside the user's Quota range
                    allocatedSeat = allDbSeats.Find(s =>
                        !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}".ToUpper()) &&
                        s.QuotaType.ToUpper() == targetQuota &&
                        (requestedBerth == "NONE" || s.BerthType.ToUpper() == requestedBerth)
                    );

                    // Step B: Fall back to ANY seat inside user's Quota range
                    if (allocatedSeat == null)
                    {
                        allocatedSeat = allDbSeats.Find(s =>
                            !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}".ToUpper()) &&
                            s.QuotaType.ToUpper() == targetQuota
                        );
                    }

                    // Step C: If Quota is full, fall back to ANY seat inside GENERAL quota
                    if (allocatedSeat == null)
                    {
                        allocatedSeat = allDbSeats.Find(s =>
                            !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}".ToUpper()) &&
                            s.QuotaType.ToUpper() == "GENERAL"
                        );
                    }

                    // Step D: Ultimate fallback: Assign ANY open seat on this train class
                    if (allocatedSeat == null)
                    {
                        allocatedSeat = allDbSeats.Find(s =>
                            !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}".ToUpper())
                        );
                    }

                    if (allocatedSeat != null)
                    {
                        bookingStatus = "CNF";
                        cnfCountCurrent++;

                        seatNumber = $"{allocatedSeat.CoachName}-{allocatedSeat.SeatNumber}";
                        berthAssigned = allocatedSeat.BerthType;

                        // Mark this seat as occupied for subsequent passengers on this same ticket
                        occupiedSeatStrings.Add(seatNumber.ToUpper());
                    }
                }

                // Step E: If cnfCountCurrent < seatsCapacity but allDbSeats was empty or not populated, dynamically generate CNF seat
                if (bookingStatus == null && cnfCountCurrent < seatsCapacity)
                {
                    int nextSeat = cnfCountCurrent + 1;
                    while (occupiedSeatStrings.Contains($"S1-{nextSeat}") || occupiedSeatStrings.Contains($"B1-{nextSeat}") || occupiedSeatStrings.Contains($"{nextSeat}"))
                    {
                        nextSeat++;
                    }

                    int coachNum = ((nextSeat - 1) / 72) + 1;
                    int seatInCoach = ((nextSeat - 1) % 72) + 1;
                    if (seatInCoach == 0) seatInCoach = 1;

                    // Standard 8-berth layout: Lower, Middle, Upper, Lower, Middle, Upper, Side Lower, Side Upper
                    string[] berthTypes = { "Lower", "Middle", "Upper", "Lower", "Middle", "Upper", "Side Lower", "Side Upper" };
                    int berthMod = (seatInCoach - 1) % 8;
                    string defaultBerth = berthTypes[berthMod];

                    seatNumber = $"S{coachNum}-{seatInCoach}";
                    berthAssigned = defaultBerth;
                    bookingStatus = "CNF";
                    cnfCountCurrent++;
                    occupiedSeatStrings.Add(seatNumber.ToUpper());
                }

                // If CNF could not be allocated (capacity truly full), assign RAC or Waitlist (WL)
                if (bookingStatus == null)
                {
                    if (quota == "TATKAL" || quota == "PT")
                    {
                        bookingStatus = "WL";
                        wlCount++;
                        position = wlCount;
                    }
                    else
                    {
                        if (racSeats >= 1 && racCountCurrent < racSeats)
                        {
                            bookingStatus = "RAC";
                            racCountCurrent++;
                            position = racCountCurrent;
                        }
                        else
                        {
                            wlCount++;
                            bookingStatus = "WL";
                            position = wlCount;
                        }
                    }
                }

                // Save Passenger
                using (var cmd = new SqlCommand("InsertPassenger", conn, transaction))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@Age", age);
                    cmd.Parameters.AddWithValue("@Gender", gender);
                    cmd.Parameters.AddWithValue("@SeatNumber", (object)seatNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Berth", (object)berthAssigned ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@BookingStatus", bookingStatus);
                    cmd.Parameters.AddWithValue("@CurrentStatus", bookingStatus);
                    cmd.Parameters.AddWithValue("@Position", (object)position ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Helper to parse absolute index (1 to capacity) from hyphenated ("S2-14") or legacy ("14") database seat codes
        private int ParseAbsoluteSeat(string seatStr, string seatPrefix, int seatsPerCoach, int seatsCapacity)
        {
            if (string.IsNullOrWhiteSpace(seatStr)) return 0;

            if (seatStr.Contains("-"))
            {
                var parts = seatStr.Split('-');
                string coachPart = parts[0];
                string seatPart = parts[1];

                // Extract coach numeric digit (e.g. S2 -> 2)
                string coachDigits = new string(coachPart.Where(char.IsDigit).ToArray());
                int coachNum = 1;
                if (!string.IsNullOrEmpty(coachDigits))
                {
                    int.TryParse(coachDigits, out coachNum);
                }

                // Extract seat inside coach (e.g. 45)
                int.TryParse(seatPart, out int coachSeat);

                return (seatsPerCoach * (coachNum - 1)) + coachSeat;
            }
            else
            {
                // Fallback support for legacy database integer seeds
                string digits = new string(seatStr.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int legacySeat) && legacySeat >= 1 && legacySeat <= seatsCapacity)
                {
                    return legacySeat;
                }
            }
            return 0;
        }

        public void PromoteQueuesOnSeatFreed(
                    SqlConnection conn,
                    SqlTransaction transaction,
                    int trainId,
                    int classId,
                    DateTime journeyDate,
                    string freedSeatPrefix,
                    int freedSeatNumeric,
                    int seatsFreed)
        {
            for (int s = 0; s < seatsFreed; s++)
            {
                int racPassengerId = 0;

                // 1) Get next RAC passenger
                using (var cmd = new SqlCommand("sp_GetNextRACPassenger", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            racPassengerId = r.GetInt32(0);
                        }
                    }
                }

                // No RAC → nothing to promote
                if (racPassengerId == 0)
                    break;

                // Assign seat
                string seatNumber = $"{freedSeatPrefix}{freedSeatNumeric}";
                string berth = "LB"; // still static but you can change later

                // 2) Promote RAC → CNF
                using (var cmd = new SqlCommand("sp_PromoteRACToCNF", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PassengerId", racPassengerId);
                    cmd.Parameters.AddWithValue("@SeatNumber", seatNumber);
                    cmd.Parameters.AddWithValue("@Berth", berth);
                    cmd.ExecuteNonQuery();
                }

                // 3) Get next WL passenger
                int wlPassengerId = 0;
                using (var cmd = new SqlCommand("sp_GetNextWLPassenger", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            wlPassengerId = r.GetInt32(0);
                        }
                    }
                }

                // No WL → done
                if (wlPassengerId == 0)
                {
                    freedSeatNumeric++;
                    continue;
                }

                // 4) Get next RAC position
                int nextRACPos = 0;
                using (var cmd = new SqlCommand("sp_GetNextRACPosition", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@JourneyDate", journeyDate.Date);

                    nextRACPos = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 5) Promote WL → RAC
                using (var cmd = new SqlCommand("sp_PromoteWLToRAC", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@PassengerId", wlPassengerId);
                    cmd.Parameters.AddWithValue("@NewPosition", nextRACPos);
                    cmd.ExecuteNonQuery();
                }

                // Next seat number
                freedSeatNumeric++;
            }
        }

        // Helper class to store temporary seat layout loaded from DB
        private class DbSeatDetail
        {
            public string CoachName { get; set; }
            public int SeatNumber { get; set; }
            public string BerthType { get; set; }
            public string QuotaType { get; set; }
        }
    }
}