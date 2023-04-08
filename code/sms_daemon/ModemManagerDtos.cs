using System.Text.Json.Serialization;

namespace sms_daemon;

public class ModemManagerListModemsDto
{
    [JsonPropertyName("modem-list")]
    public string[] ModemList { get; set; } = default!;
}

public class ModemManager3GppDto
{
    [JsonPropertyName("imei")]
    public string Imei { get; set; } = default!;
}

public class ModemManagerGenericDto
{
    [JsonPropertyName("own-numbers")]
    public string[] OwnNumbers { get; set; } = default!;
}

public class ModemManagerModemDto
{
    [JsonPropertyName("3gpp")]
    public ModemManager3GppDto ThreeGpp { get; set; } = default!;

    [JsonPropertyName("generic")]
    public ModemManagerGenericDto Generic { get; set; } = default!;
}

public class ModemManagerModemInfoDto
{
    [JsonPropertyName("modem")]
    public ModemManagerModemDto Modem { get; set; } = default!;
}

public class ModemManagerListSmsDto
{
    [JsonPropertyName("modem.messaging.sms")]
    public string[] Sms { get; set; } = default!;
}


public class ModemManagerSmsDto
{
    [JsonPropertyName("sms")]
    public ModemManagerSmsEntryDto Sms { get; set; } = default!;
}

public class ModemManagerSmsEntryDto
{
    [JsonPropertyName("content")]
    public ModemManagerSmsContentDto Content { get; set; } = default!;
}

public class ModemManagerSmsContentDto
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = default!;

    [JsonPropertyName("text")]
    public string Text { get; set; } = default!;
}

