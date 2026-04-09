namespace PicoNode.Http.Internal;

internal static class HttpCharacters
{
    public static bool IsHttpToken(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsHttpTokenCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidHeaderValue(string value)
    {
        foreach (var character in value)
        {
            if ((character < 0x20 && character != '\t') || character == 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsHttpTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
