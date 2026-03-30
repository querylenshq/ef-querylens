using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SampleSqlServerApp.Application.Abstractions;
using SampleSqlServerApp.Domain.Entities;
using SampleSqlServerApp.Domain.Enums;
using SampleSqlServerApp.Infrastructure.Persistence;

namespace SampleSqlServerApp.Application.Reporting;

/// <summary>
/// Read-only reporting queries that run against <see cref="SqlServerReportingDbContext"/>.
/// This service deliberately accepts the concrete context rather than an interface so that
/// QueryLens can unambiguously resolve it to <c>SqlServerReportingDbContext</c> when
/// hovering over LINQ expressions — demonstrating multi-DbContext support.
/// </summary>
public sealed class CustomerReportService
{
    private readonly SqlServerReportingDbContext _db;

    public CustomerReportService(SqlServerReportingDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns a projection of all active, non-deleted customers.</summary>
    public IQueryable<CustomerSummaryDto> GetActiveCustomerSummariesQuery()
    {

        var p = _db.CustomerDirectory
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.Name);

        return _db.CustomerDirectory
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSummaryDto(
                c.CustomerId,
                c.Name,
                c.Email,
                c.CreatedUtc));
    }

    /// <summary>Returns per-month customer registration counts for the given year.</summary>
    public IQueryable<CustomerCountByMonthDto> GetCustomerCountByMonthQuery(int year)
    {
        return _db.CustomerDirectory
            .Where(c => !c.IsDeleted && c.CreatedUtc.Year == year)
            .GroupBy(c => c.CreatedUtc.Month)
            .Select(g => new CustomerCountByMonthDto(g.Key, g.Count()));
    }

    /// <summary>Full-text style directory search on name or email.</summary>
    public IQueryable<CustomerSummaryDto> SearchCustomerDirectoryQuery(string term)
    {
        return _db.CustomerDirectory
            .Where(c => !c.IsDeleted)
            .Where(c => c.Name.Contains(term) || c.Email.StartsWith(term))
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSummaryDto(
                c.CustomerId,
                c.Name,
                c.Email,
                c.CreatedUtc));
    }
}

public sealed record CustomerCountByMonthDto(int Month, int Count);
