using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArchPillar.Extensions.Models.EntityFrameworkCore.Tests;

public sealed class IdConventionTests
{
    [Fact]
    public void WithConvention_AllIdProperties_HaveConverterAndComparer()
    {
        DbContextOptions<IdTestDbContext> options = new DbContextOptionsBuilder<IdTestDbContext>()
            .UseNpgsql("Host=localhost")
            .UseArchPillarTypedIds()
            .Options;

        using var db = new IdTestDbContext(options);
        IModel model = db.Model;

        IEntityType userEntity = model.FindEntityType(typeof(UserEntity))!;

        IProperty idProp = userEntity.FindProperty(nameof(UserEntity.Id))!;
        Assert.NotNull(idProp.GetValueConverter());
        Assert.NotNull(idProp.GetValueComparer());

        IProperty latestOrderIdProp = userEntity.FindProperty(nameof(UserEntity.LatestOrderId))!;
        Assert.NotNull(latestOrderIdProp.GetValueConverter());
        Assert.NotNull(latestOrderIdProp.GetValueComparer());
    }

    [Fact]
    public void WithoutConvention_ModelBuild_ThrowsBecauseIdTypeIsNotMapped()
    {
        DbContextOptions<IdNoConventionDbContext> options =
            new DbContextOptionsBuilder<IdNoConventionDbContext>()
                .UseNpgsql("Host=localhost")
                .Options;

        using var db = new IdNoConventionDbContext(options);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _ = db.Model);
        Assert.Contains("Id<UserTag>", ex.Message, StringComparison.Ordinal);
        Assert.Contains("could not be mapped", ex.Message, StringComparison.Ordinal);
    }
}
