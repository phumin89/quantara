using Microsoft.Data.SqlClient;

namespace Portfolio.Checks;

public static class CryptoSqlTestDatabaseChecks
{
    public static void Run()
    {
        const string server = "verified-test-host";
        const string database = "QuantaraFeatureChecksCrypto20261022";
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
        };
        _ = CryptoSqlTestDatabase.Validate(
            settings.ConnectionString,
            server,
            database,
            CryptoSqlTestDatabase.WriteConsent
        );
        settings.DataSource = "(localdb)\\QuantaraVerifyCrypto20261021T085414";
        settings.InitialCatalog = "QuantaraFeatureChecksCrypto20261021T085414";
        _ = CryptoSqlTestDatabase.Validate(
            settings.ConnectionString,
            settings.DataSource,
            settings.InitialCatalog,
            CryptoSqlTestDatabase.WriteConsent
        );
        settings.DataSource = server;
        settings.InitialCatalog = database;

        Reject(settings.ConnectionString, server, database, null);
        Reject(settings.ConnectionString, "other-host", database, CryptoSqlTestDatabase.WriteConsent);
        Reject(settings.ConnectionString, server, database + "Other", CryptoSqlTestDatabase.WriteConsent);
        foreach (
            var forbidden in new[]
            {
                "master",
                "Quantara",
                "QuantaraTeam",
                "QuantaraMarketData",
                "QuantaraFeatureChecksCryptoProduction",
                database + ";USE Quantara",
                "QuantaraFeatureChecksCrypto20260230",
            }
        )
        {
            settings.InitialCatalog = forbidden;
            Reject(settings.ConnectionString, server, forbidden, CryptoSqlTestDatabase.WriteConsent);
        }

        settings.InitialCatalog = database;
        settings.FailoverPartner = "other-host";
        Reject(settings.ConnectionString, server, database, CryptoSqlTestDatabase.WriteConsent);
        settings.FailoverPartner = "";
        settings.AttachDBFilename = "not-used.mdf";
        Reject(settings.ConnectionString, server, database, CryptoSqlTestDatabase.WriteConsent);
        Reject("Invalid SQL option=synthetic", server, database, CryptoSqlTestDatabase.WriteConsent);
        Console.WriteLine(
            "Crypto SQL configuration checks passed: exact target/opt-in, date-named test catalog, production and alternate target rejection; no SQL/network calls."
        );
    }

    private static void Reject(string connection, string server, string database, string? consent)
    {
        try
        {
            _ = CryptoSqlTestDatabase.Validate(connection, server, database, consent);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Unsafe SQL test configuration was accepted.");
    }
}
