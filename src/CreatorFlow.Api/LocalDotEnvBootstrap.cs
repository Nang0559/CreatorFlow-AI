using System.Runtime.CompilerServices;

namespace CreatorFlow.Api;

/// <summary>
/// Loads the repository's ignored local .env file before WebApplication reads configuration.
/// Explicit process/container environment variables always take precedence.
/// </summary>
internal static class LocalDotEnvBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Opt-in only: tests, IDEs, services, and production hosts must never
        // inherit a developer's repository .env file unless explicitly requested.
        var enabled = Environment.GetEnvironmentVariable("CREATORFLOW_LOAD_DOTENV");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase))
            return;

        var configuredPath = Environment.GetEnvironmentVariable("CREATORFLOW_DOTENV_PATH");
        var envPath = !string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : FindRepositoryEnvFile();

        if (envPath is null || !File.Exists(envPath))
            return;

        var loaded = 0;
        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line[7..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                continue;

            // A process/container/IDE-provided value wins over the local file.
            if (Environment.GetEnvironmentVariable(name) is not null)
                continue;

            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(name, value);
            loaded++;
        }

        Console.WriteLine($"[LocalConfig] Loaded {loaded} local environment value(s) from .env. Secret values are not logged.");
    }

    private static string? FindRepositoryEnvFile()
    {
        // dotnet run normally starts in the repository root. Also search upward from
        // the application directory so launching the built API from a child folder works.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, ".env");
                if (File.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
        }

        return null;
    }
}
