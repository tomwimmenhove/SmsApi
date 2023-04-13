using sms_daemon;
using Microsoft.Extensions.Configuration;

namespace sms_daemon;

public class DaemonException : Exception
{
    public DaemonException(string message) : base(message) { }
}

public class Daemon
{
    private readonly object _mmcliLocker = new();
    private readonly List<ModemManager> _modemList = new();
    private readonly SmsApiClient _apiClient;

    private string _baseUrl;
    private string _startIdFile;

    public Daemon(IConfiguration configuration)
    {
        var baseUrl = configuration.GetValue<string>("Service:BaseUrl");
        if (baseUrl == null)
        {
            throw new DaemonException("Service:BaseUrl not set in appsettings.json");
        }
        _baseUrl = baseUrl;

        var startIdFile = configuration.GetValue<string>("StartIdFile");
        if (startIdFile == null)
        {
            throw new DaemonException("StartIdFile not set in appsettings.json");
        }
        _startIdFile = startIdFile;        

        var mmcli = configuration.GetValue<string>("Mmcli");
        if (mmcli == null)
        {
            throw new DaemonException("Mmcli not set in appsettings.json");
        }

        var listResult = ModemManager.ListModems(mmcli);
        if (listResult == null)
        {
            throw new DaemonException("Failed to get a list of available modems");
        }

        if (listResult.ModemList.Length == 0)
        {
            throw new DaemonException("No modems available");
        }

        foreach (var modemName in listResult.ModemList)
        {
            ModemManager modem;
            try
            {
                modem = new ModemManager(modemName, mmcli);
            }
            catch (ModemManagerException e)
            {
                throw new DaemonException($"Error {e.Message}");
            }

            if (!modem.Enable())
            {
                throw new DaemonException($"Failed to enable modem {modem}");
            }

            if (modem.Numbers.Length == 0)
            {
                throw new DaemonException($"Modem {modem} has no number");
            }

            _modemList.Add(modem);

            Console.WriteLine($"Using modem {modem.Modem} with number {modem.Numbers.First()}");
        }

        _apiClient = new SmsApiClient(new HttpClient(), _baseUrl);
    }

    public async Task Run()
    {
        Task.Factory.StartNew(
            async () => await UpdatesChecker(), TaskCreationOptions.LongRunning)
            .Forget();

        while (true)
        {
            List<(ModemManager modem, string smsId)> messages;
            lock (_mmcliLocker)
            {
                messages = GetMessages();
            }

            // Apparently, there's a bug in mmcli that, if you read the messages
            // too quickly after the --messaging-list-sms call, it returns nonsense. :(
            // This is why we'll do the delay first, and then read the messages and 
            // send them to the API endpoint
            Thread.Sleep(500);

            if (!messages.Any())
            {
                continue;
            }

            foreach((var modem, var smsId) in messages)
            {
                await SendMessageToApi(modem, smsId);
            }
        }
    }

    private List<(ModemManager modem, string smsId)> GetMessages()
    {
        var messages = new List<(ModemManager modem, string smsId)>();
        foreach (var modem in _modemList)
        {
            var smsIdsResult = modem.ListSmsIds();
            if (smsIdsResult == null)
            {
                Console.Error.WriteLine($"Failed to retrieve messages from modem {modem.Modem}");
                continue;
            }

            foreach (var smsId in smsIdsResult.Sms)
            {
                messages.Add((modem, smsId));
            }
        }

        return messages;
    }

    private async Task SendMessageToApi(ModemManager modem, string smsId)
    {
        var sms = modem.GetSms(smsId);
        if (sms?.Sms?.Content == null)
        {
            Console.Error.WriteLine($"Failed to retrieve sms ID {smsId} from modem {modem.Modem}");
            return;
        }

        if (sms.Sms.Properties?.PduType != "deliver")
        {
            return;
        }

        var toNumber = $"+{modem.Numbers.First()}";

        Console.WriteLine($"Received message \"{sms.Sms.Content.Text}\" " +
            $"from \"{sms.Sms.Content.Number}\" " +
            $"to \"{toNumber}\" ");

        var dto = new NewMessageDto
        {
            From = sms.Sms.Content.Number,
            To = toNumber,
            Message = sms.Sms.Content.Text
        };

        var success = await _apiClient.PostNewMessageAsync(dto);
        if (success)
        {
            modem.DeleteSms(smsId);
        }
        else
        {
            Console.WriteLine($"Failed to send message to API endpoint.");
        }
    }

    private void SendMessageToModem(SendMessageUpdateDto message)
    {
        var modem = _modemList.FirstOrDefault(x => x.Numbers.Any(x => $"+{x}" == message.From));
        if (modem == null)
        {
            Console.Error.WriteLine($"No suitable modems found for number {message.From}");
            return;
        }

        var smsId = modem.CreateSms(message.To, message.Message);
        if (smsId == null)
        {
            Console.Error.WriteLine($"Failed to create SMS message ID {message.Id}");
            return;
        }

        if (!modem.SendSms(smsId))
        {
            Console.Error.WriteLine($"Failed to send SMS message ID {message.Id} ({smsId})");
            return;
        }

        if (!modem.DeleteSms(smsId))
        {
            Console.Error.WriteLine($"Failed to delete SMS message ID {message.Id} ({smsId})");
            return;
        }
    }

    private void SetStartId(long startId)
    {
        try
        {
            File.WriteAllText(_startIdFile, startId.ToString());
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to write to {_startIdFile}: {e}");
        }
    }

    private long GetStartId()
    {
        if (!File.Exists(_startIdFile))
        {
            return 0;
        }

        var numberString = File.ReadAllText(_startIdFile);
        if (!long.TryParse(numberString, out var number))
        {
            Console.Error.WriteLine($"Could not parse \"{numberString}\" in \"path\")");
            return 0;
        }

        return number;
    }

    private async Task UpdatesChecker()
    {
        var startId = GetStartId();
        while (true)
        {
            SendMessageUpdateResultsDto? result;
            try
            {
                result = await _apiClient.GetSendMessageUpdates(startId);
            }
            catch (TaskCanceledException)
            {
                continue;
            }
            if (result == null)
            {
                Thread.Sleep(10000);
                continue;
            }

            if (!result.Success || result.Messages == null || !result.Messages.Any())
            {
                continue;
            }

            foreach (var message in result.Messages)
            {
                if (message.Id >= startId)
                {
                    startId = message.Id + 1;
                }

                Console.WriteLine($"Sending message \"{message.Message}\" " +
                    $"from \"{message.From}\" " +
                    $"to \"{message.To}\" ");

                lock (_mmcliLocker)
                {
                    SendMessageToModem(message);
                }
            }

            SetStartId(startId);
        }
    }
}
