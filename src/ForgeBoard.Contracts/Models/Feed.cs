using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ForgeBoard.Contracts.Models;

public sealed class Feed
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public FeedType SourceType { get; set; } = FeedType.LocalPath;

    public string ConnectionString { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Repository { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string DeterministicId
    {
        get
        {
            string input = $"{SourceType}|{ConnectionString}|{Repository ?? string.Empty}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(hash)[..16];
        }
    }

    public void AssignDeterministicId()
    {
        Id = DeterministicId;
    }
}
