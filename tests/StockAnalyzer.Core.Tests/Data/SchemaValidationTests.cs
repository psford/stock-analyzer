namespace StockAnalyzer.Core.Tests.Data;

using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data;
using Xunit;

/// <summary>
/// Validates that EF Core entity CLR types match the actual SQL Server column types.
/// Catches int/bigint mismatches and similar drift that in-memory tests cannot detect.
/// Requires local SQL Express with StockAnalyzer database.
/// </summary>
public class SchemaValidationTests
{
    private const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";

    /// <summary>
    /// Maps SQL Server data types to their expected CLR types.
    /// Only maps types where a mismatch would cause a runtime cast exception.
    /// </summary>
    private static readonly Dictionary<string, HashSet<Type>> SqlToClrMap = new()
    {
        ["int"] = new() { typeof(int), typeof(int?) },
        ["bigint"] = new() { typeof(long), typeof(long?) },
        ["smallint"] = new() { typeof(short), typeof(short?) },
        ["tinyint"] = new() { typeof(byte), typeof(byte?) },
        ["bit"] = new() { typeof(bool), typeof(bool?) },
        ["decimal"] = new() { typeof(decimal), typeof(decimal?) },
        ["numeric"] = new() { typeof(decimal), typeof(decimal?) },
        ["float"] = new() { typeof(double), typeof(double?) },
        ["real"] = new() { typeof(float), typeof(float?) },
        ["date"] = new() { typeof(DateTime), typeof(DateTime?), typeof(DateOnly), typeof(DateOnly?) },
        ["datetime"] = new() { typeof(DateTime), typeof(DateTime?) },
        ["datetime2"] = new() { typeof(DateTime), typeof(DateTime?) },
        ["nvarchar"] = new() { typeof(string) },
        ["varchar"] = new() { typeof(string) },
        ["varbinary"] = new() { typeof(byte[]) },
        ["uniqueidentifier"] = new() { typeof(Guid), typeof(Guid?) },
    };

    [Fact]
    [Trait("Category", "Integration")]
    public void EntityClrTypes_MatchSqlServerColumnTypes()
    {
        // Skip when SQL Express is not available (e.g., GitHub Actions Linux runner)
        if (!IsSqlServerAvailable())
        {
            return;
        }

        // Arrange: Build the EF Core model to get table/column mappings
        var optionsBuilder = new DbContextOptionsBuilder<StockAnalyzerDbContext>();
        optionsBuilder.UseSqlServer(ConnectionString);
        using var context = new StockAnalyzerDbContext(optionsBuilder.Options);

        var model = context.Model;
        var mismatches = new List<string>();

        // Query all column types from the actual database
        var dbColumns = GetDatabaseColumns();

        // Act: For each entity type in the model, compare CLR type to SQL type
        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema() ?? "dbo";

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName();
                var clrType = property.ClrType;

                var key = $"{schema}.{tableName}.{columnName}";
                if (!dbColumns.TryGetValue(key, out var sqlType))
                    continue; // Column not in DB yet (pending migration) — skip

                if (!SqlToClrMap.TryGetValue(sqlType, out var allowedTypes))
                    continue; // Unmapped SQL type — skip

                if (!allowedTypes.Contains(clrType))
                {
                    mismatches.Add(
                        $"{key}: SQL type '{sqlType}' requires CLR type in [{string.Join(", ", allowedTypes.Select(t => t.Name))}] " +
                        $"but entity has '{clrType.Name}'");
                }
            }
        }

        // Assert
        Assert.True(mismatches.Count == 0,
            $"Entity/database type mismatches found ({mismatches.Count}):\n" +
            string.Join("\n", mismatches));
    }

    private static bool IsSqlServerAvailable()
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> GetDatabaseColumns()
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);
            var dataType = reader.GetString(3);
            columns[$"{schema}.{table}.{column}"] = dataType;
        }

        return columns;
    }
}
