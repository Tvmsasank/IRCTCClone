using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using IRCTCClone.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IrctcClone.BackgroundServices
{
    public class ChartPreparationNotificationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChartPreparationNotificationService> _logger;
        private readonly string _connectionString;

        public ChartPreparationNotificationService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ChartPreparationNotificationService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ChartPreparationNotificationService background worker started.");

            // Run once immediately at startup, then every 60 seconds
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessUpcomingChartedBookingsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing chart preparation notifications.");
                }

                // Check every 60 seconds
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("ChartPreparationNotificationService stopped.");
        }

        public async Task ProcessUpcomingChartedBookingsAsync()
        {
            var eligibleBookingIds = new List<(int BookingId, string PNR, string UserId, DateTime DepDateTime)>();
            var indiaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // 1. Get all active bookings within today or upcoming 2 days that haven't been notified yet
                string query = @"
                    SELECT b.Id, b.PNR, b.UserId, b.JourneyDate, b.Departure
                    FROM Bookings b
                    WHERE (b.Status IN ('CONFIRMED', 'BOOKED') OR b.TicketStatus IN ('BOOKED', 'CNF', 'RAC', 'WL'))
                      AND b.JourneyDate >= CAST(DATEADD(day, -1, GETDATE()) AS DATE)
                      AND b.JourneyDate <= CAST(DATEADD(day, 2, GETDATE()) AS DATE)
                      AND NOT EXISTS (
                          SELECT 1 FROM ChartNotificationLogs cnl WHERE cnl.BookingId = b.Id
                      )";

                using (var cmd = new SqlCommand(query, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int bId = Convert.ToInt32(reader["Id"]);
                            string pnr = reader["PNR"]?.ToString() ?? "";
                            string uId = reader["UserId"]?.ToString() ?? "";
                            DateTime jDate = Convert.ToDateTime(reader["JourneyDate"]);
                            TimeSpan depTime = reader["Departure"] != DBNull.Value ? (TimeSpan)reader["Departure"] : TimeSpan.Zero;

                            DateTime depDateTime = jDate.Date + depTime;
                            TimeSpan timeToDeparture = depDateTime - indiaTime;

                            // 8-hour charting rule: timeToDeparture <= 8 hours and train not departed more than 4 hours ago
                            if (timeToDeparture.TotalHours <= 8 && timeToDeparture.TotalHours >= -4)
                            {
                                eligibleBookingIds.Add((bId, pnr, uId, depDateTime));
                            }
                        }
                    }
                }
            }

            if (eligibleBookingIds.Count == 0) return;

            _logger.LogInformation($"Found {eligibleBookingIds.Count} booking(s) eligible for chart preparation notification.");

            using var scope = _scopeFactory.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

            foreach (var (bookingId, pnr, userId, depDateTime) in eligibleBookingIds)
            {
                try
                {
                    bool sent = await emailService.SendChartPreparedEmailAsync(bookingId);
                    if (sent)
                    {
                        using var conn = new SqlConnection(_connectionString);
                        await conn.OpenAsync();
                        string insertLog = @"
                            IF NOT EXISTS (SELECT 1 FROM ChartNotificationLogs WHERE BookingId = @BookingId)
                            BEGIN
                                INSERT INTO ChartNotificationLogs (BookingId, PNR, RecipientEmail, SentAt)
                                VALUES (@BookingId, @PNR, @RecipientEmail, GETDATE())
                            END";
                        using var cmd = new SqlCommand(insertLog, conn);
                        cmd.Parameters.AddWithValue("@BookingId", bookingId);
                        cmd.Parameters.AddWithValue("@PNR", pnr);
                        cmd.Parameters.AddWithValue("@RecipientEmail", userId ?? "");
                        await cmd.ExecuteNonQueryAsync();

                        _logger.LogInformation($"Successfully dispatched Chart Preparation email for Booking #{bookingId}, PNR: {pnr} to {userId}.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send chart preparation notification for Booking #{bookingId}.");
                }
            }
        }
    }
}
