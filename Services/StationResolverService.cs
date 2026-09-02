using IRCTCClone.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

public class StationResolverService
{
    private readonly string _connectionString;
    private readonly IMemoryCache _cache;

    public StationResolverService(string connectionString, IMemoryCache cache)
    {
        _connectionString = connectionString;
        _cache = cache;
    }

    //-----------------------------------------
    // MAIN METHOD (USE THIS EVERYWHERE)
    //-----------------------------------------
    public int ResolveStation(string input)
    {
        input = Normalize(input);

        // STEP 1: Try SQL match (fast)
        int id = ResolveFromDatabase(input);

        if (id != 0)
            return id;

        // STEP 2: Fuzzy fallback (smart)
        return ResolveUsingFuzzy(input);
    }

    //-----------------------------------------
    // SQL MATCH
    //-----------------------------------------
    private int ResolveFromDatabase(string input)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        var cmd = new SqlCommand(@"
            SELECT TOP 1 Id
            FROM Stations
            WHERE 
                LOWER(City) LIKE '%' + @input + '%'
                OR LOWER(Name) LIKE '%' + @input + '%'
                OR LOWER(Code) LIKE @input + '%'
            ORDER BY 
                CASE 
                    WHEN LOWER(Code) = @input THEN 1
                    WHEN LOWER(City) LIKE @input + '%' THEN 2
                    WHEN LOWER(Name) LIKE @input + '%' THEN 3
                    ELSE 4
                END
        ", conn);

        cmd.Parameters.AddWithValue("@input", input);

        var result = cmd.ExecuteScalar();

        return result != null ? Convert.ToInt32(result) : 0;
    }

    //-----------------------------------------
    // FUZZY MATCH (Levenshtein)
    //-----------------------------------------
    private int ResolveUsingFuzzy(string input)
    {
        var stations = GetAllStations();

        var best = stations
            .Select(s => new
            {
                Station = s,
                Score = new[]
                {
            string.IsNullOrEmpty(s.City) ? 999 : Levenshtein(input, s.City.ToLower()),
            string.IsNullOrEmpty(s.Name) ? 999 : Levenshtein(input, s.Name.ToLower()),
            string.IsNullOrEmpty(s.Code) ? 999 : Levenshtein(input, s.Code.ToLower())
                }.Min()
            })
            .OrderBy(x => x.Score)
            .FirstOrDefault();

        if (best == null)
            return 0;

        // 🚨 IMPORTANT: threshold check
        if (best.Score > 4)
            return 0;

        return best.Station.Id;
    }

    //-----------------------------------------
    // CACHE ALL STATIONS
    //-----------------------------------------
    private List<Station> GetAllStations()
    {
        return _cache.GetOrCreate("ALL_STATIONS", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

            var list = new List<Station>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var cmd = new SqlCommand("SELECT Id, Name, Code, City FROM Stations", conn);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new Station
                {
                    Id = reader.GetInt32(0),

                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Code = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    City = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }

            return list;
        });
    }

    //-----------------------------------------
    // NORMALIZE INPUT
    //-----------------------------------------
    private string Normalize(string input)
    {
        return input.ToLower()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("-", " ")
            .Trim();
    }

    //-----------------------------------------
    // LEVENSHTEIN DISTANCE
    //-----------------------------------------
    private int Levenshtein(string s, string t)
    {
        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[n, m];
    }
}