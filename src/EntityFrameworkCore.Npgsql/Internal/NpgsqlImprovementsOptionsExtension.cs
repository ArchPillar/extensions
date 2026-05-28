using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;
using ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.TypeMappings;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal;

internal sealed class NpgsqlImprovementsOptionsExtension : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info
        => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddScoped<IRelationalTypeMappingSourcePlugin, GuidUuidTypeMappingSourcePlugin>();
        services.AddScoped<IMethodCallTranslatorPlugin, JsonbBuildObjectMethodCallTranslatorPlugin>();
        services.AddSingleton<IEvaluatableExpressionFilterPlugin, JsonbEvaluatableExpressionFilterPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using ArchPillarNpgsqlImprovements ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["ArchPillar:Npgsql:Improvements"] = "1";
    }
}
