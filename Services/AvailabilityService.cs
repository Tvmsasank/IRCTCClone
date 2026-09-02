using Microsoft.Extensions.Caching.Memory;
using System.Data;
using Microsoft.Data.SqlClient;
using IRCTCClone.Models;

namespace IRCTCClone.Services
{
    public class AvailabilityService : IAvailabilityService
    {
        private readonly string _connectionString;
        private readonly IMemoryCache _cache;

        public AvailabilityService(IConfiguration config, IMemoryCache cache)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
            _cache = cache;
        }

        public async Task<List<AvailabilityDto>> GetNext7DaysAsync(int trainId, string travelClass)
        {
            string cacheKey = $"avail:{trainId}:{travelClass}";
            if (_cache.TryGetValue(cacheKey, out List<AvailabilityDto> cached))
                return cached;

            var list = new List<AvailabilityDto>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand("GetNext7DaysAvailability", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@TrainId", trainId);
                cmd.Parameters.AddWithValue("@Class", travelClass);

                await conn.OpenAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var date = reader.GetDateTime(reader.GetOrdinal("TravelDate"));
                        var avail = reader.GetInt32(reader.GetOrdinal("AvailableSeats"));
                        var fare = reader.GetDecimal(reader.GetOrdinal("CalculatedFare"));

                        list.Add(new AvailabilityDto
                        {
                            Date = date,
                            AvailableSeats = avail,
                            Fare = fare,
                            IsWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday,
                            IsTatkalWindow = false
                        });
                    }
                }
            }

            _cache.Set(cacheKey, list, TimeSpan.FromSeconds(30));
            return list;
        }
    }
}
