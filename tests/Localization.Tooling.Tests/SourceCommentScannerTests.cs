using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class SourceCommentScannerTests : IDisposable
{
    private readonly string _root;

    public SourceCommentScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aplcomments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private CommentIndex Scan(string source, string relativePath = "Sample.cs")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return SourceCommentScanner.Scan(_root);
    }

    [Fact]
    public void InlineBlockComment_AfterDefault_ReachesEntryByDefault()
    {
        CommentIndex index = Scan("""
            class C { void M() { T("home.title", "Home" /* shown in the header */); } }
            """);

        Assert.Equal("shown in the header", index.Lookup("home.title", "Home"));
    }

    [Fact]
    public void MultiLineLineComment_BeforeCloseParen_ReachesEntryByDefault()
    {
        CommentIndex index = Scan("""
            class C
            {
                void M()
                {
                    T(
                        "home.title",
                        "Home"
                        // shown in the header
                    );
                }
            }
            """);

        Assert.Equal("shown in the header", index.Lookup("home.title", "Home"));
    }

    [Fact]
    public void TextAsKeyMarker_JoinsByKey()
    {
        CommentIndex index = Scan("""
            class C { string M() => L("Log in" /* on the button, keep it short */); }
            """);

        Assert.Equal("on the button, keep it short", index.Lookup("Log in", "Log in"));
    }

    [Fact]
    public void EnumDisplayAnnotation_TextAsKey_JoinsByValue()
    {
        CommentIndex index = Scan("""
            enum Status
            {
                [System.ComponentModel.DataAnnotations.Display(Name = "Active" /* the running state */)]
                Active
            }
            """);

        Assert.Equal("the running state", index.Lookup("Active", "Active"));
    }

    [Fact]
    public void TwinAnnotation_CommentInTwin_ReachesEntryByDefault()
    {
        // The comment sits in the [Localized…] twin (which carries the default); the key comes from a paired
        // system attribute. Checking both literals lets the comment reach the (key, default) entry by its default.
        CommentIndex index = Scan("""
            class Account
            {
                [ArchPillar.Extensions.Localization.LocalizedDisplayName("Log in" /* on the button */)]
                public string LogIn { get; set; }
            }
            """);

        Assert.Equal("on the button", index.Lookup("auth.login", "Log in"));
    }

    [Fact]
    public void MultipleComments_ForSameLiteral_AreCombinedInOrderAndDeduplicated()
    {
        CommentIndex index = Scan("""
            class C
            {
                void M()
                {
                    T("k", "d" /* first note */);
                    T("k", "d" /* second note */);
                    T("k", "d" /* first note */);
                }
            }
            """);

        Assert.Equal("first note\nsecond note", index.Lookup("k", "d"));
    }

    [Fact]
    public void NoComment_ReturnsNull()
    {
        CommentIndex index = Scan("""
            class C { void M() { T("k", "d"); } }
            """);

        Assert.Null(index.Lookup("k", "d"));
    }

    [Fact]
    public void LeadingComment_AboveTheCall_IsIgnored()
    {
        // Leading trivia is deferred by design: only comments inside the argument list are extracted.
        CommentIndex index = Scan("""
            class C
            {
                void M()
                {
                    // not a translation comment
                    T("k", "d");
                }
            }
            """);

        Assert.Null(index.Lookup("k", "d"));
    }

    [Fact]
    public void SlashesInsideAString_AreNotMistakenForAComment()
    {
        // The "//" is string content, not a comment — a real hazard for a strings library. Roslyn knows the
        // difference, which is why the scan parses rather than regexes.
        CommentIndex index = Scan("""
            class C { void M() { T("cta", "50% off at https://x.io/sale"); } }
            """);

        Assert.Null(index.Lookup("cta", "50% off at https://x.io/sale"));
    }

    [Fact]
    public void CommentBesideAUrlDefault_IsStillExtracted()
    {
        CommentIndex index = Scan("""
            class C { void M() { T("cta", "https://x.io/sale" /* the promo link */); } }
            """);

        Assert.Equal("the promo link", index.Lookup("cta", "https://x.io/sale"));
    }

    [Fact]
    public void ConstantConcatenationDefault_IsFoldedForTheJoin()
    {
        CommentIndex index = Scan("""
            class C { void M() { T("greeting", "Hello, " + "world" /* the greeting */); } }
            """);

        Assert.Equal("the greeting", index.Lookup("greeting", "Hello, world"));
    }

    [Fact]
    public void BuildOutput_IsNotScanned()
    {
        CommentIndex index = Scan(
            """
            class C { void M() { T("k", "d" /* generated */); } }
            """,
            Path.Combine("obj", "Debug", "Generated.cs"));

        Assert.Null(index.Lookup("k", "d"));
    }

    [Fact]
    public void MissingSourceRoot_YieldsAnEmptyIndex()
    {
        CommentIndex index = SourceCommentScanner.Scan(Path.Combine(_root, "does-not-exist"));

        Assert.Null(index.Lookup("k", "d"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
