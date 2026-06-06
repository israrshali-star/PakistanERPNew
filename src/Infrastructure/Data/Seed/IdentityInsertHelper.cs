using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PakistanAccountingERP.Infrastructure.Data.Seed;

internal static class IdentityInsertHelper
{
    /// <summary>
    /// Saves seed rows with explicit key values on the same SQL connection.
    /// IDENTITY_INSERT is session-scoped — must run inside one transaction/connection.
    /// </summary>
    public static async Task SaveWithExplicitKeysAsync(
        AppDbContext context,
        string tableName,
        Func<Task> seedAction,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var hasIdentity = await HasIdentityColumnAsync(context, tableName, cancellationToken);

            if (hasIdentity)
            {
                await context.Database.ExecuteSqlRawAsync(
                    GetIdentityInsertSql(tableName, on: true), cancellationToken);
            }

            await seedAction();
            await context.SaveChangesAsync(cancellationToken);

            if (hasIdentity)
            {
                await context.Database.ExecuteSqlRawAsync(
                    GetIdentityInsertSql(tableName, on: false), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string GetIdentityInsertSql(string tableName, bool on) =>
        tableName switch
        {
            "Provinces" => on ? "SET IDENTITY_INSERT [dbo].[Provinces] ON" : "SET IDENTITY_INSERT [dbo].[Provinces] OFF",
            "UnitsOfMeasure" => on ? "SET IDENTITY_INSERT [dbo].[UnitsOfMeasure] ON" : "SET IDENTITY_INSERT [dbo].[UnitsOfMeasure] OFF",
            "AccountTypes" => on ? "SET IDENTITY_INSERT [dbo].[AccountTypes] ON" : "SET IDENTITY_INSERT [dbo].[AccountTypes] OFF",
            "SubAccountTypes" => on ? "SET IDENTITY_INSERT [dbo].[SubAccountTypes] ON" : "SET IDENTITY_INSERT [dbo].[SubAccountTypes] OFF",
            "ScenarioTypes" => on ? "SET IDENTITY_INSERT [dbo].[ScenarioTypes] ON" : "SET IDENTITY_INSERT [dbo].[ScenarioTypes] OFF",
            "Permissions" => on ? "SET IDENTITY_INSERT [dbo].[Permissions] ON" : "SET IDENTITY_INSERT [dbo].[Permissions] OFF",
            _ => throw new ArgumentException($"Unsupported seed table: {tableName}", nameof(tableName))
        };

    private static async Task<bool> HasIdentityColumnAsync(
        AppDbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            "SELECT COUNT(1) FROM sys.identity_columns WHERE OBJECT_NAME(object_id) = @tableName";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }
}
