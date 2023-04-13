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
    private Regex _validateNumberRegex = new Regex(@"^\+?\d*$", RegexOptions.Compiled);

    private readonly object _mmcliAtomic = new();
    private readonly string _mmcli;

    public string Modem { get; }
    public string[] Numbers { get; }

    public ModemManager(string modem, string mmcli)
    {
        _mmcli = mmcli;
        
        var info = GetModemInfo(modem);
        if (info == null)
        {
            throw new ModemManagerException($"Failed to get information on modem {modem}");
        }

        Numbers = info.Modem.Generic.OwnNumbers;
        Modem = modem;
    }

    private static void PrintErrors(ExecuteResult result)
    {
        var stdOutLines = result.StdOut.Split("\n");
        var stdErrLines = result.StdErr.Split("\n");

        Console.Error.WriteLine("mmcli failed:");
        foreach(var line in stdOutLines)
        {
            Console.Error.WriteLine($"stdout: {line}");
        }

        foreach(var line in stdErrLines)
        {
            Console.Error.WriteLine($"stderr: {line}");
        }
    }

    public static ModemManagerListModemsDto? ListModems(string mmcli)
    {
        var result = Execute.Run(mmcli, "--list-modems --output-json");
        if (!result.Success)
        {
            PrintErrors(result);
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerListModemsDto>(result.StdOut);
    }

    private ModemManagerModemInfoDto? GetModemInfo(string modem)
    {
        var result = Execute.Run(_mmcli, $"-m {modem} --output-json");
        if (!result.Success)
        {
            PrintErrors(result);
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerModemInfoDto>(result.StdOut);
    }

    public bool Enable()
    {
        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli, $"-e -m {Modem}");
        }
        return result.Success;
    }

    public ModemManagerListSmsDto? ListSmsIds()
    {
        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli, $"-m {Modem} --messaging-list-sms --output-json");
        }
        if (!result.Success)
        {
            PrintErrors(result);
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerListSmsDto>(result.StdOut);
    }

    public ModemManagerSmsDto? GetSms(string smsId)
    {
        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli, $"-m {Modem} --sms {smsId} --output-json");
        }
        if (!result.Success)
        {
            PrintErrors(result);
            return null;
        }

        return JsonSerializer.Deserialize<ModemManagerSmsDto>(result.StdOut);
    }

    public string? CreateSms(string number, string message)
    {
        /* Valiate the phone number */
        if (!_validateNumberRegex.IsMatch(number))
        {
            Console.Error.WriteLine($"Invalid number: \"{number}\"");
            return null;
        }

        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli,
                $"-m {Modem} --messaging-create-sms=\"number='{number}'\" " + 
                "--messaging-create-sms-with-text=/dev/stdin",
                message);
        }

        if (!result.Success)
        {
            PrintErrors(result);
            return null;
        }

        var tokens = result.StdOut.Split(":");
        if (tokens.Length != 2)
        {
            Console.Error.WriteLine("Unexpected response from mmcli");
            PrintErrors(result);
            return null;
        }

        return tokens[1].Trim();
    }

    public bool SendSms(string smsId)
    {
        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli, $"-m {Modem} -s {smsId} --send");
        }
        return result.Success;
    }

    public bool DeleteSms(string smsId)
    {
        ExecuteResult result;
        lock(_mmcliAtomic)
        {
            result = Execute.Run(_mmcli, $"-m {Modem} --messaging-delete-sms {smsId}");
        }
        return result.Success;
    }
}
