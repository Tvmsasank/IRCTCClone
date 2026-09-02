using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IRCTCClone.Models
{
    public class UserProfile
    {
        public int Id { get; set; }
        public string UserId { get; set; } // Email / ClaimIdentifier
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string MobileNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string Country { get; set; } = "India";
        public string Address { get; set; }
        public bool IsAadhaarVerified { get; set; } = true;
        public string AadhaarNumber { get; set; }
        public decimal WalletBalance { get; set; } = 0.00m;
        public DateTime? LastPasswordUpdate { get; set; }
    }

    public class MasterPassenger
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Concession { get; set; } = "Normal User";
        
        [Required]
        public string FullName { get; set; }
        
        public DateTime? DateOfBirth { get; set; }
        public int? Age { get; set; }
        public string Nationality { get; set; } = "Indian";
        
        [Required]
        public string Gender { get; set; } // Male, Female, Transgender
        
        public string BerthPreference { get; set; } = "No Preference"; // Lower, Middle, Upper, Side Lower, Side Upper, Window Side
        public string FoodChoice { get; set; } = "Veg"; // Veg, Non-Veg, No Food
        public string IdCardType { get; set; } = "AADHAAR"; // AADHAAR, VOTER ID, PASSPORT, DRIVING LICENSE, PAN CARD
        public string IdCardNumber { get; set; }
        public string VerificationStatus { get; set; } = "Verified"; // Verified, Not Verified, Re-Verify
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class FavoriteJourney
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string TrainNumber { get; set; }
        public string TrainName { get; set; }
        public string ClassCode { get; set; } = "3A";
        public string ClassName { get; set; } = "AC 3 Tier (3A)";
        public string FromStation { get; set; }
        public string FromStationCode { get; set; }
        public string ToStation { get; set; }
        public string ToStationCode { get; set; }
        public string Quota { get; set; } = "GENERAL";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProfileViewModel
    {
        public UserProfile Profile { get; set; } = new UserProfile();
        public List<MasterPassenger> MasterPassengers { get; set; } = new List<MasterPassenger>();
        public List<FavoriteJourney> FavoriteJourneys { get; set; } = new List<FavoriteJourney>();
        public string ActiveTab { get; set; } = "profile";
    }
}
