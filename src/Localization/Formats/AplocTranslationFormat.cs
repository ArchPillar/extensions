using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArchPillar.Extensions.Localization.Formats;

/// <summary>
/// The ArchPillar localization container format (<c>.aploc</c>): a compact, deploy-only JSON bundle the runtime
/// fetches. Unlike the authoring formats (XLIFF, ARB, PO), it carries only what the runtime resolves — the
/// translated value per entry — and folds the category into a nested object tree so a namespace segment is
/// written once no matter how many keys hang beneath it. Each dot-segment of the category is one nested object;
/// a key is a plain string member of its category's object, so keys and child namespaces sit side by side.
/// Uncategorized entries are the root's own string members; <c>@@locale</c> at the root carries the culture (as
/// in ARB). Keys and child namespaces share one member namespace, so a key whose name equals a child namespace
/// is written as that child's <c>"@"</c> member — the DNS-zone apex, the record at the node itself — which keeps
/// the segment name written once. A real bundle carries no <c>"@"</c> at all, since a category is a type
/// full-name and a name cannot be both a type and a namespace. <c>@</c> is the format's reserved sigil, so a key
/// named exactly <c>@</c> or starting with <c>@@</c> is invalid and rejected on write. Only the category nests —
/// a key is a member taken verbatim, so a dotted key (<c>home.title</c>) stays one key. It is intentionally lossy
/// on write — source text, state, comments, references and fingerprints are dropped — so it round-trips a
/// translation but not an authoring workflow; use <c>convert</c> to move a catalog back to an authoring format.
/// Written pretty by default and minified for the publish bundle; JSON string escaping keeps values with
/// newlines, quotes or <c>=</c> safe with no format-specific escaping.
/// </summary>
public sealed class AplocTranslationFormat : ITranslationFormat
{
    private const string LocaleHeader = "@@locale";
    private const string HeaderPrefix = "@@";
    private const string ApexMember = "@";

    private static readonly JsonWriterOptions _prettyOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonWriterOptions _minifiedOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public string FormatId => "aploc";

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = [".aploc"];

    /// <inheritdoc />
    public FormatCapabilities Capabilities => FormatCapabilities.IcuPlural;

    /// <inheritdoc />
    public Catalog Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var document = JsonDocument.Parse(input);
        var culture = string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<CatalogEntry>();

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            // Root-level @@ members are file metadata (the culture and any opaque headers), never entries.
            if (property.Value.ValueKind == JsonValueKind.String && property.Name.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                var value = property.Value.GetString() ?? string.Empty;
                headers[property.Name[HeaderPrefix.Length..]] = value;
                if (string.Equals(property.Name, LocaleHeader, StringComparison.Ordinal))
                {
                    culture = value;
                }
            }
        }

        // The root is just a nameless namespace node with the empty category: its string members are the global
        // entries, its object members top-level namespaces. Header strings (@@…) are skipped by the walk.
        ReadNode(document.RootElement, string.Empty, string.Empty, entries);
        return new Catalog { Culture = culture, Entries = entries, Headers = headers };
    }

    /// <inheritdoc />
    public async Task WriteAsync(Stream output, Catalog catalog, CatalogWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(catalog);

        var bytes = Serialize(catalog, options ?? CatalogWriteOptions.Default);
        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }

    // Recursively reads the namespace node reached by member <paramref name="name"/> of <paramref name="parentCategory"/>.
    // An object member is a child namespace; a string member is a key of this node's own category, except "@" —
    // the node's own value, which is the key named like the node and belongs to the parent's category. Knowing its
    // own name is what lets a node claim its apex itself, so the parent never looks inside a child.
    private static void ReadNode(JsonElement node, string parentCategory, string name, List<CatalogEntry> entries)
    {
        var category = parentCategory.Length == 0 ? name : parentCategory + "." + name;

        foreach (JsonProperty property in node.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                ReadNode(property.Value, category, property.Name, entries);
                continue;
            }

            // @@-prefixed strings are file headers, never entries.
            if (property.Value.ValueKind != JsonValueKind.String || property.Name.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var value = property.Value.GetString() ?? string.Empty;
            if (!string.Equals(property.Name, ApexMember, StringComparison.Ordinal))
            {
                entries.Add(BuildEntry(category, property.Name, value));
            }
            else if (name.Length > 0)
            {
                // The root is nameless, so it has no apex to claim; the writer never emits one there.
                entries.Add(BuildEntry(parentCategory, name, value));
            }
        }
    }

    // A deploy bundle carries only the translated value, so the source fields it does not store are filled from
    // the value and the entry reads back as a complete translation — the shape the runtime resolves against.
    private static CatalogEntry BuildEntry(string category, string key, string value) =>
        new()
        {
            Key = key,
            Category = category,
            SourceMessage = value,
            TranslatedMessage = value,
            SourceFingerprint = string.Empty,
            State = TranslationState.Translated
        };

    private static byte[] Serialize(Catalog catalog, CatalogWriteOptions options)
    {
        Node root = BuildTree(catalog);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, options.Minify ? _minifiedOptions : _prettyOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(LocaleHeader, catalog.Culture);
            foreach (KeyValuePair<string, string> header in catalog.Headers)
            {
                if (!string.Equals(header.Key, "locale", StringComparison.Ordinal))
                {
                    writer.WriteString(HeaderPrefix + header.Key, header.Value);
                }
            }

            WriteNode(writer, root);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static Node BuildTree(Catalog catalog)
    {
        var root = new Node();
        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (string.Equals(entry.Key, ApexMember, StringComparison.Ordinal)
                || entry.Key.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Key '{entry.Key}' is reserved: '@' names a namespace's own value and '@@' prefixes file headers.",
                    nameof(catalog));
            }

            Node node = root;
            if (!string.IsNullOrEmpty(entry.Category))
            {
                foreach (var segment in entry.Category.Split('.'))
                {
                    node = node.Child(segment);
                }
            }

            node.Keys[entry.Key] = entry.TranslatedMessage ?? entry.SourceMessage;
        }

        ClaimApexKeys(root);
        return root;
    }

    // Moves every key that names a child namespace into that child as its apex, so keys and children no longer
    // overlap and each node owns the one value it writes as "@". Runs once the whole tree is built, since a
    // colliding child may be created after the key was added.
    private static void ClaimApexKeys(Node node)
    {
        foreach (KeyValuePair<string, Node> child in node.Children)
        {
            if (node.Keys.TryGetValue(child.Key, out var value))
            {
                child.Value.Apex = value;
                node.Keys.Remove(child.Key);
            }

            ClaimApexKeys(child.Value);
        }
    }

    private static void WriteNode(Utf8JsonWriter writer, Node node)
    {
        // The node's own value goes first, so the write stays deterministic and byte-stable.
        if (node.Apex is not null)
        {
            writer.WriteString(ApexMember, node.Apex);
        }

        // Keys and child namespaces share one member namespace, and ClaimApexKeys already removed every overlap,
        // so the two merge into one sorted sequence for a stable order.
        var members = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> key in node.Keys)
        {
            members[key.Key] = key.Value;
        }

        foreach (KeyValuePair<string, Node> child in node.Children)
        {
            members[child.Key] = child.Value;
        }

        foreach (KeyValuePair<string, object> member in members)
        {
            if (member.Value is Node childNode)
            {
                writer.WritePropertyName(member.Key);
                writer.WriteStartObject();
                WriteNode(writer, childNode);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString(member.Key, (string)member.Value);
            }
        }
    }

    // A namespace node: its own keys (the category's entries) and its child namespace segments, each kept sorted
    // for a deterministic, byte-stable write. Apex is the value of the key named like this node, which lives in
    // the parent's category and is written as this node's "@" member.
    private sealed class Node
    {
        public string? Apex { get; set; }

        public SortedDictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public Node Child(string segment)
        {
            if (!Children.TryGetValue(segment, out Node? child))
            {
                child = new Node();
                Children[segment] = child;
            }

            return child;
        }
    }
}
