using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The XLIFF 2.1 container-format provider. Each entry is a <c>&lt;unit&gt;</c> whose <c>id</c> is the
/// symbolic key, with the source default in <c>&lt;source&gt;</c> and the translation in
/// <c>&lt;target&gt;</c>. The segment <c>state</c> carries the translation state natively; the exact
/// <see cref="TranslationState"/> is preserved in <c>subState</c>, and context, comments, references,
/// previous-source, and the source fingerprint are carried as categorized <c>&lt;note&gt;</c> elements.
/// ICU MessageFormat values are stored verbatim.
/// </summary>
public sealed class XliffTranslationFormat : ITranslationFormat
{
    private const string SubStatePrefix = "archpillar:";
    private static readonly XNamespace _ns = "urn:oasis:names:tc:xliff:document:2.1";
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public string FormatId => "xliff";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = [".xliff", ".xlf"];

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

        XDocument document = await Task.Run(() => XDocument.Load(input), cancellationToken).ConfigureAwait(false);
        return Parse(document);
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

    private static Catalog Parse(XDocument document)
    {
        XElement root = document.Root ?? new XElement(_ns + "xliff");

        // Read against the document's own namespace rather than a hardcoded one, so XLIFF 2.0 (same shape,
        // namespace differs by one digit) and an unqualified document both parse instead of silently
        // returning nothing.
        XNamespace ns = root.Name.Namespace;
        var sourceLanguage = (string?)root.Attribute("srcLang") ?? string.Empty;
        var targetLanguage = (string?)root.Attribute("trgLang");
        var culture = string.IsNullOrEmpty(targetLanguage) ? sourceLanguage : targetLanguage!;

        var entries = new List<CatalogEntry>();
        foreach (XElement unit in root.Descendants(ns + "unit"))
        {
            entries.Add(ParseUnit(unit, ns));
        }

        // XLIFF 1.x uses a different shape (<trans-unit>). Fail loudly rather than hand back an empty
        // catalog that looks like total data loss.
        if (entries.Count == 0 && root.Descendants().Any(element => element.Name.LocalName == "trans-unit"))
        {
            throw new NotSupportedException(
                $"XLIFF 1.x is not supported (version '{(string?)root.Attribute("version")}'); this provider reads XLIFF 2.x.");
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["srcLang"] = sourceLanguage };
        return new Catalog { Culture = culture, Entries = entries, Headers = headers };
    }

    private static CatalogEntry ParseUnit(XElement unit, XNamespace ns)
    {
        XElement? segment = unit.Element(ns + "segment");
        Notes notes = ReadNotes(unit, ns);
        return new CatalogEntry
        {
            Key = (string?)unit.Attribute("id") ?? string.Empty,
            SourceMessage = (string?)segment?.Element(ns + "source") ?? string.Empty,
            TranslatedMessage = (string?)segment?.Element(ns + "target"),
            Category = notes.Category ?? string.Empty,
            Context = notes.Context,
            Comment = notes.Comment,
            PreviousSource = notes.PreviousSource,
            References = notes.References,
            Placeholders = [],
            SourceFingerprint = notes.Fingerprint ?? string.Empty,
            State = ParseState(segment)
        };
    }

    private static Notes ReadNotes(XElement unit, XNamespace ns)
    {
        var notes = new Notes();
        XElement? container = unit.Element(ns + "notes");
        if (container is null)
        {
            return notes;
        }

        foreach (XElement note in container.Elements(ns + "note"))
        {
            ApplyNote(notes, (string?)note.Attribute("category"), note.Value);
        }

        return notes;
    }

    private static void ApplyNote(Notes notes, string? category, string value)
    {
        switch (category)
        {
            case "x-category":
                notes.Category = value;
                break;
            case "context":
                notes.Context = value;
                break;
            case "comment":
                notes.Comment = value;
                break;
            case "previous-source":
                notes.PreviousSource = value;
                break;
            case "fingerprint":
                notes.Fingerprint = value;
                break;
            case "reference":
                AddReference(notes, value);
                break;
            default:
                break;
        }
    }

    private static void AddReference(Notes notes, string value)
    {
        SourceReference? reference = SourceReferenceText.Parse(value);
        if (reference is not null)
        {
            notes.References.Add(reference);
        }
    }

    private static TranslationState ParseState(XElement? segment)
    {
        if (TryParseSubState((string?)segment?.Attribute("subState"), out TranslationState exact))
        {
            return exact;
        }

        return (string?)segment?.Attribute("state") switch
        {
            "translated" => TranslationState.Translated,
            "reviewed" => TranslationState.Final,
            "final" => TranslationState.Final,
            _ => TranslationState.NeedsTranslation
        };
    }

    private static bool TryParseSubState(string? subState, out TranslationState state)
    {
        state = TranslationState.NeedsTranslation;
        if (subState is null)
        {
            return false;
        }

        if (!subState.StartsWith(SubStatePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return Enum.TryParse(subState[SubStatePrefix.Length..], out state);
    }

    private static byte[] Serialize(Catalog catalog)
    {
        catalog.Headers.TryGetValue("srcLang", out var sourceLanguage);
        var root = new XElement(
            _ns + "xliff",
            new XAttribute("version", "2.1"),
            new XAttribute("srcLang", string.IsNullOrEmpty(sourceLanguage) ? "en" : sourceLanguage!),
            new XAttribute("trgLang", catalog.Culture),
            new XElement(_ns + "file", new XAttribute("id", "f1"), BuildUnits(catalog)));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return Render(document);
    }

    private static IEnumerable<XElement> BuildUnits(Catalog catalog)
    {
        foreach (CatalogEntry entry in catalog.Entries)
        {
            yield return BuildUnit(entry);
        }
    }

    private static XElement BuildUnit(CatalogEntry entry)
    {
        // xml:space="preserve" keeps whitespace-only or whitespace-edge content from being replaced by the
        // writer's indentation under Indent=true.
        XAttribute preserve = new(XNamespace.Xml + "space", "preserve");
        var segment = new XElement(
            _ns + "segment",
            new XAttribute("state", StandardState(entry.State)),
            new XAttribute("subState", SubStatePrefix + entry.State),
            new XElement(_ns + "source", new XAttribute(preserve), entry.SourceMessage));
        if (entry.TranslatedMessage is not null)
        {
            segment.Add(new XElement(_ns + "target", new XAttribute(preserve), entry.TranslatedMessage));
        }

        return new XElement(_ns + "unit", new XAttribute("id", entry.Key), BuildNotes(entry), segment);
    }

    private static XElement? BuildNotes(CatalogEntry entry)
    {
        var notes = new List<XElement>();
        AddNote(notes, "x-category", entry.Category);
        AddNote(notes, "context", entry.Context);
        AddNote(notes, "comment", entry.Comment);
        AddNote(notes, "previous-source", entry.PreviousSource);
        foreach (SourceReference reference in entry.References)
        {
            AddNote(notes, "reference", SourceReferenceText.Format(reference));
        }

        AddNote(notes, "fingerprint", entry.SourceFingerprint);
        return notes.Count == 0 ? null : new XElement(_ns + "notes", notes);
    }

    private static void AddNote(List<XElement> notes, string category, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            notes.Add(new XElement(_ns + "note", new XAttribute("category", category), value));
        }
    }

    private static string StandardState(TranslationState state) => state switch
    {
        TranslationState.Translated => "translated",
        TranslationState.NeedsReview => "translated",
        TranslationState.Final => "final",
        _ => "initial"
    };

    private static byte[] Render(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = _utf8NoBom
        };

        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        var text = _utf8NoBom.GetString(buffer.ToArray());
        return _utf8NoBom.GetBytes(text + "\n");
    }

    private sealed class Notes
    {
        public string? Category { get; set; }

        public string? Context { get; set; }

        public string? Comment { get; set; }

        public string? PreviousSource { get; set; }

        public string? Fingerprint { get; set; }

        public List<SourceReference> References { get; } = [];
    }
}
