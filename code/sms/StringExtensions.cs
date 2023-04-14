using System.Text.RegularExpressions;

namespace sms;

public static class StringExtensions
{
    private static Regex _tailingDigits = new Regex(@"\d+$");

    public static string? GetNext(this string s, int maxLen)
    {
        var match = _tailingDigits.Match(s);
        var n = match.Success && int.TryParse(match.Value, out var parsed) ? parsed + 1 : 0;

        var nString = n.ToString();
        if (nString.Length > maxLen)
        {
            return null;
        }

        if (match.Success)
        {
            s = s.Substring(0, s.Length - match.Value.Length);
        }

        if (nString.Length + s.Length > maxLen)
        {
            s = s.Substring(0, maxLen - nString.Length);
        }

        return s + nString;
    }
}
