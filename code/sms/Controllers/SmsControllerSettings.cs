using System.ComponentModel.DataAnnotations;

namespace sms.Controllers;

public class SmsControllerSettings
{
    [Required]
    public string ConnectionString { get; set; } = default!;

    [Required]
    public string UsernameHeader { get; set; } = default!;

    [Required]
    public string[] Numbers { get; set; } = default!;
}
