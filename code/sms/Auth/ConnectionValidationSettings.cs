using System.ComponentModel.DataAnnotations;

namespace sms.Auth;

public class ConnectionValidationSettings
{
    [Required]
    public string SecretHeaders { get; set; } = default!;

    [Required]
    public string SecretValue { get; set; } = default!;
}
