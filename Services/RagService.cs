using Azure;
using IRCTCClone.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace IrctcClone.Services
{
    public class RagService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly HttpClient _http;

        public RagService(IWebHostEnvironment env, IConfiguration config, HttpClient http)
        {
            _env = env;
            _config = config;
            _http = http;
        }

        public string LoadRefundPolicy()
        {
            var filepath = Path.Combine(
                _env.ContentRootPath,
                "RagData",
                "refund-policy.txt"
            );

            return System.IO.File.ReadAllText(filepath);
        }

        public async Task<string> GetRefundExplanation(Booking booking)
        {
            var refundSummary = CalculateRefund(booking);

            // 🔴 CASE 1: NOT CANCELLED
            if (booking.TicketStatus != "CANCELLED")
            {
                return "This ticket is not cancelled yet. Refund will depend on when you cancel the ticket as per IRCTC rules.";
            }

            try
            {
                // 🟢 CASE 2: CANCELLED → go to AI
                var policy = LoadRefundPolicy();

                var prompt = $@"
                You are an IRCTC Assistant.

                Refund Rules:
                {policy}

                User Ticket Details:
                - Status: {booking.Status}
                - Fare: {booking.BaseFare}
                - Journey Date: {booking.JourneyDate}
                - Cancelled At: {booking.CancelledAt?.ToString() ?? "Not Available"}

                Question: 
                Explain why this refund amount was give.

                Rules: 
                - Answer clearly
                - Use only given rules
                - If unsure, say 'Not enough information'
                ";

                var apiKey = _config["Gemini:ApiKey"];

                var requestBody = new
                {
                    contents = new[]
                    {
                    new
                    {
                        parts = new[]
                        {
                            new {text = prompt}
                        }
                    }
                }
                };

                var response = await _http.PostAsync(
                    $"https://generativelanguage.googleapis.com/v1/models/gemini-2.0-flash-lite:generateContent?key={apiKey}",
                    new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                );

                var result = await response.Content.ReadAsStringAsync();

                Console.WriteLine(result);

                // ❌ If API failed
                /*            if (!response.IsSuccessStatusCode)
                            {
                                return $"Gemini API Error: {result}";
                            }
                */

                if (!response.IsSuccessStatusCode)
                {
                    // 🔥 IMPORTANT: Use TicketStatus FIRST

                    if (booking.TicketStatus == "CANCELLED")
                    {
                        // Now check original booking state
                        if (booking.Status == "CNF")
                        {
                            return "Your confirmed ticket was cancelled. Refund depends on cancellation timing. If cancelled within 24 hours, higher deduction applies.";
                        }

                        if (booking.Status == "WL")
                        {
                            return "Your waitlisted ticket was cancelled. Full refund is issued as it was not confirmed.";
                        }

                        if (booking.Status == "RAC")
                        {
                            return "Your RAC ticket was cancelled. Partial refund is given after deduction.";
                        }

                        return "Your ticket was cancelled. Refund calculated as per IRCTC rules.";
                    }

                    // NOT CANCELLED
                    return "This ticket is not cancelled yet. Refund will depend on when you cancel the ticket.";
                }

                // ❌ If response is not JSON
                if (!result.Trim().StartsWith("{"))
                {
                    return $"Invalid response from AI: {result}";
                }

                using var doc = JsonDocument.Parse(result);

                // ❌ If candidates missing
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates))
                {
                    return $"Unexpected AI response: {result}";
                }

                var text = candidates[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text ?? "No response from AI";

            }

            catch
            {
                return refundSummary;
            }

            return refundSummary;
        }

        private string CalculateRefund(Booking booking)
        {
            if (booking.TicketStatus != "CANCELLED")
            {
                return "Ticket not cancelled. No refund applicable.";
            }

            var now = DateTime.Now;
            var journey = booking.JourneyDate;

            DateTime cancelledTime = booking.CancelledAt ?? DateTime.Now;
            var hoursDiff = (journey - cancelledTime).TotalHours;

            decimal deduction = 0;

            // 🔥 IRCTC-like rules (simplified)
            if (booking.Status == "CNF")
            {
                if (hoursDiff > 48)
                    deduction = 60;
                else if (hoursDiff > 24)
                    deduction = 120;
                else if (hoursDiff > 12)
                    deduction = 240;
                else
                    deduction = booking.BaseFare * 0.5m;
            }
            else if (booking.Status == "RAC")
            {
                deduction = 60;
            }
            else if (booking.Status == "WL")
            {
                deduction = 0;
            }

            var refund = booking.BaseFare - deduction;
            if (refund < 0) refund = 0;

            return $@"
            Refund Summary:

            Ticket Status: {booking.Status}
            Base Fare: ₹{booking.BaseFare}

            Deduction: ₹{deduction}
            Refund Amount: ₹{refund}

            Reason:
            Refund calculated as per IRCTC cancellation rules based on timing and ticket status.";
        }
    }
}
