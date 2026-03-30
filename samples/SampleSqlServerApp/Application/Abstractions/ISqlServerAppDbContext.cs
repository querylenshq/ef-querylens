using Microsoft.EntityFrameworkCore;
using SampleSqlServerApp.Domain.Entities;
using TypeEntity = SampleSqlServerApp.Domain.Entities.Type;

namespace SampleSqlServerApp.Application.Abstractions;

public interface ISqlServerAppDbContext
{
    DbSet<Customer> Customers { get;  }
    DbSet<Order> Orders { get;  }
    DbSet<TypeEntity> Types { get; }
}
