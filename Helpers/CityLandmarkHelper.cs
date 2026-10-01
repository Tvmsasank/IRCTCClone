using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Html;

namespace IRCTCClone.Helpers
{
    public class CityLandmark
    {
        public string City { get; set; } = string.Empty;
        public string LandmarkName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Tagline { get; set; } = string.Empty;
        public string BadgeClass { get; set; } = string.Empty;
    }

    public static class CityLandmarkHelper
    {
        private static readonly Dictionary<string, CityLandmark> _landmarks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CHARMINAR"] = new CityLandmark
            {
                City = "Hyderabad",
                LandmarkName = "Charminar",
                ImageUrl = "/images/landmarks/charminar.svg",
                Tagline = "Hyderabad • Known for Iconic Charminar",
                BadgeClass = "landmark-hyderabad"
            },
            ["VIDHANA_SOUDHA"] = new CityLandmark
            {
                City = "Bengaluru",
                LandmarkName = "Vidhana Soudha",
                ImageUrl = "/images/landmarks/vidhana_soudha.svg",
                Tagline = "Bengaluru • Known for Vidhana Soudha",
                BadgeClass = "landmark-bengaluru"
            },
            ["INDIA_GATE"] = new CityLandmark
            {
                City = "Delhi",
                LandmarkName = "India Gate",
                ImageUrl = "/images/landmarks/india_gate.svg",
                Tagline = "Delhi • Known for India Gate",
                BadgeClass = "landmark-delhi"
            },
            ["GATEWAY_OF_INDIA"] = new CityLandmark
            {
                City = "Mumbai",
                LandmarkName = "Gateway of India",
                ImageUrl = "/images/landmarks/gateway_of_india.svg",
                Tagline = "Mumbai • Known for Gateway of India",
                BadgeClass = "landmark-mumbai"
            },
            ["CHENNAI_CENTRAL"] = new CityLandmark
            {
                City = "Chennai",
                LandmarkName = "Chennai Central",
                ImageUrl = "/images/landmarks/chennai_central.svg",
                Tagline = "Chennai • Known for Chennai Central Heritage",
                BadgeClass = "landmark-chennai"
            },
            ["HOWRAH_BRIDGE"] = new CityLandmark
            {
                City = "Kolkata",
                LandmarkName = "Howrah Bridge",
                ImageUrl = "/images/landmarks/howrah_bridge.svg",
                Tagline = "Kolkata • Known for Howrah Bridge",
                BadgeClass = "landmark-kolkata"
            },
            ["TAJ_MAHAL"] = new CityLandmark
            {
                City = "Agra",
                LandmarkName = "Taj Mahal",
                ImageUrl = "/images/landmarks/taj_mahal.svg",
                Tagline = "Agra • Known for Taj Mahal",
                BadgeClass = "landmark-agra"
            },
            ["TIRUPATI_TEMPLE"] = new CityLandmark
            {
                City = "Tirupati",
                LandmarkName = "Tirumala Temple",
                ImageUrl = "/images/landmarks/tirupati_temple.svg",
                Tagline = "Tirupati • Known for Tirumala Venkateswara Temple",
                BadgeClass = "landmark-tirupati"
            },
            ["TENALI"] = new CityLandmark
            {
                City = "Tenali",
                LandmarkName = "Andhra Paris",
                ImageUrl = "/images/landmarks/tenali.svg",
                Tagline = "Tenali • Known as Andhra Paris & Vaikuntapuram",
                BadgeClass = "landmark-tenali"
            },
            ["PRAKASAM_BARRAGE"] = new CityLandmark
            {
                City = "Vijayawada",
                LandmarkName = "Prakasam Barrage",
                ImageUrl = "/images/landmarks/prakasam_barrage.svg",
                Tagline = "Vijayawada • Known for Prakasam Barrage & Kanaka Durga",
                BadgeClass = "landmark-vijayawada"
            }
        };

        public static CityLandmark? GetLandmark(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            string q = query.Trim().ToUpperInvariant();

            // Extract station codes or words if string has hyphens or spaces (e.g. "SBC - KSR BENGALURU" or "TENALI JN - TEL")
            var tokens = q.Split(new[] { ' ', '-', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

            // 1. Hyderabad / Secunderabad / Kacheguda
            if (ContainsAny(q, tokens, new[] { "KCG", "SC", "HYB", "LPI", "BMT", "CHZ", "FM", "MJF", "SNF" },
                                      new[] { "KACHEGUDA", "SECUNDERABAD", "HYDERABAD", "LINGAMPALLI", "CHARLAPALLI", "BEGUMPET" }))
            {
                return _landmarks["CHARMINAR"];
            }

            // 2. Bengaluru / Bangalore
            if (ContainsAny(q, tokens, new[] { "SBC", "YPR", "SMVB", "BNC", "BYPL", "KJM", "WFD" },
                                      new[] { "BENGALURU", "BANGALORE", "YESVANTPUR", "YESHWANTHPUR", "KSR BENGALURU", "WHITEFIELD" }))
            {
                return _landmarks["VIDHANA_SOUDHA"];
            }

            // 3. Delhi / New Delhi
            if (ContainsAny(q, tokens, new[] { "NDLS", "DLI", "NZM", "ANVT", "DEE", "DSA" },
                                      new[] { "DELHI", "NEW DELHI", "NIZAMUDDIN", "ANAND VIHAR" }))
            {
                return _landmarks["INDIA_GATE"];
            }

            // 4. Mumbai / Bombay
            if (ContainsAny(q, tokens, new[] { "CSMT", "BCT", "MMCT", "DR", "LTT", "BDTS" },
                                      new[] { "MUMBAI", "BOMBAY", "CST", "DADAR", "LOKMANYA", "BANDRA" }))
            {
                return _landmarks["GATEWAY_OF_INDIA"];
            }

            // 5. Chennai / Madras
            if (ContainsAny(q, tokens, new[] { "MAS", "MS", "MSB", "TBM" },
                                      new[] { "CHENNAI", "MADRAS", "CENTRAL", "EGMORE", "TAMBARAM" }))
            {
                return _landmarks["CHENNAI_CENTRAL"];
            }

            // 6. Kolkata / Calcutta
            if (ContainsAny(q, tokens, new[] { "HWH", "SDAH", "KOAA", "SHM" },
                                      new[] { "HOWRAH", "SEALDAH", "KOLKATA", "CALCUTTA", "SHALIMAR" }))
            {
                return _landmarks["HOWRAH_BRIDGE"];
            }

            // 7. Agra
            if (ContainsAny(q, tokens, new[] { "AGC", "AF" },
                                      new[] { "AGRA" }))
            {
                return _landmarks["TAJ_MAHAL"];
            }

            // 8. Tirupati
            if (ContainsAny(q, tokens, new[] { "TPTY", "RU" },
                                      new[] { "TIRUPATI", "RENIGUNTA", "TIRUMALA" }))
            {
                return _landmarks["TIRUPATI_TEMPLE"];
            }

            // 9. Tenali
            if (ContainsAny(q, tokens, new[] { "TEL" },
                                      new[] { "TENALI" }))
            {
                return _landmarks["TENALI"];
            }

            // 10. Vijayawada
            if (ContainsAny(q, tokens, new[] { "BZA", "RYP" },
                                      new[] { "VIJAYAWADA", "RAYANAPADU" }))
            {
                return _landmarks["PRAKASAM_BARRAGE"];
            }

            return null;
        }

        private static bool ContainsAny(string fullStr, string[] tokens, string[] exactCodes, string[] namePatterns)
        {
            foreach (var code in exactCodes)
            {
                foreach (var token in tokens)
                {
                    if (token.Equals(code, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            foreach (var name in namePatterns)
            {
                if (fullStr.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static HtmlString RenderBadge(string? query, string size = "20px", bool inlineText = false)
        {
            var landmark = GetLandmark(query);
            if (landmark == null) return HtmlString.Empty;

            var title = $"{landmark.City} • {landmark.LandmarkName} ({landmark.Tagline})";
            var html = $@"<span class=""landmark-badge {landmark.BadgeClass}"" title=""{title}"" data-bs-toggle=""tooltip"" data-bs-placement=""top"">"
                     + $@"<img src=""{landmark.ImageUrl}"" alt=""{landmark.LandmarkName}"" style=""width:{size}; height:{size}; vertical-align:middle; border-radius:50%; box-shadow:0 1px 3px rgba(0,0,0,0.18);"" />"
                     + (inlineText ? $@" <span class=""landmark-mini-name"">{landmark.LandmarkName}</span>" : "")
                     + @"</span>";

            return new HtmlString(html);
        }
    }
}
