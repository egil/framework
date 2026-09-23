using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using System.Text.Json;
using Egil.SystemTextJson.Migration.PerfTests;

static string CaseKey(BenchmarkCase benchmark) => $"{benchmark.Descriptor.Type.Name}.{benchmark.Descriptor.WorkloadMethod.Name}{benchmark.Parameters.DisplayInfo}";

if (args is ["--list-hot-path-cases"])
{
    var cases = new[] { typeof(StructMigrationHotPathBenchmarks), typeof(VersionsMigrationHotPathBenchmarks) }
        .SelectMany(type => BenchmarkConverter.TypeToBenchmarks(type).BenchmarksCases)
        .Select(CaseKey).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(cases));
    return;
}

if (args.Length >= 2 && args[0] == "--hot-path-case")
{
    string key = args[1];
    var config = ManualConfig.Create(DefaultConfig.Instance).AddFilter(new SimpleFilter(benchmark => CaseKey(benchmark) == key));
    var summaries = BenchmarkSwitcher.FromTypes([typeof(StructMigrationHotPathBenchmarks), typeof(VersionsMigrationHotPathBenchmarks)])
        .Run(args[2..], config);
    if (summaries.Sum(summary => summary.Reports.Length) != 1 || summaries.Any(summary => summary.Reports.Any(report => !report.Success)))
    {
        throw new InvalidOperationException($"Isolated benchmark '{key}' did not produce exactly one successful result.");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
