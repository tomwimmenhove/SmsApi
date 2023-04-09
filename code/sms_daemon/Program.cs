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

var result = Execute.Run("mmcli", "-m 0 --output-json");

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
    foreach(var modem in modemList)
    {
        var smsIdsResult = modem.ListSmsIds();
        if (smsIdsResult == null)
        {
            Console.Error.WriteLine($"Failed to retrieve messages from modem {modem.Modem}");
            continue;
        }

        var toNumber = $"+{modem.Numbers.First()}";
        
        foreach(var smsId in smsIdsResult.Sms)
        {
            var sms = modem.GetSms(smsId);
            if (sms?.Sms?.Content == null)
            {
                Console.Error.WriteLine($"Failed to retrieve sms ID {smsId} from modem {modem.Modem}");
                continue;
            }

            Console.Write($"Message \"{sms.Sms.Content.Text}\" " +
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
                Console.WriteLine($"Sent to API");
            }
            else
            {
                Console.WriteLine($"Not sent to API");
            }
        }
    }

    Thread.Sleep(1);
}