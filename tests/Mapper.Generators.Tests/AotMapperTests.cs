namespace ArchPillar.Extensions.Mapper.Generators.Tests;

/// <summary>
/// Verifies that the AOT source-generated mapping methods produce
/// identical results to the expression-compiled mapper.
/// </summary>
public sealed class AotMapperTests
{
    private readonly AotTestMappers _mapper = new();

    // -------------------------------------------------------------------------
    // Simple flat mapping
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Project_MapsAllProperties()
    {
        var source = new Project { Id = 1, Title = "Alpha", Budget = 100_000m };

        ProjectDto? result = _mapper.Project.Map(source);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Alpha", result.Title);
        Assert.Equal(100_000m, result.Budget);
    }

    [Fact]
    public void Map_NullSource_ReturnsNull()
    {
        ProjectDto? result = _mapper.Project.Map((Project?)null);

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // String concatenation
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Employee_ConcatenatesFullName()
    {
        Employee source = CreateEmployee();

        EmployeeDto? result = _mapper.Employee.Map(source);

        Assert.NotNull(result);
        Assert.Equal("Jane Doe", result.FullName);
    }

    // -------------------------------------------------------------------------
    // Nested property access
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Employee_AccessesDepartmentName()
    {
        Employee source = CreateEmployee();

        EmployeeDto? result = _mapper.Employee.Map(source);

        Assert.NotNull(result);
        Assert.Equal("Engineering", result.DepartmentName);
    }

    // -------------------------------------------------------------------------
    // Enum mapper (should still work through EnumMapper.Map())
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Employee_MapsEnumLevel()
    {
        Employee source = CreateEmployee(level: EmployeeLevel.Senior);

        EmployeeDto? result = _mapper.Employee.Map(source);

        Assert.NotNull(result);
        Assert.Equal(EmployeeLevelDto.Senior, result.Level);
    }

    // -------------------------------------------------------------------------
    // Variable binding
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Employee_VariableDefault_IsOwnManagerFalse()
    {
        Employee source = CreateEmployee(managerId: 42);

        EmployeeDto? result = _mapper.Employee.Map(source);

        Assert.NotNull(result);
        Assert.False(result.IsOwnManager);
    }

    [Fact]
    public void Map_Employee_VariableBound_IsOwnManagerTrue()
    {
        Employee source = CreateEmployee(managerId: 42);

        EmployeeDto? result = _mapper.Employee.Map(source, o => o.Set(_mapper.CurrentUserId, 42));

        Assert.NotNull(result);
        Assert.True(result.IsOwnManager);
    }

    // -------------------------------------------------------------------------
    // Optional property (collection with nested mapper)
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Employee_OptionalProjectsIncluded()
    {
        Employee source = CreateEmployee();
        source.Projects =
        [
            new Project { Id = 10, Title = "ProjectA", Budget = 50_000m },
            new Project { Id = 20, Title = "ProjectB", Budget = 75_000m },
        ];

        // In-memory Map always includes all optionals
        EmployeeDto? result = _mapper.Employee.Map(source);

        Assert.NotNull(result);
        Assert.NotNull(result.Projects);
        Assert.Equal(2, result.Projects.Count);
        Assert.Equal("ProjectA", result.Projects[0].Title);
        Assert.Equal(75_000m, result.Projects[1].Budget);
    }

    // -------------------------------------------------------------------------
    // MapTo — assigns onto existing object
    // -------------------------------------------------------------------------

    [Fact]
    public void MapTo_Project_AssignsProperties()
    {
        var source = new Project { Id = 5, Title = "Beta", Budget = 200_000m };
        var dest = new ProjectDto { Id = 0, Title = "", Budget = 0 };

        _mapper.Project.MapTo(source, dest);

        Assert.Equal(5, dest.Id);
        Assert.Equal("Beta", dest.Title);
        Assert.Equal(200_000m, dest.Budget);
    }

    [Fact]
    public void MapTo_NullSource_LeavesDestinationUnchanged()
    {
        var dest = new ProjectDto { Id = 99, Title = "Unchanged", Budget = 1m };

        _mapper.Project.MapTo(null, dest);

        Assert.Equal(99, dest.Id);
        Assert.Equal("Unchanged", dest.Title);
    }

    // -------------------------------------------------------------------------
    // Optional property on flat mapper
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_Department_IncludesOptionalLocation()
    {
        var source = new Department { Id = 1, Name = "HR", Location = "Building A" };

        DepartmentDto? result = _mapper.Department.Map(source);

        Assert.NotNull(result);
        Assert.Equal("Building A", result.Location);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Employee CreateEmployee(
        EmployeeLevel level = EmployeeLevel.Mid,
        int managerId = 0)
    {
        return new Employee
        {
            Id = 1,
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            Department = new Department { Id = 10, Name = "Engineering" },
            Level = level,
            ManagerId = managerId,
        };
    }
}
