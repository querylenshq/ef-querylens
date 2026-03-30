using Microsoft.EntityFrameworkCore;
using SampleSqlServerApp.Domain.Entities;

namespace SampleSqlServerApp.Application.Abstractions;

public interface ISqlServerAppDbContext
{
    DbSet<Customer> Customers { get;  }
    DbSet<Order> Orders { get;  }
}
