using IRCTCClone.Helpers;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json;

namespace IRCTCClone.Services
{
    public class AiSearchService
    {
        private readonly string _apiKey;

        public AiSearchService(IConfiguration config)
        {
            _apiKey = config["OpenAI:ApiKey"];
        }

        public async Task<ParsedSearch> ParseQueryAsync(string input)
        {
            var client = new OpenAIClient(_apiKey);

            var prompt = $@"
            Extract train booking details from the sentence.

            Sentence: ""{input}""

            Return STRICT JSON only:

            {{
                ""from"": ""city"",
                ""to"": ""city"",
                ""date"": ""yyyy-MM-dd"",
                ""class"": ""SL or 3A or 2A or CC""
            }}

            Rules:
            - 'tomorrow' → next date
            - 'today' → current date
            - AC → 3A
            - sleeper → SL
            - If missing, use defaults
            ";

            var chat = new ChatClient(model: "gpt-4o-mini", apiKey: _apiKey);

            var response = await chat.CompleteChatAsync(prompt);

            var json = response.Value.Content[0].Text;

            return JsonSerializer.Deserialize<ParsedSearch>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}