using Microsoft.AspNetCore.Mvc;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace IRCTCClone.Helpers
{
    public class ParsedSearch
    {
        public string From { get; set; }
        public string To { get; set; }
        public DateTime JourneyDate { get; set; }
        public string ClassCode { get; set; }
    }

    public static class NaturalLanguageParser
    {
        public static ParsedSearch Parse(string input)
        {
            // ❌ STEP 0: EMPTY CHECK
            if (string.IsNullOrWhiteSpace(input))
                throw new Exception("Search cannot be empty");

            input = input.ToLower().Trim();

            var result = new ParsedSearch
            {
                JourneyDate = DateTime.Today,
                ClassCode = "SL"
            };

            //-----------------------------------------
            // 1. NORMALIZE INPUT
            //-----------------------------------------
            input = input.Replace("→", " to ")
                         .Replace("-", " to ")
                         .Replace("from", "")
                         .Replace("  ", " ");

            //-----------------------------------------
            // 2. EXTRACT DATE
            //-----------------------------------------
            if (input.Contains("tomorrow"))
                result.JourneyDate = DateTime.Today.AddDays(1);
            else if (input.Contains("today"))
                result.JourneyDate = DateTime.Today;

            //-----------------------------------------
            // 3. EXTRACT CLASS
            //-----------------------------------------
            if (input.Contains("3ac"))
                result.ClassCode = "3A";
            else if (input.Contains("2ac"))
                result.ClassCode = "2A";
            else if (input.Contains("1ac"))
                result.ClassCode = "1A";
            else if (input.Contains("sleeper"))
                result.ClassCode = "SL";
            else if (input.Contains("chaircar"))
                result.ClassCode = "CC";
            else if (input.Contains("executivechaircar"))
                result.ClassCode = "EC";
            else if (input.Contains("sitting"))
                result.ClassCode = "2S";


                //-----------------------------------------
                // 4. REMOVE NOISE WORDS
                //-----------------------------------------
                string cleaned = input;

            string[] noiseWords = {
                "tomorrow", "today",
                "ac", "2ac", "3ac", "1ac",
                "sleeper", "class", "ticket", "book"
            };

            foreach (var word in noiseWords)
                cleaned = cleaned.Replace(word, "");

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            //-----------------------------------------
            // ❌ STEP 5: VALIDATE "to"
            //-----------------------------------------
            if (!cleaned.Contains(" to "))
                throw new Exception("Please provide both FROM and TO stations");

            var parts = cleaned.Split(" to ");

            result.From = parts[0].Trim();
            result.To = parts.Length > 1 ? parts[1].Trim() : "";

            //-----------------------------------------
            // ❌ STEP 6: VALIDATE FROM & TO
            //-----------------------------------------
            if (string.IsNullOrWhiteSpace(result.From))
                throw new Exception("FROM station is required");

            if (string.IsNullOrWhiteSpace(result.To))
                throw new Exception("TO station is required");

            //-----------------------------------------
            // FINAL CLEANUP
            //-----------------------------------------
            result.From = CleanCity(result.From);
            result.To = CleanCity(result.To);

            return result;
        }
        private static string CleanCity(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text.Split(" ")[0]; // take main word only
        }
    }
}