namespace ArchPillar.Extensions.Mapper.Generators.Tests;

/// <summary>
/// Partial MapperContext subclass — the source generator should detect this and
/// emit AOT-compatible mapping methods in a generated partial class file.
/// </summary>
public partial class AotTestMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public EnumMapper<EmployeeLevel, EmployeeLevelDto> EmployeeLevelMapper { get; }

    public Mapper<Project, ProjectDto> Project { get; }
    public Mapper<Department, DepartmentDto> Department { get; }
    public Mapper<Employee, EmployeeDto> Employee { get; }

    public AotTestMappers()
    {
        EmployeeLevelMapper = CreateEnumMapper<EmployeeLevel, EmployeeLevelDto>(MapLevel);

        Project = CreateMapper<Project, ProjectDto>(src => new ProjectDto
        {
            Id = src.Id,
            Title = src.Title,
            Budget = src.Budget,
        });

        Department = CreateMapper<Department, DepartmentDto>(src => new DepartmentDto
        {
            Id = src.Id,
            Name = src.Name,
        })
        .Optional(dest => dest.Location, src => src.Location);

        Employee = CreateMapper<Employee, EmployeeDto>(src => new EmployeeDto
        {
            Id = src.Id,
            FullName = src.FirstName + " " + src.LastName,
            Email = src.Email,
            DepartmentName = src.Department.Name,
            Level = EmployeeLevelMapper.Map(src.Level),
            IsOwnManager = src.ManagerId == CurrentUserId,
        })
        .Optional(dest => dest.Projects, src => src.Projects.Project(Project).ToList());

        OnAotInitialize();
    }

    private static EmployeeLevelDto MapLevel(EmployeeLevel level) => level switch
    {
        EmployeeLevel.Junior => EmployeeLevelDto.Junior,
        EmployeeLevel.Mid => EmployeeLevelDto.Mid,
        EmployeeLevel.Senior => EmployeeLevelDto.Senior,
        EmployeeLevel.Lead => EmployeeLevelDto.Lead,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
