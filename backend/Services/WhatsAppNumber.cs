using System.Text.RegularExpressions;

namespace SmartCommute.Server.Services;

// A WhatsApp number as wa.me links need it: the full international number, digits only ("8801712345678").
// Bangladeshi numbers may be typed the local way ("01712-345678") or with +880; other countries must include the
// country code. Whether the number really uses WhatsApp cannot be checked from here; the user says so by saving it.
public static partial class WhatsAppNumber
{
    [GeneratedRegex(@"^8801[3-9]\d{8}$")]
    private static partial Regex BangladeshMobile();

    // The normalised number, "" for a blank value (meaning: remove it), or null when it is not a valid number.
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        var trimmed = text.Trim();
        if (trimmed.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')')) || trimmed.LastIndexOf('+') > 0)
        {
            return null;
        }
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00"))
        {
            digits = digits[2..];
        }
        if (digits.Length == 11 && digits.StartsWith("01"))
        {
            digits = "88" + digits;
        }
        if (digits.StartsWith("880"))
        {
            return BangladeshMobile().IsMatch(digits) ? digits : null;
        }
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }

    // "https://wa.me/8801712345678" for a stored number.
    public static string Link(string number) => $"https://wa.me/{number}";
}
