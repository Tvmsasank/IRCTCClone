using IRCTCClone.Models;
using Microsoft.AspNetCore.Mvc;

namespace IRCTCClone.Services
{
    public interface IAvailabilityService
    {
        Task<List<AvailabilityDto>> GetNext7DaysAsync(int trainId, string travelClass);
    }
}

