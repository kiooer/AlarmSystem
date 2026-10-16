using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlarmSystem.Application.Abstractions.Persistence;
using AlarmSystem.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.SqlClient;

return await DatabaseToolProgram.RunAsync(args);

internal static class DatabaseToolProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!DatabaseToolArguments.TryParse(args, out var arguments, out var argumentError))
        {
            WriteError("usage", argumentError!);
            WriteUsage();
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var options = await SqlServerConnectionOptionsLoader.LoadAsync(
                arguments!.ConfigPath,
                cancellation.Token);
            var factory = new SqlServerConnectionFactory(options);

            return arguments.Command switch
            {
                "test-connection" => await TestConnectionAsync(options, factory, cancellation.Token),
                "inspect-source" => await InspectSourceAsync(
                    options,
                    factory,
                    arguments,
                    cancellation.Token),
                "initialize" => await InitializeAsync(options, factory, arguments, cancellation.Token),
                "verify-schema" => await VerifySchemaAsync(options, factory, cancellation.Token),
                _ => 2,
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            WriteError("cancelled", "The operation was cancelled by the caller.");
            return 130;
        }
        catch (SqlConfigurationException exception)
        {
            WriteError("configuration", exception.Message);
            return 2;
        }
        catch (SqlException exception)
        {
            var category = SqlConnectionErrorClassifier.Classify(exception);
            WriteError(category.ToString(), SqlConnectionErrorClassifier.GetRecommendation(category));
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            WriteError("operation_refused", exception.Message);
            return 1;
        }
        catch (ArgumentException exception)
        {
            WriteError("invalid_argument", exception.Message);
            return 2;
        }
        catch (Exception)
        {
            WriteError(
                "unexpected_failure",
                "The operation failed unexpectedly. Review local diagnostics without exposing configuration secrets.");
            return 1;
        }
    }

    private static async Task<int> TestConnectionAsync(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory factory,
        CancellationToken cancellationToken)
    {
        var result = await new SqlConnectionDiagnostics(options, factory)
            .TestAsync(cancellationToken);
        WriteJson(result);
        return result.ConnectionStatus ? 0 : 1;
    }

    private static async Task<int> InspectSourceAsync(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory factory,
        DatabaseToolArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await new SqlSourceSchemaInspector(options, factory).InspectAsync(
            arguments.IncludeMessageIdStatistics,
            cancellationToken);
        WriteJson(result);
        return result.Findings.Any(finding =>
            finding.Kind == SourceSchemaFindingKind.MissingTable) ? 1 : 0;
    }

    private static async Task<int> InitializeAsync(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory factory,
        DatabaseToolArguments arguments,
        CancellationToken cancellationToken)
    {
        var initializer = new SqlDatabaseInitializer(options, factory);
        var toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "development";
        var dryRun = await initializer.InitializeAsync(
            new DatabaseInitializationRequest(false, ToolVersion: toolVersion),
            cancellationToken);
        WriteJson(new { Phase = "Preflight", Result = dryRun });

        if (!arguments.Apply)
        {
            return 0;
        }

        var result = await initializer.InitializeAsync(
            new DatabaseInitializationRequest(
                true,
                arguments.ConfirmedServer,
                arguments.ConfirmedDatabase,
                toolVersion),
            cancellationToken);
        WriteJson(new { Phase = "ApplyComplete", Result = result });
        return 0;
    }

    private static async Task<int> VerifySchemaAsync(
        SqlServerConnectionOptions options,
        SqlServerConnectionFactory factory,
        CancellationToken cancellationToken)
    {
        var result = await new SqlDatabaseSchemaValidator(options, factory)
            .ValidateAsync(cancellationToken);
        WriteJson(result);
        return result.IsValid ? 0 : 3;
    }

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteError(string category, string message) =>
        Console.Error.WriteLine(JsonSerializer.Serialize(
            new { ErrorCategory = category, Message = message },
            JsonOptions));

    private static void WriteUsage() => Console.Error.WriteLine("""
        Usage:
          AlarmSystem.DatabaseTool test-connection --config <path>
          AlarmSystem.DatabaseTool inspect-source --config <path> [--include-msgid-stats]
          AlarmSystem.DatabaseTool initialize --config <path> [--dry-run]
          AlarmSystem.DatabaseTool initialize --config <path> --apply --confirm-server <value> --confirm-database <value>
          AlarmSystem.DatabaseTool verify-schema --config <path>
        """);
}

internal sealed record DatabaseToolArguments(
    string Command,
    string ConfigPath,
    bool Apply,
    string? ConfirmedServer,
    string? ConfirmedDatabase,
    bool IncludeMessageIdStatistics)
{
    private static readonly HashSet<string> Commands =
        ["test-connection", "inspect-source", "initialize", "verify-schema"];

    public static bool TryParse(
        string[] args,
        out DatabaseToolArguments? result,
        out string? error)
    {
        result = null;
        error = null;
        if (args.Length == 0 || args[0] is "--help" or "-h" || !Commands.Contains(args[0]))
        {
            error = args.Length == 0 || args[0] is "--help" or "-h"
                ? "A command is required."
                : $"Unsupported command '{args[0]}'.";
            return false;
        }

        string? configPath = null;
        string? confirmedServer = null;
        string? confirmedDatabase = null;
        var apply = false;
        var dryRun = false;
        var includeStatistics = false;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--config":
                    if (!TryReadValue(args, ref index, out configPath))
                    {
                        error = "--config requires a path.";
                        return false;
                    }

                    break;
                case "--confirm-server":
                    if (!TryReadValue(args, ref index, out confirmedServer))
                    {
                        error = "--confirm-server requires a value.";
                        return false;
                    }

                    break;
                case "--confirm-database":
                    if (!TryReadValue(args, ref index, out confirmedDatabase))
                    {
                        error = "--confirm-database requires a value.";
                        return false;
                    }

                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--include-msgid-stats":
                    includeStatistics = true;
                    break;
                default:
                    error = $"Unsupported option '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            error = "--config is required for every command.";
            return false;
        }

        if (args[0] != "initialize" && (apply || dryRun || confirmedServer is not null || confirmedDatabase is not null))
        {
            error = "Initialization options are valid only for the initialize command.";
            return false;
        }

        if (args[0] != "inspect-source" && includeStatistics)
        {
            error = "--include-msgid-stats is valid only for inspect-source.";
            return false;
        }

        if (apply && dryRun)
        {
            error = "Use either --apply or --dry-run, not both.";
            return false;
        }

        if (apply && (string.IsNullOrWhiteSpace(confirmedServer) ||
            string.IsNullOrWhiteSpace(confirmedDatabase)))
        {
            error = "--apply requires --confirm-server and --confirm-database.";
            return false;
        }

        if (!apply && (confirmedServer is not null || confirmedDatabase is not null))
        {
            error = "Confirmation values are accepted only with --apply.";
            return false;
        }

        result = new DatabaseToolArguments(
            args[0],
            Path.GetFullPath(configPath),
            apply,
            confirmedServer,
            confirmedDatabase,
            includeStatistics);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }
}
