using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArchPillar.Extensions.Identifiers.EntityFrameworkCore.Internal;

internal sealed class IdConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (IConventionProperty property in entityType.GetProperties())
            {
                Type? idType = GetIdType(property.ClrType);
                if (idType is null)
                {
                    continue;
                }

                if (property.GetValueConverter() is not null)
                {
                    continue;
                }

                Type typeArg = idType.GetGenericArguments()[0];

                var converter = (ValueConverter)Activator.CreateInstance(
                    typeof(IdValueConverter<>).MakeGenericType(typeArg))!;
                var comparer = (ValueComparer)Activator.CreateInstance(
                    typeof(IdValueComparer<>).MakeGenericType(typeArg))!;

                property.Builder.HasConversion(converter)?.HasValueComparer(comparer);
            }
        }
    }

    internal static Type? GetIdType(Type clrType)
    {
        if (clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Id<>))
        {
            return clrType;
        }

        Type? underlying = Nullable.GetUnderlyingType(clrType);
        if (underlying?.IsGenericType == true
            && underlying.GetGenericTypeDefinition() == typeof(Id<>))
        {
            return underlying;
        }

        return null;
    }
}
