using IRCTCClone.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace IRCTCClone.Services
{
    public class BookingService : IBookingService
    {
        private readonly string _connectionString;

        public BookingService(IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("DefaultConnection");
        }

        public SeatStatus GetSeatStatus(
            int trainId,
            int classId,
            DateTime journeyDate,
            string quota)
        {
            if (journeyDate == DateTime.MinValue)
            {
                return null;
            }

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("spGetSeatStatusCounts", conn))
            {
                conn.Open();

                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@ClassId", classId);
                cmd.Parameters.AddWithValue("@JourneyDate", journeyDate);
                cmd.Parameters.AddWithValue("@Quota", quota);

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        return new SeatStatus
                        {
                            SeatsAvailable =
                                Convert.ToInt32(rdr["SeatsAvailable"]),

                            RACSeats =
                                Convert.ToInt32(rdr["RACSeats"]),

                            ConfirmedCount =
                                Convert.ToInt32(rdr["ConfirmedCount"]),

                            RACCount =
                                Convert.ToInt32(rdr["RACCount"]),

                            WLCount =
                                Convert.ToInt32(rdr["WLCount"]),

                            TatkalSeats =
                                Convert.ToInt32(rdr["TatkalSeats"])
                        };
                    }
                }
            }

            return null;
        }

        public int GetBookedSeatsCount(
            int trainId,
            int classId)
        {
            int count = 0;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new SqlCommand("GetBookedSeatsCount", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue("@TrainId", trainId);

                    cmd.Parameters.AddWithValue("@ClassId", classId);

                    object result = cmd.ExecuteScalar();

                    count = result != DBNull.Value
                        ? Convert.ToInt32(result)
                        : 0;
                }
            }

            return count;
        }

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

        public Station GetStationById(
            int stationId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                using (var cmd =
                       new SqlCommand("GetStationById", conn))
                {
                    cmd.CommandType =
                        CommandType.StoredProcedure;

                    cmd.Parameters.AddWithValue(
                        "@StationId",
                        stationId);

                    using (var reader =
                           cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Station
                            {
                                Id = reader["Id"] != DBNull.Value
                                    ? Convert.ToInt32(reader["Id"])
                                    : 0,

                                Code = reader["Code"]?.ToString(),

                                Name = reader["Name"]?.ToString()
                            };
                        }
                    }
                }
            }

            return null;
        }
    }
}