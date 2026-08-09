using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The XLIFF 2.1 container-format provider. Each entry is a <c>&lt;unit&gt;</c> whose <c>name</c> is the
/// symbolic key and whose <c>id</c> is a stable hash of the entry's identity (category, key) — a valid,
/// unique <c>NMTOKEN</c> as the standard requires, which a bare key is not: text-as-key keys carry
/// spaces and punctuation, and the same key recurs across categories. Entries of a category are wrapped in a
/// <c>&lt;group&gt;</c> whose <c>name</c> is the category, so a translator tool shows the category as
/// structure. The source default is in <c>&lt;source&gt;</c> and the translation in <c>&lt;target&gt;</c>.
/// The segment <c>state</c> carries the translation state natively; the exact <see cref="TranslationState"/>
/// is preserved in <c>subState</c>, and comments, references, previous-source, and the source fingerprint
/// are carried as categorized <c>&lt;note&gt;</c> elements. ICU MessageFormat values are stored verbatim.
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
        FormatCapabilities.Comments
        | FormatCapabilities.SourceReferences
        | FormatCapabilities.ExplicitState
        | FormatCapabilities.IcuPlural
        | FormatCapabilities.PreviousSource;

    /// <inheritdoc />
    public Catalog Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var document = XDocument.Load(input);
        return Parse(document);
    }

    /// <inheritdoc />
    public async Task WriteAsync(Stream output, Catalog catalog, CatalogWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(catalog);

        var bytes = Serialize(catalog, options ?? CatalogWriteOptions.Default);
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
        var culture = string.IsNullOrEmpty(targetLanguage) ? sourceLanguage : targetLanguage;

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
            // The key is the human-readable name; a foreign file without a name falls back to the id.
            Key = (string?)unit.Attribute("name") ?? (string?)unit.Attribute("id") ?? string.Empty,
            SourceMessage = (string?)segment?.Element(ns + "source") ?? string.Empty,
            TranslatedMessage = (string?)segment?.Element(ns + "target"),
            // The category is the enclosing <group>'s name, or empty for a unit directly in the file.
            Category = CategoryOf(unit, ns) ?? string.Empty,
            Comment = notes.Comment,
            PreviousSource = notes.PreviousSource,
            References = notes.References,
            Placeholders = [],
            SourceFingerprint = notes.Fingerprint ?? string.Empty,
            State = ParseState(segment)
        };
    }

    // The category a unit belongs to is its enclosing <group>'s name, or null when the unit sits directly in
    // the file (the global category). An empty name is treated as no category rather than a named empty one.
    private static string? CategoryOf(XElement unit, XNamespace ns)
    {
        var name = (string?)unit.Ancestors(ns + "group").FirstOrDefault()?.Attribute("name");
        return string.IsNullOrEmpty(name) ? null : name;
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

    private static byte[] Serialize(Catalog catalog, CatalogWriteOptions options)
    {
        catalog.Headers.TryGetValue("srcLang", out var sourceLanguage);
        var root = new XElement(
            _ns + "xliff",
            new XAttribute("version", "2.1"),
            new XAttribute("srcLang", string.IsNullOrEmpty(sourceLanguage) ? "en" : sourceLanguage),
            new XAttribute("trgLang", catalog.Culture),
            new XElement(_ns + "file", new XAttribute("id", FileId(options)), BuildFileContent(catalog)));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return Render(document, options.Minify);
    }

    // The <file> id identifies the logical source independently of target language, so it carries the
    // catalog's source name (the assembly) when known, and a generic token otherwise.
    private static string FileId(CatalogWriteOptions options) =>
        string.IsNullOrEmpty(options.SourceName) ? "f1" : options.SourceName;

    // Groups entries by category, deterministically: the global (empty) category's units sit directly in the
    // file; each named category is a <group name="{category}"> so a translator tool shows it as structure.
    private static IEnumerable<XElement> BuildFileContent(Catalog catalog)
    {
        // The unit id is a hash keyed on the full identity; a collision would mean two entries share an
        // identity — a caller bug surfaced here rather than emitted as a duplicate id the standard forbids.
        var seenUnitIds = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<IGrouping<string, CatalogEntry>> categories = catalog.Entries
            .GroupBy(entry => entry.Category ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(category => category.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, CatalogEntry> category in categories)
        {
            IEnumerable<XElement> units = category
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => BuildUnit(entry, seenUnitIds));
            if (category.Key.Length == 0)
            {
                foreach (XElement unit in units)
                {
                    yield return unit;
                }
            }
            else
            {
                yield return new XElement(
                    _ns + "group",
                    new XAttribute("id", GroupId(category.Key)),
                    new XAttribute("name", category.Key),
                    units);
            }
        }
    }

    private static XElement BuildUnit(CatalogEntry entry, HashSet<string> seenUnitIds)
    {
        var id = UnitId(entry);
        if (!seenUnitIds.Add(id))
        {
            throw new InvalidOperationException(
                $"Two catalog entries share the identity (category '{entry.Category}', key '{entry.Key}') and would emit the duplicate XLIFF unit id '{id}'.");
        }

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

        // name carries the human-readable key (the standard's "resource name"); id is the machine handle.
        return new XElement(
            _ns + "unit",
            new XAttribute("id", id),
            new XAttribute("name", entry.Key),
            BuildNotes(entry),
            segment);
    }

    // A unit's id is a valid NMTOKEN derived from the identity: the same key under two categories are
    // distinct entries that MUST get distinct ids within the file.
    private static string UnitId(CatalogEntry entry) =>
        "u" + ShortHash(string.Join('\u001f', entry.Category, entry.Key));

    // A category's <group> id, unique per category (categories are distinct type names).
    private static string GroupId(string category) => "g" + ShortHash(category);

    // A stable, process-independent 64-bit hash rendered as 16 lowercase hex characters — a valid NMTOKEN.
    // SHA-256, not string.GetHashCode (which is randomized per process and would break round-trip stability).
    private static string ShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static XElement? BuildNotes(CatalogEntry entry)
    {
        var notes = new List<XElement>();
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

    private static byte[] Render(XDocument document, bool minify)
    {
        // The publish bundle drops the insignificant indentation; the XLIFF structure (source/target, the
        // group/name the reader needs to recover the category and key) is kept, since it is all meaningful.
        var settings = new XmlWriterSettings
        {
            Indent = !minify,
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
        return _utf8NoBom.GetBytes(minify ? text : text + "\n");
    }

    private sealed class Notes
    {
        public string? Comment { get; set; }

        public string? PreviousSource { get; set; }

        public string? Fingerprint { get; set; }

        public List<SourceReference> References { get; } = [];
    }
}
