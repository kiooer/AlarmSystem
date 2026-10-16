namespace AlarmSystem.Application.Abstractions.Persistence;

public interface IDatabaseInitializer
{
    Task<DatabaseInitializationResult> InitializeAsync(
        DatabaseInitializationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DatabaseInitializationRequest(
    bool Apply,
    string? ConfirmedServer = null,
    string? ConfirmedDatabase = null,
    string ToolVersion = "development");

public sealed record DatabaseMigrationPlanItem(
    string MigrationId,
    string ChecksumSha256,
    bool IsApplied);

public sealed record DatabaseInitializationResult(
    bool Applied,
    string ConfiguredServer,
    string ActualServer,
    string DatabaseName,
    IReadOnlyList<DatabaseMigrationPlanItem> Migrations);
