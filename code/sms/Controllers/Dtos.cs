using System.ComponentModel.DataAnnotations;

namespace sms.Controllers;

public class NewMessageDto
{
    [Required]
    public string From { get; set; } = default!;

    [Required]
    public string To { get; set; } = default!;

    [Required]
    public string Message { get; set; } = default!;
}

public class SimpleOkResponeDto
{
    public bool Success { get; set; } = true;
}

public class SimpleErrorResponeDto
{
    public bool Success { get; set; } = false;
    public string Message { get; set; } = default!;
}

public class GetTagDto
{
    public bool Success { get; set; } = true;
    public string Tag { get; set; } = default!;
}

public class GetUpdatesDto
{
    public bool Success { get; set; } = true;
    public List<int> Messages { get; set; } = default!;
}

public class GetMessageDto
{
    public bool Success { get; set; } = true;
    public List<int> Messages { get; set; } = default!;
}

public class MessageDTO
{
    public bool Success { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string From { get; set; } = default!;
    public string To { get; set; } = default!;
    public string Message { get; set; } = default!;
}
