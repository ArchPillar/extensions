using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The Application Resource Bundle (ARB) container-format provider. ARB is a single JSON object per
/// locale: non-<c>@</c> keys are translatable entries whose value is an ICU MessageFormat string, a
/// sibling <c>@key</c> object carries metadata, and <c>@@</c> keys are file-level metadata. State,
/// references, the preserved source text (<c>source_text</c>, a predefined ARB attribute written when
/// the value is a distinct translation), previous-source, and the source fingerprint live under
/// metadata: <c>source_text</c> is part of the ARB spec, the <c>x-</c>-prefixed fields are custom
/// extensions the spec permits and other tools ignore.
/// </summary>
public sealed class ArbTranslationFormat : ITranslationFormat
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public string FormatId => "arb";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = [".arb"];

    /// <inheritdoc />
    public FormatCapabilities Capabilities =>
        FormatCapabilities.Context
        | FormatCapabilities.Comments
        | FormatCapabilities.SourceReferences
        | FormatCapabilities.ExplicitState
        | FormatCapabilities.IcuPlural
        | FormatCapabilities.PreviousSource;

    /// <inheritdoc />
    public async Task<Catalog> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using JsonDocument document = await JsonDocument
            .ParseAsync(input, default, cancellationToken)
            .ConfigureAwait(false);
        return Parse(document.RootElement);
    }

    /// <inheritdoc />
    public async Task WriteAsync(Stream output, Catalog catalog, CancellationToken cancellationToken)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        var bytes = Serialize(catalog);
        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    private static Catalog Parse(JsonElement root)
    {
        var culture = string.Empty;
        var defaultCategory = string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new List<KeyValuePair<string, string>>();
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name.StartsWith("@@", StringComparison.Ordinal))
            {
                ReadFileMetadata(property, ref culture, ref defaultCategory, headers);
            }
            else if (property.Name.StartsWith("@", StringComparison.Ordinal))
            {
                metadata[property.Name[1..]] = property.Value;
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                values.Add(new KeyValuePair<string, string>(property.Name, property.Value.GetString() ?? string.Empty));
            }

            // A non-string message value is skipped rather than thrown, so one malformed entry does not
            // drop the entire catalog.
        }

        var entries = new List<CatalogEntry>(values.Count);
        foreach (KeyValuePair<string, string> value in values)
        {
            entries.Add(BuildEntry(value.Key, value.Value, metadata, defaultCategory));
        }

        return new Catalog { Culture = culture, Entries = entries, Headers = headers };
    }

    private static void ReadFileMetadata(JsonProperty property, ref string culture, ref string defaultCategory, Dictionary<string, string> headers)
    {
        // File-metadata values are expected to be strings; a non-string value (e.g. from an "@@"-prefixed
        // user key) is ignored rather than thrown, so it does not drop the whole catalog.
        var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : null;
        if (value is null)
        {
            return;
        }

        if (property.Name == "@@locale")
        {
            culture = value;
            return;
        }

        if (property.Name == "@@x-category")
        {
            defaultCategory = value;
            return;
        }

        headers[property.Name[2..]] = value;
    }

    private static CatalogEntry BuildEntry(string member, string value, Dictionary<string, JsonElement> metadata, string defaultCategory)
    {
        metadata.TryGetValue(member, out JsonElement meta);
        var category = GetString(meta, "x-category") ?? defaultCategory;
        var context = GetString(meta, "context");
        var stateText = GetString(meta, "x-state");
        return new CatalogEntry
        {
            Key = QualifiedKey.Unqualify(member, category, context),
            // ARB stores one value per key, so a translated entry's value is the translation. The source text
            // is preserved separately under the spec's source_text attribute (written on save when it differs
            // from the value), so the original stays visible to translation tools and round-trips; a file
            // without it falls back to the value.
            SourceMessage = GetString(meta, "source_text") ?? value,
            // An entry explicitly marked NeedsTranslation has no translation (its value is the source
            // placeholder), so it picks up a refreshed source on the next sync instead of pinning the old one.
            // A file without x-state (a hand-authored or Flutter ARB) carries the value as the translation.
            TranslatedMessage = string.Equals(stateText, nameof(TranslationState.NeedsTranslation), StringComparison.Ordinal)
                ? null
                : value,
            Category = category,
            Context = context,
            Comment = GetString(meta, "description"),
            PreviousSource = GetString(meta, "x-previous-source"),
            Placeholders = GetPlaceholders(meta),
            References = GetReferences(meta),
            SourceFingerprint = GetString(meta, "x-source-fingerprint") ?? string.Empty,
            State = ParseState(stateText)
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string> GetPlaceholders(JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("placeholders", out JsonElement placeholders)
            || placeholders.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var names = new List<string>();
        foreach (JsonProperty property in placeholders.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    private static IReadOnlyList<SourceReference> GetReferences(JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("x-references", out JsonElement references)
            || references.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<SourceReference>();
        foreach (JsonElement element in references.EnumerateArray())
        {
            SourceReference? reference = SourceReferenceText.Parse(element.GetString());
            if (reference is not null)
            {
                result.Add(reference);
            }
        }

        return result;
    }

    private static TranslationState ParseState(string? state) =>
        Enum.TryParse(state, out TranslationState parsed) ? parsed : TranslationState.NeedsTranslation;

    private static byte[] Serialize(Catalog catalog)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("@@locale", catalog.Culture);
            foreach (KeyValuePair<string, string> header in catalog.Headers)
            {
                writer.WriteString("@@" + header.Key, header.Value);
            }

            foreach (CatalogEntry entry in catalog.Entries)
            {
                WriteEntry(writer, entry);
            }

            writer.WriteEndObject();
        }

        var json = _utf8NoBom.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        return _utf8NoBom.GetBytes(json + "\n");
    }

    private static void WriteEntry(Utf8JsonWriter writer, CatalogEntry entry)
    {
        // The member name is the category-qualified identity, so entries from different categories (or with
        // different contexts) never collide as JSON members, and a key beginning with "@" becomes a member
        // beginning with the category (or "::"), never mistaken for metadata.
        var member = QualifiedKey.Qualify(entry.Category, entry.Key, entry.Context);
        var value = entry.TranslatedMessage ?? entry.SourceMessage;
        writer.WriteString(member, value);
        writer.WritePropertyName("@" + member);
        writer.WriteStartObject();
        WriteOptionalString(writer, "description", entry.Comment);
        WriteOptionalString(writer, "context", entry.Context);
        WriteOptionalString(writer, "x-category", entry.Category);
        WritePlaceholders(writer, entry.Placeholders);
        writer.WriteString("x-state", entry.State.ToString());
        WriteReferences(writer, entry.References);
        // Preserve the source text whenever the value is a distinct translation (ARB's single value per key would
        // otherwise lose it once translated), so a translator keeps the original to work from and it round-trips.
        // source_text is a predefined ARB attribute meant for exactly this, so tools that read it recognise it.
        if (!string.Equals(entry.SourceMessage, value, StringComparison.Ordinal))
        {
            writer.WriteString("source_text", entry.SourceMessage);
        }

        WriteOptionalString(writer, "x-previous-source", entry.PreviousSource);
        writer.WriteString("x-source-fingerprint", entry.SourceFingerprint);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WritePlaceholders(Utf8JsonWriter writer, IReadOnlyList<string> placeholders)
    {
        if (placeholders.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("placeholders");
        writer.WriteStartObject();
        foreach (var placeholder in placeholders)
        {
            writer.WritePropertyName(placeholder);
            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteReferences(Utf8JsonWriter writer, IReadOnlyList<SourceReference> references)
    {
        if (references.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("x-references");
        writer.WriteStartArray();
        foreach (SourceReference reference in references)
        {
            writer.WriteStringValue(SourceReferenceText.Format(reference));
        }

        writer.WriteEndArray();
    }
}
