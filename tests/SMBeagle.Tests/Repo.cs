using System.Diagnostics;

namespace SMBeagle.Tests;

/// <summary>Localisation du dépôt et de l'exécutable SMBeagle construit par dotnet build/test.</summary>
static class Repo
{
    public static string Root { get; } = FindRoot();
    public static string Fixtures => Path.Combine(Root, "tests", "fixtures");
    public static string Golden => Path.Combine(Root, "tests", "golden", "scan_19col.csv");
    public static string Executable { get; } = FindExecutable();

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SMBeagle.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SMBeagle.csproj introuvable au-dessus de " + AppContext.BaseDirectory);
    }

    static string FindExecutable()
    {
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}") ? "Debug" : "Release";
        string name = OperatingSystem.IsWindows() ? "SMBeagle.exe" : "SMBeagle";
        var candidates = new DirectoryInfo(Path.Combine(Root, "bin", config))
            .EnumerateFiles(name, SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"exécutable {name} introuvable sous bin/{config} : lancer dotnet build -c {config}");
        return candidates[0].FullName;
    }

    public sealed record RunResult(int ExitCode, string Stdout, string Stderr);

    public static RunResult Run(params string[] args) => RunIn(Root, args);

    /// <summary>
    /// Lance SMBeagle depuis un répertoire courant choisi : indispensable pour prouver
    /// qu'un fragment de chemin relatif n'est PAS résolu contre le répertoire courant.
    /// </summary>
    public static RunResult RunIn(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("impossible de lancer " + Executable);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(180_000))
        {
            p.Kill(true);
            throw new TimeoutException("SMBeagle n'a pas terminé en 180 s");
        }
        return new RunResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    public static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "smbeagle-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
