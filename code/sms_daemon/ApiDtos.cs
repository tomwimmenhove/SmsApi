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