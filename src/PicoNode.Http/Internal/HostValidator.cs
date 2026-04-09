using System.Globalization;

namespace PicoNode.Http.Internal;

internal static class HostValidator
{
    public static bool IsValidHostHeaderValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is ',' or '/' or '?' or '#' or '@')
            {
                return false;
            }
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (value[0] == '[')
        {
            return TryParseBracketedIpv6Host(value);
        }

        var lastColon = value.LastIndexOf(':');
        string hostPart;

        if (lastColon >= 0)
        {
            hostPart = value[..lastColon];
            var portPart = value[(lastColon + 1)..];

            if (hostPart.Length == 0 || !IsValidPort(portPart))
            {
                return false;
            }
        }
        else
        {
            hostPart = value;
        }

        return IsValidHostName(hostPart) || IsValidIpv4Address(hostPart);
    }

    private static bool TryParseBracketedIpv6Host(string value)
    {
        var closingBracketIndex = value.IndexOf(']');
        if (closingBracketIndex <= 1)
        {
            return false;
        }

        var addressPart = value[1..closingBracketIndex];
        if (!System.Net.IPAddress.TryParse(addressPart, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (closingBracketIndex == value.Length - 1)
        {
            return true;
        }

        if (value[closingBracketIndex + 1] != ':')
        {
            return false;
        }

        return IsValidPort(value[(closingBracketIndex + 2)..]);
    }

    private static bool IsValidPort(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 0 and <= 65535;
    }

    private static bool IsValidHostName(string value)
    {
        if (value.Length == 0 || value.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        var labels = value.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0)
            {
                return false;
            }

            if (!char.IsAsciiLetterOrDigit(label[0]) || !char.IsAsciiLetterOrDigit(label[^1]))
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsValidIpv4Address(string value)
    {
        var segments = value.Split('.');
        if (segments.Length != 4)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            if (!byte.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }
}
