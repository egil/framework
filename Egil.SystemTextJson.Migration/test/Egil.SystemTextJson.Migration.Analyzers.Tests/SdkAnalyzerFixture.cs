#if NET11_0_OR_GREATER
using System.Diagnostics;
using System.Security;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

internal static class SdkAnalyzerFixture
{
    public static async Task<(int ExitCode, string Output, string Diagnostics)> BuildAsync(string source, string analyzerPath, CancellationToken cancellationToken)
    {
        // The analyzer targets Roslyn 4.8 for compiler compatibility. Only the SDK compiler
        // can bind actual C# unions, so this boundary test must compile through dotnet build.
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var directory = Directory.CreateTempSubdirectory("stjm-sdk-analyzer-");
        var fixtureName = directory.Name;
        try
        {
            var project = Path.Combine(directory.FullName, "Fixture.csproj");
            var errorLog = Path.Combine(directory.FullName, "diagnostics.sarif");
            await File.WriteAllTextAsync(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EnableNETAnalyzers>false</EnableNETAnalyzers>
                    <ErrorLog>{SecurityElement.Escape(errorLog)},version=2.1</ErrorLog>
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="{SecurityElement.Escape(analyzerPath)}" />
                  </ItemGroup>
                </Project>
                """, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Fixture.cs"), source, cancellationToken);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add("--configuration");
            start.ArgumentList.Add("Release");
            start.ArgumentList.Add("--disable-build-servers");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var diagnostics = File.Exists(errorLog) ? await File.ReadAllTextAsync(errorLog, cancellationToken) : "";
            return (process.ExitCode, await output + await errors, diagnostics);
        }
        finally
        {
            directory.Refresh();
            if (directory.Exists)
            {
                var resolvedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.FullName));
                if (Path.GetDirectoryName(resolvedPath) != temporaryRoot
                    || Path.GetFileName(resolvedPath) != fixtureName
                    || directory.LinkTarget is not null)
                {
                    throw new InvalidOperationException($"Refusing to remove unexpected SDK fixture directory '{resolvedPath}'.");
                }

                directory.Delete(recursive: true);
            }
        }
    }
}
#endif
