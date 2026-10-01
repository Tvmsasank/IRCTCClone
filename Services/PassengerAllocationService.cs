using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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
            string quota,
            string preferredCoach = null)
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

            // 2. Fetch all currently occupied seats on this journey date (both via SP and direct Bookings query)
            var occupiedSeatStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
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
            }
            catch { }

            // Complement with direct lookup to guarantee zero double bookings
            using (var cmd = new SqlCommand(@"
                SELECT p.SeatNumber
                FROM Passengers p
                JOIN Bookings b ON p.BookingId = b.Id
                WHERE b.TrainId = @TrainId 
                  AND CAST(b.JourneyDate AS DATE) = @JourneyDate 
                  AND p.SeatNumber IS NOT NULL 
                  AND (p.CurrentStatus = 'CNF' OR p.BookingStatus = 'CNF')", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TrainId", trainId);
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

            // 3. Retrieve Class details (Code, SeatPrefix, TotalSeats)
            string classCode = "SL";
            string seatPrefix = "S";
            int classTotalSeats = seatsCapacity;

            using (var cmd = new SqlCommand("SELECT Code, SeatPrefix, TotalSeats FROM TrainClasses WHERE Id = @ClassId", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@ClassId", classId);
                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        classCode = rdr["Code"]?.ToString() ?? "SL";
                        seatPrefix = rdr["SeatPrefix"]?.ToString() ?? "S";
                        if (rdr["TotalSeats"] != DBNull.Value)
                        {
                            classTotalSeats = Convert.ToInt32(rdr["TotalSeats"]);
                        }
                    }
                }
            }

            // Standardize prefix based on class if prefix is missing
            string cleanCode = CleanClassCode(classCode);
            if (string.IsNullOrWhiteSpace(seatPrefix) || seatPrefix.Length > 2)
            {
                seatPrefix = GetDefaultSeatPrefix(cleanCode);
            }

            int seatsPerCoach = GetSeatsPerCoachByClass(cleanCode);

            // 4. Retrieve full Train Rake Coach Positions from Trains table
            string coachPositionsStr = null;
            using (var cmd = new SqlCommand("SELECT CoachPositions FROM Trains WHERE Id = @TrainId", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                var obj = cmd.ExecuteScalar();
                if (obj != null && obj != DBNull.Value)
                {
                    coachPositionsStr = obj.ToString();
                }
            }

            // 5. Load or Auto-Generate Coaches for this Train & Class
            var coachList = new List<DbCoachInfo>();
            string prefixPattern = seatPrefix + "%";

            using (var cmd = new SqlCommand(@"
                SELECT c.Id, c.CoachName, c.SeatsCount
                FROM Coaches c
                WHERE c.TrainId = @TrainId 
                  AND (c.ClassId = @ClassId OR c.CoachName LIKE @PrefixPattern)
                ORDER BY c.Id", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);
                cmd.Parameters.AddWithValue("@PrefixPattern", prefixPattern);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        coachList.Add(new DbCoachInfo
                        {
                            CoachId = Convert.ToInt32(rdr["Id"]),
                            CoachName = rdr["CoachName"].ToString().Trim().ToUpper(),
                            SeatsCount = Convert.ToInt32(rdr["SeatsCount"])
                        });
                    }
                }
            }

            // If no coaches exist in DB for this train/class, auto-generate them now!
            if (coachList.Count == 0)
            {
                coachList = AutoGenerateCoachesAndSeats(conn, transaction, trainId, classId, cleanCode, seatPrefix, seatsPerCoach, classTotalSeats, coachPositionsStr);
            }

            // 6. ORDER COACHES FROM THE MIDDLE OF THE TRAIN OUTWARD
            var orderedCoaches = CalculateMiddleOutCoachOrder(coachList, coachPositionsStr, seatPrefix);

            // If user specified Preferred Coach (e.g. S9), move preferred coach to the FRONT of the queue
            if (!string.IsNullOrWhiteSpace(preferredCoach))
            {
                string normPref = preferredCoach.Trim().ToUpper();
                var matchedPrefCoach = orderedCoaches.FirstOrDefault(c => c.CoachName == normPref);
                if (matchedPrefCoach != null)
                {
                    orderedCoaches.Remove(matchedPrefCoach);
                    orderedCoaches.Insert(0, matchedPrefCoach);
                }
            }

            // 7. Load all CoachSeats for these coaches from DB
            var allDbSeats = new List<DbSeatDetail>();
            using (var cmd = new SqlCommand(@"
                SELECT c.Id AS CoachId, c.CoachName, cs.SeatNumber, cs.BerthType, cs.QuotaType
                FROM Coaches c
                JOIN CoachSeats cs ON c.Id = cs.CoachId
                WHERE c.TrainId = @TrainId 
                  AND (c.ClassId = @ClassId OR c.CoachName LIKE @PrefixPattern)
                ORDER BY cs.SeatNumber", conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);
                cmd.Parameters.AddWithValue("@PrefixPattern", prefixPattern);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        allDbSeats.Add(new DbSeatDetail
                        {
                            CoachId = Convert.ToInt32(rdr["CoachId"]),
                            CoachName = rdr["CoachName"].ToString().Trim().ToUpper(),
                            SeatNumber = Convert.ToInt32(rdr["SeatNumber"]),
                            BerthType = rdr["BerthType"].ToString().Trim().ToUpper(),
                            QuotaType = rdr["QuotaType"].ToString().Trim().ToUpper()
                        });
                    }
                }
            }

            // Fallback: If DB CoachSeats were still empty, populate virtual seats list
            if (allDbSeats.Count == 0)
            {
                foreach (var coach in orderedCoaches)
                {
                    for (int s = 1; s <= coach.SeatsCount; s++)
                    {
                        allDbSeats.Add(new DbSeatDetail
                        {
                            CoachId = coach.CoachId,
                            CoachName = coach.CoachName,
                            SeatNumber = s,
                            BerthType = CalculateBerthType(cleanCode, s),
                            QuotaType = CalculateQuotaType(s, coach.SeatsCount)
                        });
                    }
                }
            }

            // Normalize target booking quota
            string targetQuota = NormalizeQuota(quota);

            // Group available seats by CoachName for efficient middle-out lookup
            var seatsByCoach = allDbSeats.GroupBy(s => s.CoachName, StringComparer.OrdinalIgnoreCase)
                                         .ToDictionary(g => g.Key.ToUpper(), g => g.ToList());

            // 8. ALLOCATE PASSENGERS
            for (int i = 0; i < passengerCount; i++)
            {
                string name = passengerNames[i];
                int age = (passengerAges != null && passengerAges.Count > i) ? passengerAges[i] : 0;
                string gender = (passengerGenders != null && passengerGenders.Count > i) ? passengerGenders[i] : null;
                string rawBerth = (passengerBerths != null && passengerBerths.Count > i) ? passengerBerths[i] : null;

                // Demographics & Berth Preference Normalization
                string requestedBerth = NormalizeRequestedBerth(rawBerth);

                // Senior Citizen automatic Lower Berth priority (Age >= 60 for male/any, Age >= 45 for female)
                bool isSenior = age >= 60 || (gender != null && gender.Trim().ToUpper().StartsWith("F") && age >= 45);
                if (isSenior && requestedBerth == "NONE")
                {
                    requestedBerth = "LB";
                }

                string bookingStatus = null;
                int? position = null;
                string seatNumber = null;
                string berthAssigned = null;

                // Attempt CNF Allocation if train capacity permits
                if (cnfCountCurrent < seatsCapacity)
                {
                    DbSeatDetail allocatedSeat = null;

                    // -------------------------------------------------------------
                    // STRATEGY 1: Check in middle-out coach order (Preferred coach is #1)
                    // Match Exact Quota + User's Requested Berth
                    // -------------------------------------------------------------
                    if (requestedBerth != "NONE")
                    {
                        foreach (var coach in orderedCoaches)
                        {
                            if (!seatsByCoach.TryGetValue(coach.CoachName, out var coachSeats)) continue;

                            allocatedSeat = coachSeats.FirstOrDefault(s =>
                                !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}") &&
                                s.QuotaType == targetQuota &&
                                s.BerthType == requestedBerth
                            );

                            if (allocatedSeat != null) break;
                        }
                    }

                    // -------------------------------------------------------------
                    // STRATEGY 2: If requested berth not found in target quota,
                    // or user chose "No Preference" -> Pick ANY open seat in target quota
                    // filling the middle coach first, then next coaches outward
                    // -------------------------------------------------------------
                    if (allocatedSeat == null)
                    {
                        foreach (var coach in orderedCoaches)
                        {
                            if (!seatsByCoach.TryGetValue(coach.CoachName, out var coachSeats)) continue;

                            allocatedSeat = coachSeats.FirstOrDefault(s =>
                                !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}") &&
                                s.QuotaType == targetQuota
                            );

                            if (allocatedSeat != null) break;
                        }
                    }

                    // -------------------------------------------------------------
                    // STRATEGY 3: If Target Quota is full in all coaches,
                    // and booking is GENERAL, fallback to any available General seat
                    // -------------------------------------------------------------
                    if (allocatedSeat == null && targetQuota == "GENERAL")
                    {
                        foreach (var coach in orderedCoaches)
                        {
                            if (!seatsByCoach.TryGetValue(coach.CoachName, out var coachSeats)) continue;

                            allocatedSeat = coachSeats.FirstOrDefault(s =>
                                !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}") &&
                                s.QuotaType == "GENERAL"
                            );

                            if (allocatedSeat != null) break;
                        }
                    }

                    // -------------------------------------------------------------
                    // STRATEGY 4: If still unallocated but capacity is confirmed
                    // Pick ANY unreserved seat in the middle-out coaches
                    // -------------------------------------------------------------
                    if (allocatedSeat == null && targetQuota == "GENERAL")
                    {
                        foreach (var coach in orderedCoaches)
                        {
                            if (!seatsByCoach.TryGetValue(coach.CoachName, out var coachSeats)) continue;

                            allocatedSeat = coachSeats.FirstOrDefault(s =>
                                !occupiedSeatStrings.Contains($"{s.CoachName}-{s.SeatNumber}")
                            );

                            if (allocatedSeat != null) break;
                        }
                    }

                    // Record allocation
                    if (allocatedSeat != null)
                    {
                        bookingStatus = "CNF";
                        cnfCountCurrent++;
                        seatNumber = $"{allocatedSeat.CoachName}-{allocatedSeat.SeatNumber}";
                        berthAssigned = allocatedSeat.BerthType;

                        // Mark as occupied for following passengers on this same ticket
                        occupiedSeatStrings.Add(seatNumber.ToUpper());
                    }
                }

                // -------------------------------------------------------------
                // RAC / WAITLIST (WL) ALLOCATION (When CNF capacity is exhausted)
                // -------------------------------------------------------------
                if (bookingStatus == null)
                {
                    if (targetQuota == "TATKAL" || targetQuota == "PREMIUM TATKAL")
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

                // Save Passenger into DB
                using (var cmd = new SqlCommand("InsertPassenger", conn, transaction))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@Age", age);
                    cmd.Parameters.AddWithValue("@Gender", (object)gender ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SeatNumber", (object)seatNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Berth", (object)berthAssigned ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@BookingStatus", bookingStatus);
                    cmd.Parameters.AddWithValue("@CurrentStatus", bookingStatus);
                    cmd.Parameters.AddWithValue("@Position", (object)position ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // =========================================================================
        // HELPER: CALCULATE MIDDLE-OUT COACH SEQUENCE
        // =========================================================================
        private List<DbCoachInfo> CalculateMiddleOutCoachOrder(
            List<DbCoachInfo> coaches,
            string coachPositionsStr,
            string seatPrefix)
        {
            if (coaches == null || coaches.Count == 0) return new List<DbCoachInfo>();

            // Case A: Train has configured CoachPositions lineup (e.g. ENG,GEN,S1,S2,S3,S4,S5,S6,S7,B1,B2,B3,A1,SLR)
            if (!string.IsNullOrWhiteSpace(coachPositionsStr))
            {
                var rake = coachPositionsStr.Split(',')
                                            .Select(c => c.Trim().ToUpper())
                                            .Where(c => !string.IsNullOrEmpty(c))
                                            .ToList();

                if (rake.Count > 0)
                {
                    double trainCenter = (rake.Count - 1) / 2.0;

                    foreach (var c in coaches)
                    {
                        int rakeIdx = rake.IndexOf(c.CoachName);
                        if (rakeIdx >= 0)
                        {
                            c.OrderIndex = rakeIdx;
                            c.DistanceFromCenter = Math.Abs(rakeIdx - trainCenter);
                        }
                        else
                        {
                            // If coach not explicitly in rake string, calculate based on numeric suffix
                            int num = ExtractCoachNumber(c.CoachName);
                            c.OrderIndex = 50 + num;
                            c.DistanceFromCenter = 100 + num;
                        }
                    }

                    // Sort: Closest to center first. For equal distance, pick earlier coach
                    return coaches.OrderBy(c => c.DistanceFromCenter)
                                  .ThenBy(c => c.OrderIndex)
                                  .ToList();
                }
            }

            // Case B: No CoachPositions given for this train
            // Order the coaches of this class naturally (e.g. S1..S9), find the middle coach, and radiate outward!
            var sortedNatural = coaches.OrderBy(c => ExtractCoachNumber(c.CoachName)).ToList();
            double classCenter = (sortedNatural.Count - 1) / 2.0;

            for (int i = 0; i < sortedNatural.Count; i++)
            {
                sortedNatural[i].OrderIndex = i;
                sortedNatural[i].DistanceFromCenter = Math.Abs(i - classCenter);
            }

            return sortedNatural.OrderBy(c => c.DistanceFromCenter)
                                .ThenBy(c => c.OrderIndex)
                                .ToList();
        }

        // =========================================================================
        // HELPER: AUTO-GENERATE COACHES & SEATS FOR UNCONFIGURED TRAINS
        // =========================================================================
        private List<DbCoachInfo> AutoGenerateCoachesAndSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int trainId,
            int classId,
            string cleanCode,
            string seatPrefix,
            int seatsPerCoach,
            int totalSeats,
            string coachPositionsStr)
        {
            var createdCoaches = new List<DbCoachInfo>();
            var coachNamesToCreate = new List<string>();

            // Check if coach positions string has coaches matching this prefix
            if (!string.IsNullOrWhiteSpace(coachPositionsStr))
            {
                var rake = coachPositionsStr.Split(',')
                                            .Select(c => c.Trim().ToUpper())
                                            .Where(c => !string.IsNullOrEmpty(c) && c.StartsWith(seatPrefix))
                                            .ToList();

                if (rake.Count > 0)
                {
                    coachNamesToCreate = rake;
                }
            }

            // Fallback: Compute coach count from capacity
            if (coachNamesToCreate.Count == 0)
            {
                int count = Math.Max(1, (int)Math.Ceiling((double)totalSeats / seatsPerCoach));
                for (int c = 1; c <= count; c++)
                {
                    coachNamesToCreate.Add($"{seatPrefix}{c}");
                }
            }

            // Insert each coach and its seats into the DB
            foreach (var coachName in coachNamesToCreate)
            {
                int coachId = 0;
                using (var cmd = new SqlCommand("spInsertCoach", conn, transaction))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@TrainId", trainId);
                    cmd.Parameters.AddWithValue("@ClassId", classId);
                    cmd.Parameters.AddWithValue("@CoachName", coachName);
                    cmd.Parameters.AddWithValue("@SeatsCount", seatsPerCoach);
                    coachId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                createdCoaches.Add(new DbCoachInfo
                {
                    CoachId = coachId,
                    CoachName = coachName,
                    SeatsCount = seatsPerCoach
                });

                // Generate seat layouts with quota breakdown
                for (int s = 1; s <= seatsPerCoach; s++)
                {
                    string berth = CalculateBerthType(cleanCode, s);
                    string quotaType = CalculateQuotaType(s, seatsPerCoach);

                    using (var cmd = new SqlCommand("spInsertCoachSeat", conn, transaction))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@CoachId", coachId);
                        cmd.Parameters.AddWithValue("@SeatNumber", s);
                        cmd.Parameters.AddWithValue("@BerthType", berth);
                        cmd.Parameters.AddWithValue("@QuotaType", quotaType);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return createdCoaches;
        }

        // =========================================================================
        // HELPER: BERTH & QUOTA TYPE CALCULATIONS
        // =========================================================================
        private string CalculateBerthType(string cleanCode, int seatNum)
        {
            if (cleanCode.Contains("SL") || cleanCode.Contains("3A") || cleanCode.Contains("3E"))
            {
                int rem = seatNum % 8;
                if (rem == 1 || rem == 4) return "LB"; // Lower
                if (rem == 2 || rem == 5) return "MB"; // Middle
                if (rem == 3 || rem == 6) return "UB"; // Upper
                if (rem == 7) return "SL";              // Side Lower
                return "SU";                              // Side Upper (rem == 0)
            }
            else if (cleanCode.Contains("2A") || cleanCode.Contains("1A"))
            {
                int rem = seatNum % 6;
                if (rem == 1 || rem == 3) return "LB";
                if (rem == 2 || rem == 4) return "UB";
                if (rem == 5) return "SL";
                return "SU";
            }
            else // Chair Car / 2S
            {
                int rem = seatNum % 5;
                if (rem == 1 || rem == 5) return "W"; // Window
                if (rem == 2 || rem == 4) return "A"; // Aisle
                return "M";                          // Middle
            }
        }

        private string CalculateQuotaType(int seatNum, int seatsPerCoach)
        {
            if (seatsPerCoach == 80 || seatsPerCoach == 78)
            {
                if (seatNum >= 1 && seatNum <= 8) return "SENIOR";
                if (seatNum >= 9 && seatNum <= 16) return "LADIES";
                if (seatNum >= 17 && seatNum <= 24) return "TATKAL";
                if (seatNum >= 25 && seatNum <= 32) return "PREMIUM TATKAL";
                return "GENERAL";
            }
            else if (seatsPerCoach == 48)
            {
                if (seatNum >= 1 && seatNum <= 4) return "SENIOR";
                if (seatNum >= 5 && seatNum <= 8) return "LADIES";
                if (seatNum >= 9 && seatNum <= 16) return "TATKAL";
                if (seatNum >= 17 && seatNum <= 20) return "PREMIUM TATKAL";
                return "GENERAL";
            }
            else if (seatsPerCoach == 24)
            {
                if (seatNum >= 1 && seatNum <= 2) return "SENIOR";
                if (seatNum >= 3 && seatNum <= 4) return "LADIES";
                if (seatNum >= 5 && seatNum <= 8) return "TATKAL";
                if (seatNum >= 9 && seatNum <= 12) return "PREMIUM TATKAL";
                return "GENERAL";
            }
            else
            {
                if (seatNum >= 1 && seatNum <= 6) return "SENIOR";
                if (seatNum >= 7 && seatNum <= 12) return "LADIES";
                if (seatNum >= 13 && seatNum <= 24) return "TATKAL";
                if (seatNum >= 25 && seatNum <= 32) return "PREMIUM TATKAL";
                return "GENERAL";
            }
        }

        private string NormalizeQuota(string quota)
        {
            if (string.IsNullOrWhiteSpace(quota)) return "GENERAL";
            string q = quota.Trim().ToUpper();
            if (q == "TATKAL" || q == "TQ") return "TATKAL";
            if (q == "PREMIUM TATKAL" || q == "PT") return "PREMIUM TATKAL";
            if (q == "LADIES" || q == "LD") return "LADIES";
            if (q == "SENIOR" || q == "SS" || q.Contains("LOWER") || q.Contains("SENIOR")) return "SENIOR";
            return "GENERAL";
        }

        private string NormalizeRequestedBerth(string rawBerth)
        {
            if (string.IsNullOrWhiteSpace(rawBerth)) return "NONE";
            string b = rawBerth.Trim().ToUpper();
            if (b.Contains("SIDE LOWER") || b == "SL") return "SL";
            if (b.Contains("SIDE UPPER") || b == "SU") return "SU";
            if (b.Contains("LOWER") || b == "LB") return "LB";
            if (b.Contains("MIDDLE") || b == "MB") return "MB";
            if (b.Contains("UPPER") || b == "UB") return "UB";
            if (b.Contains("WINDOW") || b == "WS" || b == "W") return "W";
            if (b.Contains("AISLE") || b == "A") return "A";
            return "NONE";
        }

        private string CleanClassCode(string classCode)
        {
            if (string.IsNullOrWhiteSpace(classCode)) return "SL";
            classCode = classCode.ToUpper();
            if (classCode.Contains("(") && classCode.Contains(")"))
            {
                int start = classCode.IndexOf("(") + 1;
                int end = classCode.IndexOf(")");
                return classCode.Substring(start, end - start).Trim();
            }
            return classCode.Trim();
        }

        private string GetDefaultSeatPrefix(string cleanCode)
        {
            switch (cleanCode)
            {
                case "1A": return "H";
                case "2A": return "A";
                case "3A": return "B";
                case "3E": return "M";
                case "CC": return "C";
                case "2S": return "D";
                case "SL":
                default: return "S";
            }
        }

        private int GetSeatsPerCoachByClass(string cleanCode)
        {
            switch (cleanCode)
            {
                case "1A": return 24;
                case "2A": return 48;
                case "CC": return 78;
                case "2S": return 108;
                case "SL":
                case "3A":
                case "3E":
                default: return 80;
            }
        }

        private int ExtractCoachNumber(string coachName)
        {
            if (string.IsNullOrWhiteSpace(coachName)) return 0;
            string digits = new string(coachName.Where(char.IsDigit).ToArray());
            int.TryParse(digits, out int num);
            return num;
        }

        // =========================================================================
        // QUEUE PROMOTION (PRESERVED)
        // =========================================================================
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
                string berth = "LB";

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

                freedSeatNumeric++;
            }
        }

        // =========================================================================
        // DATA STRUCTURES
        // =========================================================================
        public class DbCoachInfo
        {
            public int CoachId { get; set; }
            public string CoachName { get; set; }
            public int SeatsCount { get; set; }
            public int OrderIndex { get; set; }
            public double DistanceFromCenter { get; set; }
        }

        public class DbSeatDetail
        {
            public int CoachId { get; set; }
            public string CoachName { get; set; }
            public int SeatNumber { get; set; }
            public string BerthType { get; set; }
            public string QuotaType { get; set; }
        }
    }
}