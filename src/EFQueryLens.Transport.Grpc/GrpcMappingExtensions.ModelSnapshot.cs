using System.Linq;

namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static partial class GrpcMappingExtensions
{
    public static ModelSnapshot ToProto(this Contracts.ModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new ModelSnapshot
        {
            DbContextType = snapshot.DbContextType ?? string.Empty,
        };

        proto.DbSetProperties.AddRange(snapshot.DbSetProperties ?? []);
        proto.Entities.AddRange(snapshot.Entities.Select(ToProto));
        return proto;
    }

    public static Contracts.ModelSnapshot ToDomain(this ModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Contracts.ModelSnapshot
        {
            DbContextType = snapshot.DbContextType,
            DbSetProperties = snapshot.DbSetProperties.ToArray(),
            Entities = snapshot.Entities.Select(ToDomain).ToArray(),
        };
    }

    public static EntitySnapshot ToProto(this Contracts.EntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new EntitySnapshot
        {
            ClrType = snapshot.ClrType ?? string.Empty,
            TableName = snapshot.TableName ?? string.Empty,
        };

        proto.Properties.AddRange(snapshot.Properties.Select(ToProto));
        proto.Navigations.AddRange(snapshot.Navigations.Select(ToProto));
        proto.Indexes.AddRange(snapshot.Indexes.Select(ToProto));
        return proto;
    }

    public static Contracts.EntitySnapshot ToDomain(this EntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Contracts.EntitySnapshot
        {
            ClrType = snapshot.ClrType,
            TableName = snapshot.TableName,
            Properties = snapshot.Properties.Select(ToDomain).ToArray(),
            Navigations = snapshot.Navigations.Select(ToDomain).ToArray(),
            Indexes = snapshot.Indexes.Select(ToDomain).ToArray(),
        };
    }

    public static PropertySnapshot ToProto(this Contracts.PropertySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PropertySnapshot
        {
            Name = snapshot.Name ?? string.Empty,
            ClrType = snapshot.ClrType ?? string.Empty,
            ColumnName = snapshot.ColumnName ?? string.Empty,
            IsKey = snapshot.IsKey,
            IsNullable = snapshot.IsNullable,
        };
    }

    public static Contracts.PropertySnapshot ToDomain(this PropertySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Contracts.PropertySnapshot
        {
            Name = snapshot.Name,
            ClrType = snapshot.ClrType,
            ColumnName = snapshot.ColumnName,
            IsKey = snapshot.IsKey,
            IsNullable = snapshot.IsNullable,
        };
    }

    public static NavigationSnapshot ToProto(this Contracts.NavigationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new NavigationSnapshot
        {
            Name = snapshot.Name ?? string.Empty,
            TargetEntity = snapshot.TargetEntity ?? string.Empty,
            IsCollection = snapshot.IsCollection,
        };

        if (snapshot.ForeignKey is not null)
        {
            proto.ForeignKey = snapshot.ForeignKey;
        }

        return proto;
    }

    public static Contracts.NavigationSnapshot ToDomain(this NavigationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Contracts.NavigationSnapshot
        {
            Name = snapshot.Name,
            TargetEntity = snapshot.TargetEntity,
            IsCollection = snapshot.IsCollection,
            ForeignKey = snapshot.HasForeignKey ? snapshot.ForeignKey : null,
        };
    }

    public static IndexSnapshot ToProto(this Contracts.IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new IndexSnapshot
        {
            IsUnique = snapshot.IsUnique,
        };

        proto.Columns.AddRange(snapshot.Columns ?? []);

        if (snapshot.Name is not null)
        {
            proto.Name = snapshot.Name;
        }

        return proto;
    }

    public static Contracts.IndexSnapshot ToDomain(this IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Contracts.IndexSnapshot
        {
            Columns = snapshot.Columns.ToArray(),
            IsUnique = snapshot.IsUnique,
            Name = snapshot.HasName ? snapshot.Name : null,
        };
    }
}
