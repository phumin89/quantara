using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Portfolio.Checks;

public static class CryptoSqlTestDatabase
{
    public const string WriteConsent = "isolated-crypto-test-database-only";

    public static async Task<string> Connection(string connectionVariable)
    {
        var configured = Validate(
            Environment.GetEnvironmentVariable(connectionVariable),
            Environment.GetEnvironmentVariable("QUANTARA_SQL_TEST_SERVER"),
            Environment.GetEnvironmentVariable("QUANTARA_SQL_TEST_DATABASE"),
            Environment.GetEnvironmentVariable("QUANTARA_SQL_TEST_ALLOW_WRITES")
        );
        await using var connection = new SqlConnection(configured);
        await connection.OpenAsync();
        await using var marker = new SqlCommand(
            "SELECT DB_NAME(),(SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Quantara.TestFixture');",
            connection
        );
        await using var reader = await marker.ExecuteReaderAsync();
        if (
            !await reader.ReadAsync()
            || reader.GetString(0) != new SqlConnectionStringBuilder(configured).InitialCatalog
            || reader.IsDBNull(1)
            || reader.GetString(1) != "CryptoReadinessOnly"
        )
        {
            throw new InvalidOperationException(
                "The selected database lacks its owner-provisioned Crypto test marker."
            );
        }

        return configured;
    }

    public static string Validate(string? configured, string? server, string? database, string? consent)
    {
        if (
            string.IsNullOrWhiteSpace(configured)
            || string.IsNullOrWhiteSpace(server)
            || string.IsNullOrWhiteSpace(database)
            || consent != WriteConsent
        )
        {
            throw new InvalidOperationException(
                "Explicit test connection, exact server/database and write opt-in are required."
            );
        }

        SqlConnectionStringBuilder settings;
        try
        {
            settings = new SqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Invalid SQL test connection configuration; values omitted.");
        }

        const string prefix = "QuantaraFeatureChecksCrypto";
        if (
            !settings.DataSource.Equals(server, StringComparison.OrdinalIgnoreCase)
            || settings.InitialCatalog != database
            || !database.StartsWith(prefix, StringComparison.Ordinal)
            || !DateTime.TryParseExact(
                database[prefix.Length..],
                ["yyyyMMdd", "yyyyMMdd'T'HHmmss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _
            )
            || settings.AttachDBFilename.Length > 0
            || settings.FailoverPartner.Length > 0
            || settings.UserInstance
            || settings.PersistSecurityInfo
            || settings.ConnectTimeout is 0 or > 30
            || settings.ApplicationIntent != ApplicationIntent.ReadWrite
        )
        {
            throw new InvalidOperationException(
                "SQL tests require the exact opted-in test server and date-named Crypto test database; production and alternate targets are rejected."
            );
        }

        return settings.ConnectionString;
    }
}
