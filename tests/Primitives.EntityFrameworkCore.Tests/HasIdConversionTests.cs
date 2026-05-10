using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArchPillar.Extensions.Primitives.EntityFrameworkCore.Tests;

public sealed class HasIdConversionTests
{
    [Fact]
    public void ManualConversion_IdProperties_HaveConverterAndComparer()
    {
        DbContextOptions<IdManualDbContext> options =
            new DbContextOptionsBuilder<IdManualDbContext>()
                .UseNpgsql("Host=localhost")
                .Options;

        using var db = new IdManualDbContext(options);
        IModel model = db.Model;

        IEntityType userEntity = model.FindEntityType(typeof(UserEntity))!;

        IProperty idProp = userEntity.FindProperty(nameof(UserEntity.Id))!;
        Assert.NotNull(idProp.GetValueConverter());
        Assert.NotNull(idProp.GetValueComparer());
    }
}
