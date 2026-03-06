#pragma warning disable CS8765 // Nullability of parameter type doesn't match overridden member

using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace QueryLens.Core.Scripting;

/// <summary>
/// A <see cref="RelationalConnection"/> subclass that prevents EF Core providers
/// from opening real TCP connections during offline <c>ToQueryString()</c>.
///
/// By extending <see cref="RelationalConnection"/> instead of implementing
/// <see cref="IRelationalConnection"/> directly, we inherit all the plumbing
/// (transaction management, command timeout, etc.) and only override the
/// connection lifecycle methods to be no-ops.
///
/// Registered via <c>ReplaceService</c> in <see cref="QueryEvaluator"/>.
/// Works for all providers (MySQL, PostgreSQL, SQL Server, etc.).
/// </summary>
public sealed class NoOpRelationalConnection(RelationalConnectionDependencies dependencies)
    : RelationalConnection(dependencies)
{
    protected override DbConnection CreateDbConnection()
        => CreateOfflineDbConnection();

    internal static DbConnection CreateOfflineDbConnection() => new NoOpDbConnection();

    public override bool Open(bool errorsExpected = false) => true;

    public override Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
        => Task.FromResult(true);

    public override bool Close() => true;

    public override Task<bool> CloseAsync() => Task.FromResult(true);

    /// <summary>
    /// A minimal <see cref="DbConnection"/> stub that satisfies any provider's
    /// connection property access, but never opens a real connection.
    /// </summary>
    private sealed class NoOpDbConnection : DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "offline";
        public override string DataSource => "localhost";
        public override string ServerVersion => "8.0.0";
        public override ConnectionState State => ConnectionState.Open; // pretend we're open

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Open()
        {
        }

        public override void Close()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new InvalidOperationException("Offline mode: transactions not supported.");

        protected override DbCommand CreateDbCommand() => new NoOpDbCommand();
    }

    /// <summary>
    /// Minimal <see cref="DbCommand"/> stub so that property access chains don't throw.
    /// </summary>
    private sealed class NoOpDbCommand : DbCommand
    {
        private readonly NoOpDbParameterCollection _parameters = new();

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            RecordCurrentCommand();
            return 0;
        }

        public override object? ExecuteScalar()
        {
            RecordCurrentCommand();
            return null;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new NoOpDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            RecordCurrentCommand();
            return new FakeDbDataReader();
        }

        private void RecordCurrentCommand()
        {
            SqlCaptureScope.Record(CommandText, _parameters.Items);
        }
    }

    private sealed class NoOpDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public IReadOnlyList<DbParameter> Items => _items;

        public override int Count => _items.Count;
        public override object SyncRoot => ((ICollection)_items).SyncRoot!;
        public override bool IsFixedSize => false;
        public override bool IsReadOnly => false;
        public override bool IsSynchronized => false;

        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => _items.Clear();

        public override bool Contains(object value) =>
            value is DbParameter dbParameter && _items.Contains(dbParameter);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(object value) =>
            value is DbParameter dbParameter ? _items.IndexOf(dbParameter) : -1;

        public override int IndexOf(string parameterName) =>
            _items.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.Ordinal));

        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);

        public override void Remove(object value)
        {
            if (value is DbParameter dbParameter)
            {
                _items.Remove(dbParameter);
            }
        }

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => _items[index];

        protected override DbParameter GetParameter(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                throw new IndexOutOfRangeException(parameterName);
            }

            return _items[index];
        }

        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _items[index] = value;
                return;
            }

            _items.Add(value);
        }
    }

    private sealed class NoOpDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
