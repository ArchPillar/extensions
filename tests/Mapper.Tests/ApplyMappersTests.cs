using ArchPillar.Extensions.Mapper.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Verifies the explicit <see cref="MapperEfCoreExtensions.ApplyMappers{TSource}"/>
/// path, which inlines mapper calls at query construction (before EF Core's
/// parameter extraction) and therefore supports mappers that contain
/// <see cref="Mapper{TSource,TDest}.Invoke(TSource)"/> in a hand-written
/// <c>Select</c> — the case the automatic interceptor cannot handle. Reuses the
/// <see cref="IxMappers"/> context (its FormData mapping routes through an
/// instance method, so it must run client-side).
/// </summary>
public sealed class ApplyMappersTests : IDisposable
{
    private readonly IxDbContext _db;
    private readonly IxMappers _mappers = new();

    public ApplyMappersTests()
    {
        DbContextOptionsBuilder<IxDbContext> builder = new DbContextOptionsBuilder<IxDbContext>()
            .UseSqlite("DataSource=:memory:");
        builder.UseArchPillarMapper();
        _db = new IxDbContext(builder.Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _db.Agents.Add(new IxAgent
        {
            Id   = 1,
            Form = new IxForm { Id = 1, Name = "F1", Data = new IxFormData { Id = 1, Raw = "hello" } },
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task ApplyMappers_ScalarMapInSelect_RunsInvokeInMemoryAsync()
    {
        IxFormVm result = await _db.Forms
            .Where(f => f.Id == 1)
            .Select(f => _mappers.Form.Map(f)!)
            .ApplyMappers()
            .SingleAsync();

        Assert.Equal("olleh", result.Data!.Value);
    }

    [Fact]
    public async Task ApplyMappers_CustomSelectWithMapperProperty_RunsInvokeInMemoryAsync()
    {
        var result = await _db.Forms
            .Where(f => f.Id == 1)
            .Select(f => new { f.Id, Vm = _mappers.Form.Map(f)! })
            .ApplyMappers()
            .SingleAsync();

        Assert.Equal(1, result.Id);
        Assert.Equal("olleh", result.Vm.Data!.Value);
    }

    [Fact]
    public async Task DirectMapWithoutApplyMappers_ThrowsHelpfulErrorAsync()
    {
        // The automatic interceptor runs after parameter extraction, so it cannot
        // inline an Invoke-containing mapper; it should fail fast pointing to ApplyMappers.
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _db.Forms
                .Where(f => f.Id == 1)
                .Select(f => _mappers.Form.Map(f)!)
                .SingleAsync());

        Assert.Contains("ApplyMappers", ex.Message, StringComparison.Ordinal);
    }
}
