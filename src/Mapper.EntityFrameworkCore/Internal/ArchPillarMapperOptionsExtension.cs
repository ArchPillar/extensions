using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// <see cref="IDbContextOptionsExtension"/> that registers the
/// <see cref="EnumMapperTranslatorPlugin"/> with EF Core's service provider.
/// </summary>
internal sealed class ArchPillarMapperOptionsExtension(EnumMappingStore store) : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info
        => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton(store);
        services.AddScoped<IMethodCallTranslatorPlugin, EnumMapperTranslatorPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
        // No validation needed.
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using ArchPillarMapper ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["ArchPillar:Mapper:EfCore"] = "1";
        }
    }
}
