namespace SampleSqliteApp.Domain.Entities;

public sealed class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
