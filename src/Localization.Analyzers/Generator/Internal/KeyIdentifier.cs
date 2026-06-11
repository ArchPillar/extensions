using System.Text;

namespace ArchPillar.Extensions.Localization.Generator.Internal;

/// <summary>Turns a translation key into a valid, PascalCase C# identifier for the typed key registry.</summary>
internal static class KeyIdentifier
{
    public static string ToIdentifier(string key)
    {
        var builder = new StringBuilder(key.Length);
        var capitalizeNext = true;
        foreach (var character in key)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (builder.Length == 0)
        {
            return "_";
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
