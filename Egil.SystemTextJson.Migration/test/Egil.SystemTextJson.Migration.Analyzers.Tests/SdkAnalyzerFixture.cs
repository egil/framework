#if NET11_0_OR_GREATER
using System.Diagnostics;
using System.Security;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

internal static class SdkAnalyzerFixture
{
    public static async Task<(int ExitCode, string Output, string Diagnostics)> BuildAsync(string source, string analyzerPath, CancellationToken cancellationToken, string? referencedSource = null, bool execute = false)
    {
        // The analyzer targets Roslyn 4.8 for compiler compatibility. SDK compilation is
        // needed to exercise actual C# unions and source-generated JSON metadata.
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var directory = Directory.CreateTempSubdirectory("stjm-sdk-analyzer-");
        var fixtureName = directory.Name;
        try
        {
            var project = Path.Combine(directory.FullName, "Fixture.csproj");
            if (referencedSource is not null)
            {
                var referenceDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "Reference"));
                await File.WriteAllTextAsync(Path.Combine(referenceDirectory.FullName, "Reference.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup></Project>", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(referenceDirectory.FullName, "Reference.cs"), referencedSource, cancellationToken);
            }
            var errorLog = Path.Combine(directory.FullName, "diagnostics.sarif");
            await File.WriteAllTextAsync(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <OutputType>{(execute ? "Exe" : "Library")}</OutputType>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EnableNETAnalyzers>false</EnableNETAnalyzers>
                    <ErrorLog>{SecurityElement.Escape(errorLog)},version=2.1</ErrorLog>
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="{SecurityElement.Escape(analyzerPath)}" />
                    {(referencedSource is null ? "" : "<ProjectReference Include=\"Reference/Reference.csproj\" /><Compile Remove=\"Reference/**/*.cs\" />")}
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
            var buildOutput = await output + await errors;
            if (!execute || process.ExitCode != 0)
            {
                return (process.ExitCode, buildOutput, diagnostics);
            }

            start.ArgumentList.Clear();
            start.ArgumentList.Add(Path.Combine(directory.FullName, "bin", "Release", "net11.0", "Fixture.dll"));
            using var execution = Process.Start(start)!;
            var executionOutput = execution.StandardOutput.ReadToEndAsync(cancellationToken);
            var executionErrors = execution.StandardError.ReadToEndAsync(cancellationToken);
            await execution.WaitForExitAsync(cancellationToken);
            return (execution.ExitCode, buildOutput + await executionOutput + await executionErrors, diagnostics);
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
