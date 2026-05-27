using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.TypeMappings;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql;

public sealed class GuidUuidMappingTests
{
    [Fact]
    public void GenerateSqlLiteral_EmitsUuidCast()
    {
        RelationalTypeMapping mapping = GuidUuidMapping.Default;
        var guid = Guid.Parse("57afda40-0000-0000-0000-000000000000");

        var literal = mapping.GenerateSqlLiteral(guid);

        Assert.Equal("'57afda40-0000-0000-0000-000000000000'::uuid", literal);
    }

    [Fact]
    public void StoreType_IsUuid()
    {
        Assert.Equal("uuid", GuidUuidMapping.Default.StoreType);
    }

    [Fact]
    public void Plugin_ReturnsMapping_ForGuid()
    {
        var plugin = new GuidUuidTypeMappingSourcePlugin();
        var info = new RelationalTypeMappingInfo(typeof(Guid));

        RelationalTypeMapping? mapping = plugin.FindMapping(in info);

        Assert.NotNull(mapping);
        Assert.Equal("uuid", mapping.StoreType);
    }

    [Fact]
    public void Plugin_IgnoresNonGuid()
    {
        var plugin = new GuidUuidTypeMappingSourcePlugin();
        var info = new RelationalTypeMappingInfo(typeof(string));

        Assert.Null(plugin.FindMapping(in info));
    }
}
