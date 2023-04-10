using sms_daemon;
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var baseUrl = configuration.GetValue<string>("Service:BaseUrl");
if (baseUrl == null)
{
    Console.Error.WriteLine("BaseUrl is not set in appsettings.json");
    return;
}

var listResult = ModemManager.ListModems();
if (listResult == null)
{
    Console.Error.WriteLine("Failed to get a list of available modems");
    return;
}

if (listResult.ModemList.Length == 0)
{
    Console.Error.WriteLine("No modems available");
    return;
}

var modemList = new List<ModemManager>();
foreach (var modemName in listResult.ModemList)
{
    ModemManager modem;
    try
    {
        modem = new ModemManager(modemName);
    }
    catch (ModemManagerException e)
    {
        Console.Error.WriteLine($"Error {e.Message}");
        return;
    }

    if (!modem.Enable())
    {
        throw new ModemManagerException($"Failed to enable modem {modem}");
    }

    if (modem.Numbers.Length == 0)
    {
        throw new ModemManagerException($"Modem {modem} has no number");
    }

    modemList.Add(modem);

    Console.WriteLine($"Using modem {modem.Modem} with number {modem.Numbers.First()}");
}

var apiClient = new SmsApiClient(new HttpClient(), baseUrl);

while (true)
{
    var messageList = new List<(ModemManager modem, string smsId)>();
    foreach(var modem in modemList)
    {
        var smsIdsResult = modem.ListSmsIds();
        if (smsIdsResult == null)
        {
            Console.Error.WriteLine($"Failed to retrieve messages from modem {modem.Modem}");
            continue;
        }
        
        foreach(var smsId in smsIdsResult.Sms)
        {
            messageList.Add((modem,smsId));
        }
    }
    
    // Apparently, there's a bug in mmcli that, if you read the messages
    // too quickly after the --messaging-list-sms call, it returns nonsense. :(
    // This is why we'll do the delay first, and then read the messages and 
    // send them to the API endpoint
    Thread.Sleep(500);

    if (!messageList.Any())
    {
        continue;
    }

    foreach ((var modem, var smsId) in messageList)
    {
        var sms = modem.GetSms(smsId);
        if (sms?.Sms?.Content == null)
        {
            Console.Error.WriteLine($"Failed to retrieve sms ID {smsId} from modem {modem.Modem}");
            continue;
        }

        if (sms.Sms.Properties?.PduType != "deliver")
        {
            continue;
        }

        var toNumber = $"+{modem.Numbers.First()}";

        Console.WriteLine($"Message \"{sms.Sms.Content.Text}\" " +
            $"from \"{sms.Sms.Content.Number}\" " +
            $"to \"{toNumber}\" ");

        var dto = new NewMessageDto
        {
            From = sms.Sms.Content.Number,
            To = toNumber,
            Message = sms.Sms.Content.Text
        };

        var success = await apiClient.PostNewMessageAsync(dto);
        if (success)
        {
            modem.DeleteSms(smsId);
        }
        else
        {
            Console.WriteLine($"Failed to send message to API endpoint.");
        }
    }
}