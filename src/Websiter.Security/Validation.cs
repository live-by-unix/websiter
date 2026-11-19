using System.Net;
using System.Text.RegularExpressions;

namespace Websiter.Security;

/// <summary>Input validation helpers. Never trust client input; validate at the boundary.</summary>
public static partial class Validation
{
    /// <summary>Common email shape check (not RFC-complete; pair with confirmation email for real use).</summary>
    public static bool IsEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 254 && EmailRegex().IsMatch(value);

    /// <summary>Alphanumeric + safe punctuation identifier (usernames, slugs).</summary>
    public static bool IsSlug(string? value, int maxLength = 128)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && SlugRegex().IsMatch(value);

    /// <summary>Reasonable password policy: length + character classes.</summary>
    public static bool IsStrongPassword(string? value, int minLength = 12)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minLength || value.Length > 256) return false;
        var classes = 0;
        if (value.Any(char.IsLower)) classes++;
        if (value.Any(char.IsUpper)) classes++;
        if (value.Any(char.IsDigit)) classes++;
        if (value.Any(c => !char.IsLetterOrDigit(c))) classes++;
        return classes >= 3;
    }

    /// <summary>Validates an IPv4/IPv6 address string.</summary>
    public static bool IsIpAddress(string? value) => IPAddress.TryParse(value, out _);

    /// <summary>Basic URL sanity check (absolute http/https only).</summary>
    public static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-_]{0,127}$")]
    private static partial Regex SlugRegex();
}
