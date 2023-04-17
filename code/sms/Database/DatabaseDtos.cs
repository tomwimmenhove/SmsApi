using System.Text.Json.Serialization;

namespace sms.Database;

public class SendMessageUpdateDto
{
    public long Id { get; set; }

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    public string From { get; set; } = default!;
    public string To { get; set; } = default!;
    public string Message { get; set; } = default!;
}

public class GetMessageDTO
{
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
    public string From { get; set; } = default!;
    public string To { get; set; } = default!;
    public string Message { get; set; } = default!;
}