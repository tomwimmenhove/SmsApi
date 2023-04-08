﻿using sms_daemon;

var apiClient = new SmsApiClient(new HttpClient(), "http://localhost:5001/sms/");

await apiClient.PostNewMessageAsync(new NewMessageDto {
    From = "From",
    To = "To",
    Message = "Hello world!"
});

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

    if (modem.Numbers.Length == 0)
    {
        throw new ModemManagerException($"Modem {modem} has no number");
    }

    modemList.Add(modem);

    Console.WriteLine($"Using modem {modem.Modem} with number {modem.Numbers.First()}");
}

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
                $"to \"{modem.Numbers.First()}\" ");

            var dto = new NewMessageDto
            {
                From = sms.Sms.Content.Number,
                To = modem.Numbers.First(),
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