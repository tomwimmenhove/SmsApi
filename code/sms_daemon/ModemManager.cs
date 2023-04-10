using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;

namespace sms_daemon;

public class ModemManagerException : Exception
{
    public ModemManagerException(string message) : base(message) { }
}
public class ModemManager
{
    private Regex ValidateNumberRegex = new Regex(@"^\+?\d*$", RegexOptions.Compiled);

    private const string _mmcli = "/usr/bin/mmcli";

    public string Modem { get; }
    public string[] Numbers { get; }

    public ModemManager(string modem)
    {
        var info = GetModemInfo(modem);
        if (info == null)
        {
            throw new ModemManagerException($"Failed to get information on modem {modem}");
        }

        Numbers = info.Modem.Generic.OwnNumbers;
        Modem = modem;
    }

    public static ModemManagerListModemsDto? ListModems()
    {
        var result = Execute.Run(_mmcli, "--list-modems --output-json");
        if (!result.Success)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerListModemsDto>(result.StdOut);
    }

    public static ModemManagerModemInfoDto? GetModemInfo(string modem)
    {
        var result = Execute.Run(_mmcli, $"-m {modem} --output-json");
        if (!result.Success)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerModemInfoDto>(result.StdOut);
    }

    public bool Enable()
    {
        var result = Execute.Run(_mmcli, $"-e -m {Modem}");
        return result.Success;
    }

    public ModemManagerListSmsDto? ListSmsIds()
    {
        var result = Execute.Run(_mmcli, $"-m {Modem} --messaging-list-sms --output-json");
        if (!result.Success)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerListSmsDto>(result.StdOut);
    }

    public ModemManagerSmsDto? GetSms(string smsId)
    {
        var result = Execute.Run(_mmcli, $"-m {Modem} --sms {smsId} --output-json");
        if (!result.Success)
        {
            return null;
        }

        File.AppendAllText("/tmp/test.log", $"{result.StdOut}\n\n");

        return JsonSerializer.Deserialize<ModemManagerSmsDto>(result.StdOut);
    }

    public bool DeleteSms(string smsId)
    {
        var result = Execute.Run(_mmcli, $"-m {Modem} --messaging-delete-sms {smsId}");
        return result.Success;
    }

    private bool Send(string smsId)
    {
        var result = Execute.Run(_mmcli, $"-m {Modem} -s {smsId} --send");
        return result.Success;
    }

    public bool SendSms(string number, string message)
    {
        /* Valiate the phone number */
        if (!ValidateNumberRegex.IsMatch(number))
        {
            return false;
        }

        /* Because mmcli can't deal with ' and " well AND because I'm a lazy fuck, use a helper
         * function to create the sms using a python script */
        var createSmsHelper = Path.Combine(
            Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
            "create_sms");

        var result = Execute.Run(createSmsHelper, $"{Modem} \"{number}\"", message);
        if (!result.Success)
        {
            Console.Error.WriteLine($"{createSmsHelper} with error code {result.ExitCode}");
            return false;
        }

        if (!Send(result.StdOut))
        {
            return false;
        }

        DeleteSms(result.StdOut);

        return true;
    }
}
