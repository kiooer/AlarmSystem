namespace AlarmSystem.Application.Abstractions.Persistence;

public interface IDatabaseSchemaValidator
{
    Task<DatabaseSchemaValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default);
}

public enum DatabaseSchemaIssueKind
{
    MissingObject,
    UnexpectedDefinition,
    MigrationChecksumMismatch,
    UnappliedMigration,
}

public sealed record DatabaseSchemaIssue(
    DatabaseSchemaIssueKind Kind,
    string ObjectName,
    string Description);

public sealed record DatabaseSchemaValidationResult(
    bool IsValid,
    string ConfiguredServer,
    string ActualServer,
    string DatabaseName,
    IReadOnlyList<DatabaseSchemaIssue> Issues);
