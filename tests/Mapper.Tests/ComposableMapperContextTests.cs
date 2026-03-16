namespace ArchPillar.Extensions.Mapper.Tests;

/// <summary>
/// Demonstrates composing multiple <see cref="MapperContext"/> subclasses via
/// constructor injection — the same pattern used with a DI container in a real
/// application (each context registered as a singleton, dependencies wired
/// automatically).
/// </summary>
public class ComposableMapperContextTests
{
    // Wire up the dependency chain manually — a DI container does this in production.
    private readonly PublisherMappers _publisherMappers = new();
    private readonly BookMappers _bookMappers;
    private readonly AuthorMappers _authorMappers;
    private readonly CompositeMappers _composite;

    public ComposableMapperContextTests()
    {
        _bookMappers   = new BookMappers(_publisherMappers);
        _authorMappers = new AuthorMappers(_bookMappers);
        _composite     = new CompositeMappers(_publisherMappers, _bookMappers, _authorMappers);
    }

    // -----------------------------------------------------------------------
    // Leaf context — standalone mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_LeafContext_MapsIndependently()
    {
        var publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" };

        PublisherDto? dto = _publisherMappers.Publisher.Map(publisher);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.Equal("O'Reilly", dto.Name);
    }

    // -----------------------------------------------------------------------
    // Mid-level context — references leaf context mapper
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_ChildContext_InlinesParentMapper()
    {
        var book = new Book
        {
            Id        = 10,
            Title     = "Designing Data-Intensive Applications",
            Price     = 45.99m,
            Publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" },
        };

        BookDto? dto = _bookMappers.Book.Map(book);

        Assert.NotNull(dto);
        Assert.Equal(10, dto.Id);
        Assert.Equal("Designing Data-Intensive Applications", dto.Title);
        Assert.Equal(45.99m, dto.Price);
        Assert.Equal("O'Reilly", dto.Publisher.Name);
        Assert.Equal("USA", dto.Publisher.Country);
    }

    // -----------------------------------------------------------------------
    // Top-level context — references mid-level (which references leaf)
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_GrandparentContext_ChainsAcrossThreeContexts()
    {
        var author = new Author
        {
            Id   = 100,
            Name = "Martin Kleppmann",
            Books =
            [
                new Book
                {
                    Id        = 10,
                    Title     = "Designing Data-Intensive Applications",
                    Price     = 45.99m,
                    Publisher = new Publisher { Id = 1, Name = "O'Reilly" },
                },
            ],
        };

        AuthorDto? dto = _authorMappers.Author.Map(author);

        Assert.NotNull(dto);
        Assert.Equal(100, dto.Id);
        Assert.Equal("Martin Kleppmann", dto.Name);
        Assert.NotNull(dto.Books);
        Assert.Single(dto.Books);
        Assert.Equal("Designing Data-Intensive Applications", dto.Books[0].Title);
        Assert.Equal("O'Reilly", dto.Books[0].Publisher.Name);
    }

    // -----------------------------------------------------------------------
    // Composite facade — accessing individual contexts
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_CompositeFacade_DelegatesToIndividualContexts()
    {
        var book = new Book
        {
            Id        = 20,
            Title     = "Clean Code",
            Price     = 35.00m,
            Publisher = new Publisher { Id = 2, Name = "Prentice Hall" },
        };

        BookDto? dto = _composite.Books.Book.Map(book);

        Assert.NotNull(dto);
        Assert.Equal("Clean Code", dto.Title);
        Assert.Equal("Prentice Hall", dto.Publisher.Name);
    }

    // -----------------------------------------------------------------------
    // LINQ projection — expression inlining works across contexts
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_AcrossContexts_InlinesNestedExpressions()
    {
        IQueryable<Book> books = new[]
        {
            new Book
            {
                Id        = 10,
                Title     = "DDIA",
                Price     = 45.99m,
                Publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" },
            },
            new Book
            {
                Id        = 20,
                Title     = "Clean Code",
                Price     = 35.00m,
                Publisher = new Publisher { Id = 2, Name = "Prentice Hall" },
            },
        }.AsQueryable();

        var results = books.Project(_bookMappers.Book).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("O'Reilly", results[0].Publisher.Name);
        Assert.Equal("Prentice Hall", results[1].Publisher.Name);
    }

    [Fact]
    public void Project_ThreeLevelChain_InlinesAllNestedExpressions()
    {
        IQueryable<Author> authors = new[]
        {
            new Author
            {
                Id   = 1,
                Name = "Martin Kleppmann",
                Books =
                [
                    new Book
                    {
                        Id        = 10,
                        Title     = "DDIA",
                        Price     = 45.99m,
                        Publisher = new Publisher { Id = 1, Name = "O'Reilly" },
                    },
                ],
            },
        }.AsQueryable();

        var results = authors
            .Project(_authorMappers.Author, o => o.Include(a => a.Books))
            .ToList();

        Assert.Single(results);
        Assert.NotNull(results[0].Books);
        Assert.Single(results[0].Books!);
        Assert.Equal("DDIA", results[0].Books![0].Title);
        Assert.Equal("O'Reilly", results[0].Books![0].Publisher.Name);
    }

    // -----------------------------------------------------------------------
    // Optional properties propagate across context boundaries
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_OptionalPropertyOnLeafContext_IncludedWhenRequested()
    {
        IQueryable<Book> books = new[]
        {
            new Book
            {
                Id        = 10,
                Title     = "DDIA",
                Price     = 45.99m,
                Publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" },
            },
        }.AsQueryable();

        var results = books
            .Project(_bookMappers.Book, o => o
                .Include("Publisher.Country"))
            .ToList();

        Assert.Equal("USA", results[0].Publisher.Country);
    }

    [Fact]
    public void Project_OptionalPropertyOnLeafContext_ExcludedByDefault()
    {
        IQueryable<Book> books = new[]
        {
            new Book
            {
                Id        = 10,
                Title     = "DDIA",
                Price     = 45.99m,
                Publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" },
            },
        }.AsQueryable();

        var results = books.Project(_bookMappers.Book).ToList();

        Assert.Null(results[0].Publisher.Country);
    }

    // -----------------------------------------------------------------------
    // Null handling works across context boundaries
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_NullSource_ReturnsNullAcrossContexts()
    {
        BookDto? dto = _bookMappers.Book.Map(null);

        Assert.Null(dto);
    }

    // -----------------------------------------------------------------------
    // Multiple contexts share the same leaf without conflict
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_SharedLeafContext_NoConflict()
    {
        // Two independent mid-level contexts share the same PublisherMappers instance.
        var bookMappers1 = new BookMappers(_publisherMappers);
        var bookMappers2 = new BookMappers(_publisherMappers);

        var book = new Book
        {
            Id        = 1,
            Title     = "Book A",
            Price     = 10m,
            Publisher = new Publisher { Id = 1, Name = "Acme" },
        };

        BookDto? dto1 = bookMappers1.Book.Map(book);
        BookDto? dto2 = bookMappers2.Book.Map(book);

        Assert.Equal(dto1!.Publisher.Name, dto2!.Publisher.Name);
    }
}
