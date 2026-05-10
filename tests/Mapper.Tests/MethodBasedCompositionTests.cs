namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Tests mapper composition via method calls (no arguments and constant arguments)
/// instead of direct property access.
/// </summary>
public class MethodBasedCompositionTests
{
    private readonly PublisherMappers _publisherMappers = new();
    private readonly BookMappers _bookMappers;

    public MethodBasedCompositionTests()
    {
        _bookMappers = new BookMappers(_publisherMappers);
    }

    // -----------------------------------------------------------------------
    // Method with no arguments
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_MethodNoArgs_InlinesNestedMapper()
    {
        var mappers = new MethodBasedBookMappers(_publisherMappers);
        var book = new Book
        {
            Id        = 10,
            Title     = "DDIA",
            Price     = 45.99m,
            Publisher = new Publisher { Id = 1, Name = "O'Reilly", Country = "USA" },
        };

        BookDto? dto = mappers.Book.Map(book);

        Assert.NotNull(dto);
        Assert.Equal(10, dto.Id);
        Assert.Equal("DDIA", dto.Title);
        Assert.Equal("O'Reilly", dto.Publisher.Name);
        Assert.Equal("USA", dto.Publisher.Country);
    }

    [Fact]
    public void Project_MethodNoArgs_InlinesForLinq()
    {
        var mappers = new MethodBasedBookMappers(_publisherMappers);
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

        var results = books.Project(mappers.Book).ToList();

        Assert.Single(results);
        Assert.Equal("O'Reilly", results[0].Publisher.Name);
    }

    [Fact]
    public void Map_MethodNoArgs_NullSource_ReturnsNull()
    {
        var mappers = new MethodBasedBookMappers(_publisherMappers);

        BookDto? dto = mappers.Book.Map(null);

        Assert.Null(dto);
    }

    // -----------------------------------------------------------------------
    // Method with constant arguments
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_MethodConstArgs_InlinesNestedMapper()
    {
        var mappers = new ConstArgMethodBookMappers(_publisherMappers);
        var book = new Book
        {
            Id        = 20,
            Title     = "Clean Code",
            Price     = 35.00m,
            Publisher = new Publisher { Id = 2, Name = "Prentice Hall" },
        };

        BookDto? dto = mappers.Book.Map(book);

        Assert.NotNull(dto);
        Assert.Equal("Clean Code", dto.Title);
        Assert.Equal("Prentice Hall", dto.Publisher.Name);
    }

    [Fact]
    public void Project_MethodConstArgs_InlinesForLinq()
    {
        var mappers = new ConstArgMethodBookMappers(_publisherMappers);
        IQueryable<Book> books = new[]
        {
            new Book
            {
                Id        = 20,
                Title     = "Clean Code",
                Price     = 35.00m,
                Publisher = new Publisher { Id = 2, Name = "Prentice Hall" },
            },
        }.AsQueryable();

        var results = books.Project(mappers.Book).ToList();

        Assert.Single(results);
        Assert.Equal("Prentice Hall", results[0].Publisher.Name);
    }

    // -----------------------------------------------------------------------
    // Collection projection via method-returned mapper
    // -----------------------------------------------------------------------

    [Fact]
    public void Map_MethodProjectCollection_InlinesNestedMapper()
    {
        var mappers = new MethodBasedAuthorMappers(_bookMappers);
        var author = new Author
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
        };

        AuthorDto? dto = mappers.Author.Map(author);

        Assert.NotNull(dto);
        Assert.Equal("Martin Kleppmann", dto.Name);
        Assert.NotNull(dto.Books);
        Assert.Single(dto.Books);
        Assert.Equal("DDIA", dto.Books[0].Title);
        Assert.Equal("O'Reilly", dto.Books[0].Publisher.Name);
    }

    [Fact]
    public void Project_MethodProjectCollection_IncludeOptional()
    {
        var mappers = new MethodBasedAuthorMappers(_bookMappers);
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
            .Project(mappers.Author, o => o.Include(a => a.Books))
            .ToList();

        Assert.Single(results);
        Assert.NotNull(results[0].Books);
        Assert.Single(results[0].Books!);
        Assert.Equal("DDIA", results[0].Books![0].Title);
    }

    // -----------------------------------------------------------------------
    // EagerBuildAll discovers parameterless methods
    // -----------------------------------------------------------------------

    [Fact]
    public void EagerBuildAll_DiscoversParameterlessMethods()
    {
        // Should not throw — EagerBuildAll finds and compiles GetPublisherMapper()
        var mappers = new EagerMethodBasedBookMappers(_publisherMappers);

        BookDto? dto = mappers.Book.Map(new Book
        {
            Id        = 1,
            Title     = "Test",
            Price     = 10m,
            Publisher = new Publisher { Id = 1, Name = "Pub" },
        });

        Assert.NotNull(dto);
        Assert.Equal("Pub", dto.Publisher.Name);
    }

    // -----------------------------------------------------------------------
    // Optional properties propagate through method-based composition
    // -----------------------------------------------------------------------

    [Fact]
    public void Project_MethodBased_OptionalOnLeaf_IncludedWhenRequested()
    {
        var mappers = new MethodBasedBookMappers(_publisherMappers);
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
            .Project(mappers.Book, o => o.Include("Publisher.Country"))
            .ToList();

        Assert.Equal("USA", results[0].Publisher.Country);
    }

    [Fact]
    public void Project_MethodBased_OptionalOnLeaf_ExcludedByDefault()
    {
        var mappers = new MethodBasedBookMappers(_publisherMappers);
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

        var results = books.Project(mappers.Book).ToList();

        Assert.Null(results[0].Publisher.Country);
    }
}
