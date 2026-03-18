using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ArchPillar.Extensions.Mapper.Generators;

internal enum MappingKind { Required, Optional, Ignored }

internal sealed class MapperContextInfo
{
    public MapperContextInfo(string? ns, string className, Accessibility accessibility)
    {
        Namespace = ns;
        ClassName = className;
        Accessibility = accessibility;
    }

    public string? Namespace { get; }
    public string ClassName { get; }
    public Accessibility Accessibility { get; }
    public List<MapperInfo> Mappers { get; } = new List<MapperInfo>();
    public List<EnumMapperInfo> EnumMappers { get; } = new List<EnumMapperInfo>();
    public List<VariableInfo> Variables { get; } = new List<VariableInfo>();
}

internal sealed class MapperInfo
{
    public MapperInfo(string propertyName, string sourceType, string destType)
    {
        PropertyName = propertyName;
        SourceType = sourceType;
        DestType = destType;
    }

    public string PropertyName { get; }
    public string SourceType { get; }
    public string DestType { get; }
    public string? SourceParameterName { get; set; }
    public List<PropertyMappingInfo> Mappings { get; } = new List<PropertyMappingInfo>();
}

internal sealed class PropertyMappingInfo
{
    public PropertyMappingInfo(
        string destinationProperty,
        string? sourceExpression,
        MappingKind kind,
        string? lambdaParameterName = null)
    {
        DestinationProperty = destinationProperty;
        SourceExpression = sourceExpression;
        Kind = kind;
        LambdaParameterName = lambdaParameterName;
    }

    public string DestinationProperty { get; }
    public string? SourceExpression { get; }
    public MappingKind Kind { get; }

    /// <summary>
    /// The parameter name used in the source lambda (e.g., "src", "x").
    /// Null for member-init bindings (they use the main lambda parameter).
    /// Used to rename the parameter to match the generated method's parameter.
    /// </summary>
    public string? LambdaParameterName { get; }
}

internal sealed class EnumMapperInfo
{
    public EnumMapperInfo(string propertyName, string sourceType, string destType)
    {
        PropertyName = propertyName;
        SourceType = sourceType;
        DestType = destType;
    }

    public string PropertyName { get; }
    public string SourceType { get; }
    public string DestType { get; }
}

internal sealed class VariableInfo
{
    public VariableInfo(string name, string typeName)
    {
        Name = name;
        TypeName = typeName;
    }

    public string Name { get; }
    public string TypeName { get; }
}
