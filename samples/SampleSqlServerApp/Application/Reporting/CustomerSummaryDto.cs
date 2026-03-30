namespace SampleSqlServerApp.Application.Reporting;

public sealed record CustomerSummaryDto(
    Guid CustomerId,
    string Name,
    string Email,
    DateTime CreatedUtc);
