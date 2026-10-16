using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AlarmSystem.Infrastructure.Persistence.SqlServer;

public sealed record SqlMigration(
    string MigrationId,
    string ChecksumSha256,
    string Sql);

public static class SqlMigrationCatalog
{
    private const string ResourceSegment = ".Persistence.SqlServer.Migrations.";

    public static IReadOnlyList<SqlMigration> Load()
    {
        var assembly = typeof(SqlMigrationCatalog).Assembly;
        var migrations = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceSegment, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Load(assembly, name))
            .ToArray();

        if (migrations.Length == 0)
        {
            throw new InvalidOperationException("No embedded SQL migrations were found.");
        }

        return migrations;
    }

    public static string ComputeChecksum(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static SqlMigration Load(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Migration resource '{resourceName}' is unavailable.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var content = memory.ToArray();
        var fileName = resourceName[(resourceName.IndexOf(ResourceSegment, StringComparison.Ordinal) +
            ResourceSegment.Length)..];
        var migrationId = Path.GetFileNameWithoutExtension(fileName);
        return new SqlMigration(
            migrationId,
            ComputeChecksum(content),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(content));
    }
}
