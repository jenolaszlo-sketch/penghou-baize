using Microsoft.Data.Sqlite;

namespace Penghou.Baize.Qualification.Execution;

internal sealed record AuthorizationConsumptionClaim(string AuthorizationId, string AuthorizationRef,
    string ExecutionCorrelationId, DateTimeOffset ClaimedUtc);

// The configured registry is provisioned once and is never implicitly created
// by run-live. A signed authority id pins each authorization to that registry.
internal sealed class AuthorizationConsumptionRegistry
{
    public const string PathEnvironmentVariable = "BAIZE_AUTHORIZATION_REGISTRY_PATH";
    private const int SchemaVersion = 1;
    private readonly string _path;

    private AuthorizationConsumptionRegistry(string path) => _path = path;

    public static AuthorizationConsumptionRegistry OpenConfigured(string expectedAuthorityId)
    {
        var configuredPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ExecutionException("authorization-consumption-store-unavailable");
        return OpenAt(configuredPath, expectedAuthorityId);
    }

    internal static AuthorizationConsumptionRegistry OpenAt(string configuredPath, string expectedAuthorityId)
    {
        try
        {
            var path = Path.GetFullPath(configuredPath);
            if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                throw new ExecutionException("authorization-consumption-store-unavailable");
            var registry = new AuthorizationConsumptionRegistry(path);
            using var connection = registry.OpenConnection();
            if (registry.ReadAuthorityId(connection) != expectedAuthorityId)
                throw new ExecutionException("authorization-consumption-authority-mismatch");
            return registry;
        }
        catch (ExecutionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SqliteException or ArgumentException or NotSupportedException)
        {
            throw new ExecutionException("authorization-consumption-store-unavailable");
        }
    }

    // Administrative provisioning only. Execution opens the existing database
    // in ReadWrite mode and never recreates it after loss or corruption.
    public static string Provision(string path, string? authorityId = null)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new ExecutionException("authorization-consumption-store-already-exists");
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ExecutionException("authorization-consumption-store-unavailable");
        Directory.CreateDirectory(directory);
        var id = authorityId ?? Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(id, "N", out var parsed) || parsed.ToString("N") != id)
            throw new ExecutionException("authorization-consumption-authority-invalid");
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 10
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            Execute(connection, null, "PRAGMA journal_mode=WAL;");
            Execute(connection, null, "PRAGMA synchronous=FULL;");
            Execute(connection, null, "PRAGMA foreign_keys=ON;");
            Execute(connection, null, "PRAGMA busy_timeout=10000;");
            RequirePragma(connection, "PRAGMA journal_mode;", "wal");
            RequirePragma(connection, "PRAGMA synchronous;", "2");
            RequirePragma(connection, "PRAGMA foreign_keys;", "1");
            RequirePragma(connection, "PRAGMA busy_timeout;", "10000");
            using var transaction = connection.BeginTransaction(deferred: false);
            Execute(connection, transaction, "CREATE TABLE registry_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            Execute(connection, transaction, "CREATE TABLE consumed_authorizations ("
                + "authorization_id TEXT PRIMARY KEY, authorization_ref TEXT NOT NULL UNIQUE, "
                + "authorization_digest TEXT NOT NULL, proposal_digest TEXT NOT NULL, plan_hash TEXT NOT NULL, execution_profile_identity TEXT NOT NULL, "
                + "claimed_utc TEXT NOT NULL, execution_correlation_id TEXT NOT NULL UNIQUE, "
                + "claim_status TEXT NOT NULL CHECK (claim_status IN ('claimed','terminal')), terminal_result TEXT NULL);");
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO registry_metadata(key,value) VALUES ('schema_version',$schema),('authority_id',$authority);";
                command.Parameters.AddWithValue("$schema", SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$authority", id);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            SetPrivateUnixPermissions(fullPath);
            return id;
        }
        catch (ExecutionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SqliteException or ArgumentException or NotSupportedException)
        {
            throw new ExecutionException("authorization-consumption-store-provision-failed");
        }
    }

    public AuthorizationConsumptionClaim Claim(LiveAuthorizationDocument authorization, string authorizationDigest,
        string planHash, string executionProfileIdentity, string executionCorrelationId, DateTimeOffset claimedUtc,
        Action? afterCommitForTesting = null)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            EnsureAuthority(connection, transaction, authorization.ConsumptionAuthorityId);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO consumed_authorizations(authorization_id,authorization_ref,"
                    + "authorization_digest,proposal_digest,plan_hash,execution_profile_identity,claimed_utc,execution_correlation_id,"
                    + "claim_status,terminal_result) VALUES ($id,$ref,$digest,$proposal,$plan,$profile,$claimed,$correlation,'claimed',NULL) "
                    + "ON CONFLICT DO NOTHING;";
                command.Parameters.AddWithValue("$id", authorization.AuthorizationId);
                command.Parameters.AddWithValue("$ref", authorization.AuthorizationRef);
                command.Parameters.AddWithValue("$digest", authorizationDigest);
                command.Parameters.AddWithValue("$proposal", authorization.ProposalDigest);
                command.Parameters.AddWithValue("$plan", planHash);
                command.Parameters.AddWithValue("$profile", executionProfileIdentity);
                command.Parameters.AddWithValue("$claimed", claimedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$correlation", executionCorrelationId);
                if (command.ExecuteNonQuery() != 1)
                    throw new ExecutionException("live-authorization-already-consumed");
            }
            transaction.Commit();
            // Tests use this seam to model a lost commit acknowledgement. The
            // caller must not dispatch when the outcome is ambiguous.
            afterCommitForTesting?.Invoke();
            return new AuthorizationConsumptionClaim(authorization.AuthorizationId, authorization.AuthorizationRef,
                executionCorrelationId, claimedUtc.ToUniversalTime());
        }
        catch (ExecutionException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SqliteException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new ExecutionException("authorization-consumption-store-unavailable");
        }
    }

    public void RecordTerminalOutcome(AuthorizationConsumptionClaim claim, int exitCode)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE consumed_authorizations SET claim_status='terminal', terminal_result=$result "
                + "WHERE authorization_id=$id AND execution_correlation_id=$correlation;";
            command.Parameters.AddWithValue("$result", "exit-code:" + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", claim.AuthorizationId);
            command.Parameters.AddWithValue("$correlation", claim.ExecutionCorrelationId);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("claim record missing");
            transaction.Commit();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SqliteException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // A terminal-status failure never releases the unique claim.
            throw new ExecutionException("authorization-consumption-terminal-record-failed");
        }
    }

    internal bool ContainsAuthorization(string authorizationId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM consumed_authorizations WHERE authorization_id=$id;";
        command.Parameters.AddWithValue("$id", authorizationId);
        return command.ExecuteScalar() is not null;
    }

    private SqliteConnection OpenConnection()
    {
        if (!File.Exists(_path) || (File.GetAttributes(_path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
            throw new ExecutionException("authorization-consumption-store-unavailable");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 10
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            Execute(connection, null, "PRAGMA synchronous=FULL;");
            Execute(connection, null, "PRAGMA foreign_keys=ON;");
            Execute(connection, null, "PRAGMA busy_timeout=10000;");
            RequirePragma(connection, "PRAGMA journal_mode;", "wal");
            RequirePragma(connection, "PRAGMA synchronous;", "2");
            RequirePragma(connection, "PRAGMA foreign_keys;", "1");
            RequirePragma(connection, "PRAGMA busy_timeout;", "10000");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private string ReadAuthorityId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM registry_metadata WHERE key='authority_id';";
        var authority = command.ExecuteScalar()?.ToString();
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT value FROM registry_metadata WHERE key='schema_version';";
        if (schema.ExecuteScalar()?.ToString() != SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || authority is null)
            throw new ExecutionException("authorization-consumption-store-unavailable");
        return authority;
    }

    private void EnsureAuthority(SqliteConnection connection, SqliteTransaction transaction, string expectedAuthority)
    {
        using var schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = "SELECT value FROM registry_metadata WHERE key='schema_version';";
        if (schema.ExecuteScalar()?.ToString() != SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new ExecutionException("authorization-consumption-store-unavailable");
        using var authority = connection.CreateCommand();
        authority.Transaction = transaction;
        authority.CommandText = "SELECT value FROM registry_metadata WHERE key='authority_id';";
        if (authority.ExecuteScalar()?.ToString() != expectedAuthority)
            throw new ExecutionException("authorization-consumption-authority-mismatch");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void RequirePragma(SqliteConnection connection, string sql, string expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (!string.Equals(command.ExecuteScalar()?.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            throw new ExecutionException("authorization-consumption-store-unavailable");
    }

    private static void SetPrivateUnixPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
