using System.Text;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The GNU gettext Portable Object container-format provider. Our stable key (with optional context)
/// maps to <c>msgctxt</c> and the source default to <c>msgid</c>, so Poedit shows readable source.
/// A clean ICU cardinal plural is converted to native <c>msgid_plural</c>/<c>msgstr[n]</c> using the
/// locale's gettext form order; anything gettext cannot represent is kept as opaque ICU text. State is
/// inferred from the <c>fuzzy</c> flag and an empty <c>msgstr</c>.
/// </summary>
public sealed class PoTranslationFormat : ITranslationFormat
{
    private const string IcuArgPrefix = "icu-arg=";
    private const string FingerprintPrefix = "fingerprint=";
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public string FormatId => "po";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = [".po", ".pot"];

    /// <inheritdoc />
    public FormatCapabilities Capabilities =>
        FormatCapabilities.Context
        | FormatCapabilities.Comments
        | FormatCapabilities.SourceReferences
        | FormatCapabilities.NativePlural
        | FormatCapabilities.PreviousSource;

    /// <inheritdoc />
    public async Task<Catalog> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var reader = new StreamReader(input, _utf8NoBom);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return Parse(text);
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

        var bytes = _utf8NoBom.GetBytes(Serialize(catalog));
        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    private static Catalog Parse(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();
        var culture = string.Empty;
        var entry = new PoEntry();

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
            {
                culture = Flush(entry, headers, entries, culture);
                entry = new PoEntry();
                continue;
            }

            entry.Consume(line);
        }

        culture = Flush(entry, headers, entries, culture);
        return new Catalog { Culture = culture, Entries = entries, Headers = headers };
    }

    private static string Flush(PoEntry entry, Dictionary<string, string> headers, List<CatalogEntry> entries, string culture)
    {
        if (entry.IsEmpty || entry.IsObsolete)
        {
            return culture;
        }

        if (entry.IsHeader)
        {
            ParseHeaders(entry.Msgstr ?? string.Empty, headers);
            return headers.TryGetValue("Language", out var language) ? language : culture;
        }

        entries.Add(entry.ToCatalogEntry(culture));
        return culture;
    }

    private static void ParseHeaders(string headerBlock, Dictionary<string, string> headers)
    {
        foreach (var line in headerBlock.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }
    }

    private static string Serialize(Catalog catalog)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, catalog);
        foreach (CatalogEntry entry in catalog.Entries)
        {
            WriteEntry(builder, entry, catalog.Culture);
        }

        return builder.ToString();
    }

    private static void WriteHeader(StringBuilder builder, Catalog catalog)
    {
        builder.Append("msgid \"\"\n");
        builder.Append("msgstr \"\"\n");
        AppendHeaderLine(builder, "MIME-Version", Header(catalog, "MIME-Version", "1.0"));
        AppendHeaderLine(builder, "Content-Type", Header(catalog, "Content-Type", "text/plain; charset=UTF-8"));
        AppendHeaderLine(builder, "Content-Transfer-Encoding", Header(catalog, "Content-Transfer-Encoding", "8bit"));
        AppendHeaderLine(builder, "Language", catalog.Culture);
        AppendHeaderLine(builder, "Plural-Forms", Header(catalog, "Plural-Forms", PluralForms(catalog.Culture)));
        AppendExtraHeaders(builder, catalog.Headers);
        builder.Append('\n');
    }

    private static void AppendExtraHeaders(StringBuilder builder, IReadOnlyDictionary<string, string> headers)
    {
        var managed = new HashSet<string>(StringComparer.Ordinal)
        {
            "MIME-Version", "Content-Type", "Content-Transfer-Encoding", "Language", "Plural-Forms"
        };
        foreach (var key in headers.Keys.Where(k => !managed.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            AppendHeaderLine(builder, key, headers[key]);
        }
    }

    private static void AppendHeaderLine(StringBuilder builder, string key, string value) =>
        builder.Append('"').Append(Escape($"{key}: {value}\n")).Append("\"\n");

    private static string Header(Catalog catalog, string key, string fallback) =>
        catalog.Headers.TryGetValue(key, out var value) ? value : fallback;

    private static string PluralForms(string culture)
    {
        var count = PluralRules.GettextOrder(culture).Count;
        var expression = count == 2 ? "(n != 1)" : "0";
        return $"nplurals={count}; plural={expression};";
    }

    private static void WriteEntry(StringBuilder builder, CatalogEntry entry, string culture)
    {
        GettextPlural? plural = PoPluralConverter.ToGettext(entry.SourceMessage, entry.TranslatedMessage, culture);
        WriteComments(builder, entry, plural);
        if (entry.State == TranslationState.NeedsReview)
        {
            builder.Append("#, fuzzy\n");
        }

        AppendString(builder, "msgctxt", TranslationKey.Compose(entry.Key, entry.Context));
        if (plural is null)
        {
            WriteSingular(builder, entry);
        }
        else
        {
            WritePlural(builder, plural);
        }

        builder.Append('\n');
    }

    private static void WriteComments(StringBuilder builder, CatalogEntry entry, GettextPlural? plural)
    {
        if (plural is not null)
        {
            builder.Append("#. ").Append(IcuArgPrefix).Append(plural.ArgumentName).Append('\n');
        }

        if (!string.IsNullOrEmpty(entry.Comment))
        {
            foreach (var line in entry.Comment!.Split('\n'))
            {
                builder.Append("#. ").Append(line).Append('\n');
            }
        }

        builder.Append("#. ").Append(FingerprintPrefix).Append(entry.SourceFingerprint).Append('\n');
        foreach (SourceReference reference in entry.References)
        {
            builder.Append("#: ").Append(SourceReferenceText.Format(reference)).Append('\n');
        }

        if (!string.IsNullOrEmpty(entry.PreviousSource))
        {
            builder.Append("#| msgid \"").Append(Escape(entry.PreviousSource!)).Append("\"\n");
        }
    }

    private static void WriteSingular(StringBuilder builder, CatalogEntry entry)
    {
        AppendString(builder, "msgid", entry.SourceMessage);
        AppendString(builder, "msgstr", entry.State == TranslationState.NeedsTranslation ? string.Empty : entry.TranslatedMessage ?? string.Empty);
    }

    private static void WritePlural(StringBuilder builder, GettextPlural plural)
    {
        AppendString(builder, "msgid", plural.SingularSource);
        AppendString(builder, "msgid_plural", plural.PluralSource);
        for (var index = 0; index < plural.TranslatedForms.Count; index++)
        {
            AppendString(builder, $"msgstr[{index}]", plural.TranslatedForms[index]);
        }
    }

    private static void AppendString(StringBuilder builder, string keyword, string value) =>
        builder.Append(keyword).Append(" \"").Append(Escape(value)).Append("\"\n");

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal);

    internal static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            index = AppendUnescaped(builder, value, index);
        }

        return builder.ToString();
    }

    private static int AppendUnescaped(StringBuilder builder, string value, int index)
    {
        if (value[index] != '\\' || index + 1 >= value.Length)
        {
            builder.Append(value[index]);
            return index;
        }

        builder.Append(value[index + 1] switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            _ => value[index + 1]
        });
        return index + 1;
    }

    /// <summary>A single Portable Object entry under construction while parsing.</summary>
    private sealed class PoEntry
    {
        private readonly List<string> _comments = [];
        private readonly Dictionary<int, string> _plurals = [];
        private Field _last;
        private int _lastIndex;

        public string? Msgctxt { get; private set; }

        public string? Msgid { get; private set; }

        public string? MsgidPlural { get; private set; }

        public string? Msgstr { get; private set; }

        public string? PreviousSource { get; private set; }

        public string? IcuArgument { get; private set; }

        public string Fingerprint { get; private set; } = string.Empty;

        public bool Fuzzy { get; private set; }

        public bool IsObsolete { get; private set; }

        public List<SourceReference> References { get; } = [];

        public bool IsEmpty => Msgctxt is null && Msgid is null && _comments.Count == 0;

        public bool IsHeader => Msgctxt is null && Msgid is { Length: 0 };

        public void Consume(string line)
        {
            if (line[0] == '#')
            {
                ConsumeComment(line);
                return;
            }

            if (line[0] == '"')
            {
                AppendContinuation(Unescape(Quoted(line)));
                return;
            }

            ConsumeField(line);
        }

        public CatalogEntry ToCatalogEntry(string culture)
        {
            var composite = Msgctxt ?? string.Empty;
            var separator = composite.IndexOf(TranslationKey.Separator);
            var context = separator >= 0 ? composite[..separator] : null;
            var key = separator >= 0 ? composite[(separator + 1)..] : composite;

            (var source, var translated) = ResolveMessages(culture, out var hasTranslation);
            return new CatalogEntry
            {
                Key = key,
                Context = context,
                SourceMessage = source,
                TranslatedMessage = translated,
                Comment = _comments.Count == 0 ? null : string.Join("\n", _comments),
                PreviousSource = PreviousSource,
                References = References,
                Placeholders = [],
                SourceFingerprint = Fingerprint,
                State = InferState(hasTranslation)
            };
        }

        private (string Source, string? Translated) ResolveMessages(string culture, out bool hasTranslation)
        {
            if (MsgidPlural is null)
            {
                var translated = string.IsNullOrEmpty(Msgstr) ? null : Msgstr;
                hasTranslation = translated is not null;
                return (Msgid ?? string.Empty, translated);
            }

            var forms = new string[_plurals.Count];
            foreach (KeyValuePair<int, string> form in _plurals)
            {
                forms[form.Key] = form.Value;
            }

            hasTranslation = forms.Any(f => f.Length > 0);
            var plural = new GettextPlural(IcuArgument ?? "count", Msgid ?? string.Empty, MsgidPlural, forms);
            (var source, var translated2) = PoPluralConverter.FromGettext(plural, culture);
            return (source, translated2);
        }

        private TranslationState InferState(bool hasTranslation)
        {
            if (!hasTranslation)
            {
                return TranslationState.NeedsTranslation;
            }

            return Fuzzy ? TranslationState.NeedsReview : TranslationState.Translated;
        }

        private void ConsumeComment(string line)
        {
            var marker = line.Length > 1 ? line[1] : ' ';
            var rest = line.Length > 2 ? line[2..].Trim() : string.Empty;
            switch (marker)
            {
                case '.':
                    ConsumeExtracted(rest);
                    break;
                case ':':
                    AddReference(rest);
                    break;
                case ',':
                    Fuzzy |= rest.Contains("fuzzy", StringComparison.Ordinal);
                    break;
                case '|':
                    PreviousSource = Unescape(Quoted(rest));
                    break;
                case '~':
                    IsObsolete = true;
                    break;
                default:
                    break;
            }
        }

        private void ConsumeExtracted(string rest)
        {
            if (rest.StartsWith(IcuArgPrefix, StringComparison.Ordinal))
            {
                IcuArgument = rest[IcuArgPrefix.Length..];
            }
            else if (rest.StartsWith(FingerprintPrefix, StringComparison.Ordinal))
            {
                Fingerprint = rest[FingerprintPrefix.Length..];
            }
            else
            {
                _comments.Add(rest);
            }
        }

        private void AddReference(string rest)
        {
            SourceReference? reference = SourceReferenceText.Parse(rest);
            if (reference is not null)
            {
                References.Add(reference);
            }
        }

        private void ConsumeField(string line)
        {
            var space = line.IndexOf(' ');
            if (space < 0)
            {
                return;
            }

            var keyword = line[..space];
            var value = Unescape(Quoted(line[(space + 1)..]));
            SetField(keyword, value);
        }

        private void SetField(string keyword, string value)
        {
            switch (keyword)
            {
                case "msgctxt":
                    Msgctxt = value;
                    _last = Field.Msgctxt;
                    break;
                case "msgid":
                    Msgid = value;
                    _last = Field.Msgid;
                    break;
                case "msgid_plural":
                    MsgidPlural = value;
                    _last = Field.MsgidPlural;
                    break;
                case "msgstr":
                    Msgstr = value;
                    _last = Field.Msgstr;
                    break;
                default:
                    SetIndexed(keyword, value);
                    break;
            }
        }

        private void SetIndexed(string keyword, string value)
        {
            if (!keyword.StartsWith("msgstr[", StringComparison.Ordinal) || !keyword.EndsWith("]", StringComparison.Ordinal))
            {
                return;
            }

            if (int.TryParse(keyword[7..^1], out var index))
            {
                _plurals[index] = value;
                _last = Field.Plural;
                _lastIndex = index;
            }
        }

        private void AppendContinuation(string value)
        {
            switch (_last)
            {
                case Field.Msgctxt:
                    Msgctxt += value;
                    break;
                case Field.Msgid:
                    Msgid += value;
                    break;
                case Field.MsgidPlural:
                    MsgidPlural += value;
                    break;
                case Field.Msgstr:
                    Msgstr += value;
                    break;
                case Field.Plural:
                    _plurals[_lastIndex] += value;
                    break;
                default:
                    break;
            }
        }

        private static string Quoted(string text)
        {
            var first = text.IndexOf('"');
            var last = text.LastIndexOf('"');
            return last > first && first >= 0 ? text[(first + 1)..last] : string.Empty;
        }

        private enum Field
        {
            None,
            Msgctxt,
            Msgid,
            MsgidPlural,
            Msgstr,
            Plural
        }
    }
}
