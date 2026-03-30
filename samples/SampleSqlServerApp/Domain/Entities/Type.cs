namespace SampleSqlServerApp.Domain.Entities;

public sealed class Type
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}