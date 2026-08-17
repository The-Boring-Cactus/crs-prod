using Newtonsoft.Json;
using Npgsql;
using Server.Core;

namespace Server.Tests;

/// <summary>
/// Points DatabasePersistence (which reads its connection info from a config.json next to the
/// running assembly via SetupConfig.Load()) at a dedicated Postgres database, then creates the
/// schema exactly the way a real deployment would: GetCreateTablesSQL() once (mirrors the setup
/// wizard) followed by RunMigrations() (mirrors every server startup).
///
/// Requires a local Postgres reachable at localhost:5432 (see docker/README or run
/// `docker run -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres`). Tests are skipped with a
/// clear reason if it isn't reachable, rather than failing with a raw connection exception.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string AdminConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
    private const string TestDatabaseName = "crs_test";

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        var adminCs = Environment.GetEnvironmentVariable("CRS_TEST_PG_ADMIN_CS") ?? AdminConnectionString;

        try
        {
            await using var admin = new NpgsqlConnection(adminCs);
            await admin.OpenAsync();

            var exists = await new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", admin)
                { Parameters = { new NpgsqlParameter("n", TestDatabaseName) } }
                .ExecuteScalarAsync();
            if (exists == null)
            {
                await new NpgsqlCommand($"CREATE DATABASE \"{TestDatabaseName}\"", admin).ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            SkipReason = $"No local Postgres reachable at localhost:5432 for integration tests ({ex.Message}). " +
                          "Set CRS_TEST_PG_ADMIN_CS to point at one, or start one (see docker/).";
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(adminCs) { Database = TestDatabaseName };

        var config = new SetupConfig
        {
            IsConfigured = true,
            Database = new DatabaseConfig
            {
                Type = "postgresql",
                Host = builder.Host!,
                Port = builder.Port,
                DatabaseName = TestDatabaseName,
                Username = builder.Username!,
                Password = builder.Password!
            }
        };

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));

        await using (var conn = new NpgsqlConnection(builder.ConnectionString))
        {
            await conn.OpenAsync();
            foreach (var stmt in config.Database.GetCreateTablesSQL().Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = stmt.Trim();
                if (trimmed.Length == 0) continue;
                await new NpgsqlCommand(trimmed, conn).ExecuteNonQueryAsync();
            }
        }

        // Mirrors Program.cs's startup call — brings the schema the rest of the way up
        // (ResourceGrants, Projects, ProjectId columns, ...) exactly as production does.
        DatabasePersistence.RunMigrations();

        // Start every test run from a clean slate: tests use randomly generated ids so they
        // don't collide with each other, but a fixed username ("alice", used to assert on
        // grant listings) collides with itself across separate `dotnet test` invocations
        // against the same persistent database, since Users.Username is UNIQUE.
        await using var cleanup = new NpgsqlConnection(builder.ConnectionString);
        await cleanup.OpenAsync();
        const string tables = "ResourceGrants, PasswordResetTokens, DataModels, Variables, " +
            "SqlScripts, CodeScripts, DatabaseConnections, Datasets, Reports, Dashboards, Projects, Users";
        await new NpgsqlCommand($"TRUNCATE TABLE {tables} RESTART IDENTITY CASCADE", cleanup).ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Inserts a minimal Users row (FK target for owned rows) and returns its id.</summary>
    public async Task<string> CreateUserAsync(string? username = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CRS_TEST_PG_ADMIN_CS") ?? AdminConnectionString)
        { Database = TestDatabaseName };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        var id = Guid.NewGuid();
        var name = username ?? $"test_{id:N}";
        await new NpgsqlCommand(
            @"INSERT INTO Users (Id, Username, FullName, Email, PasswordHash, Salt, Roles)
              VALUES (@Id, @Username, @FullName, @Email, @PasswordHash, @Salt, @Roles)", conn)
        {
            Parameters =
            {
                new NpgsqlParameter("Id", id),
                new NpgsqlParameter("Username", name),
                new NpgsqlParameter("FullName", name),
                new NpgsqlParameter("Email", $"{name}@example.com"),
                new NpgsqlParameter("PasswordHash", "x"),
                new NpgsqlParameter("Salt", "x"),
                new NpgsqlParameter("Roles", "user")
            }
        }.ExecuteNonQueryAsync();

        return id.ToString();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
