using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArchPillar.Extensions.Localization.Generator.Internal;

/// <summary>
/// Computes the stable source fingerprint used to detect drift: a truncated SHA-256 over the
/// NFC-normalized source message and context. Deterministic across runs, runtimes, and platforms.
/// </summary>
internal static class Fingerprint
{
    public static string Compute(string source, string? context)
    {
        var normalized = source.Normalize(NormalizationForm.FormC) + "\0" + (context ?? string.Empty);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var builder = new StringBuilder(32);
        for (var index = 0; index < 16; index++)
        {
            builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
