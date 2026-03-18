namespace ArchPillar.Extensions.Mapper.Generators.Tests;

// ---------------------------------------------------------------------------
// Source models
// ---------------------------------------------------------------------------

public class Employee
{
    public required int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required Department Department { get; set; }
    public required EmployeeLevel Level { get; set; }
    public int ManagerId { get; set; }
    public List<Project> Projects { get; set; } = [];
}

public class Department
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public string? Location { get; set; }
}

public class Project
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public required decimal Budget { get; set; }
}

public enum EmployeeLevel { Junior, Mid, Senior, Lead }

// ---------------------------------------------------------------------------
// Destination models
// ---------------------------------------------------------------------------

public class EmployeeDto
{
    public required int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string DepartmentName { get; set; }
    public required EmployeeLevelDto Level { get; set; }
    public required bool IsOwnManager { get; set; }
    public List<ProjectDto>? Projects { get; set; }
}

public class ProjectDto
{
    public required int Id { get; set; }
    public required string Title { get; set; }
    public required decimal Budget { get; set; }
}

public class DepartmentDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public string? Location { get; set; }
}

public enum EmployeeLevelDto { Junior, Mid, Senior, Lead }
