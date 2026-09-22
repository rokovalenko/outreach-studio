using Microsoft.EntityFrameworkCore;
using OutreachStudio.Data;
using Testcontainers.PostgreSql;

namespace OutreachStudio.Worker.Tests;

/// <summary>
/// One container for the whole run. Tests share it and stay apart by using their own campaign and
/// their own users, which is also how two campaigns behave in production.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public OutreachDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OutreachDbContext>().UseNpgsql(ConnectionString);
        OutreachDbContext.Configure(options);
        return new(options.Options);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
