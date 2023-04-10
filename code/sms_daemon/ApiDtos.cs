using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace sms_daemon;

public class NewMessageDto
{
    [Required]
    public string From { get; set; } = default!;

    [Required]
    public string To { get; set; } = default!;

    [Required]
    public string Message { get; set; } = default!;
}

public class NewMessageResponseDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = default!;
}

public class SendMessageUpdateResultsDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("messages")]
    public SendMessageUpdateDto[] Messages { get; set; } = default!;
}

public class SendMessageUpdateDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("from")]
    public string From { get; set; } = default!;

    [JsonPropertyName("to")]
    public string To { get; set; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = default!;
}