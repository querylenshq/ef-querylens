using EntityFrameworkCore.Projectables;
using SampleMySqlApp.Domain.Entities;

namespace SampleMySqlApp.Domain.Extensions;

public static class ProductSoftDeleteExtensions
{
    [Projectable]
    public static bool IsNotDeleted(this Product product)
        => product.Stock > 0;
}
