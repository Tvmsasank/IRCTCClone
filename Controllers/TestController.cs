using IrctcClone.Services;
using IRCTCClone.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using Microsoft.Data.SqlClient;

namespace IrctcClone.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class TestController : ControllerBase
    {
        private readonly RagService _ragService;
        private readonly string _connectionString;
        private readonly IConfiguration configuration;

        public TestController(RagService ragService, IConfiguration configuration)
        {
            _ragService = ragService;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet("policy")]
        public IActionResult GetPolicy()
        {
            var data = _ragService.LoadRefundPolicy();
            return Ok(data);
        }

        [HttpGet("refund-explanation")]
        public async Task<IActionResult> GetRefundExplanation(string pnr)
        {
            try
            {

                Booking booking = null;

                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("spGetPNRDetails", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@PNR", pnr);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                booking = new Booking
                                {
                                    PNR = reader["PNR"].ToString(),
                                    BaseFare = reader["BaseFare"] != DBNull.Value
                                    ? Convert.ToDecimal(reader["BaseFare"])
                                    : 0,
                                    JourneyDate = reader["JourneyDate"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["JourneyDate"])
                                    : DateTime.MinValue,
                                    Status = reader["Status"].ToString(),
                                    TicketStatus = reader["TicketStatus"].ToString(),
                                    CancelledAt = reader["CancelledAt"] != DBNull.Value
                                    ? Convert.ToDateTime(reader["CancelledAt"])
                                    : null
                                };
                            }
                        }
                    }
                }

                if (booking == null)
                {
                    return NotFound("Booking not found for the provided PNR.");
                }

                var result = await _ragService.GetRefundExplanation(booking);

                return Content(result);
            }

            catch (Exception ex)
            {
                return Content("ERROR: " + ex.Message);
            }
        }
    }
}
