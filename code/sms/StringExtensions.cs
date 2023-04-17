using System.Text.RegularExpressions;

namespace sms;

public static class StringExtensions
{
    public static string Limit(this string s, int maxLen) =>
        s.Length > maxLen ? s.Substring(0, maxLen) : s;
}
