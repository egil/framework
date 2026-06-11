# Source-Generated Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
> Public reports omit the internal `PolymorphicPlainStj*` guardrail benchmarks.
```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8655)
Unknown processor
.NET SDK 11.0.100-preview.4.26230.115
  [Host] : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                       | Categories                                 | PayloadSize | Mean        | Error        | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |------------:|-------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |    **566.9 ns** |    **104.92 ns** |    **16.24 ns** |  **1.70** |    **0.08** | **0.0181** |      **-** |     **312 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    334.1 ns |     92.13 ns |    14.26 ns |  1.00 |    0.06 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **2,405.9 ns** |    **489.72 ns** |   **127.18 ns** |  **1.29** |    **0.08** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  1,861.7 ns |    532.51 ns |    82.41 ns |  1.00 |    0.06 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **18,846.3 ns** |  **9,421.14 ns** | **2,446.64 ns** |  **1.10** |    **0.17** | **1.4648** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 17,206.9 ns | 11,081.65 ns | 1,714.90 ns |  1.01 |    0.13 | 1.4801 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **486.0 ns** |    **108.05 ns** |    **28.06 ns** |  **1.30** |    **0.08** | **0.0114** |      **-** |     **192 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    373.4 ns |     42.89 ns |    11.14 ns |  1.00 |    0.04 | 0.0114 |      - |     192 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **1,991.9 ns** |    **743.20 ns** |   **193.01 ns** |  **1.04** |    **0.12** | **0.0992** |      **-** |    **1688 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  1,933.9 ns |    677.74 ns |   176.01 ns |  1.01 |    0.12 | 0.0992 |      - |    1688 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **15,528.3 ns** |  **3,325.89 ns** |   **863.72 ns** |  **0.92** |    **0.05** | **1.4648** | **0.1526** |   **24656 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 16,924.1 ns |  2,087.67 ns |   323.07 ns |  1.00 |    0.02 | 1.4648 | 0.1526 |   24656 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **304.5 ns** |     **59.16 ns** |    **15.36 ns** |  **1.00** |    **0.06** | **0.0095** |      **-** |     **160 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    337.1 ns |     14.80 ns |     2.29 ns |  1.11 |    0.05 | 0.0095 |      - |     160 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,465.9 ns** |     **46.41 ns** |     **7.18 ns** |  **1.00** |    **0.01** | **0.0973** |      **-** |    **1656 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  1,572.8 ns |     12.60 ns |     3.27 ns |  1.07 |    0.01 | 0.0973 |      - |    1656 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **15,625.2 ns** |    **969.57 ns** |   **150.04 ns** |  **1.00** |    **0.01** | **1.4648** | **0.1526** |   **24624 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 16,915.7 ns |  6,643.56 ns | 1,725.31 ns |  1.08 |    0.10 | 1.4648 | 0.1526 |   24624 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **632.9 ns** |    **372.98 ns** |    **57.72 ns** |  **1.85** |    **0.19** | **0.0181** |      **-** |     **312 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    342.7 ns |     82.25 ns |    21.36 ns |  1.00 |    0.08 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **2,104.1 ns** |    **312.81 ns** |    **48.41 ns** |  **1.05** |    **0.05** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  2,013.6 ns |    343.38 ns |    89.17 ns |  1.00 |    0.06 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **16,988.8 ns** |  **4,215.95 ns** | **1,094.87 ns** |  **0.96** |    **0.09** | **1.4801** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 17,840.2 ns |  5,076.17 ns | 1,318.27 ns |  1.00 |    0.10 | 1.4801 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **491.2 ns** |    **196.23 ns** |    **30.37 ns** |  **1.30** |    **0.11** | **0.0186** |      **-** |     **312 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    379.1 ns |     97.97 ns |    25.44 ns |  1.00 |    0.09 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **2,106.4 ns** |  **1,186.72 ns** |   **183.65 ns** |  **1.04** |    **0.10** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  2,026.8 ns |    454.43 ns |   118.01 ns |  1.00 |    0.08 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **15,659.2 ns** |  **1,689.50 ns** |   **261.45 ns** |  **0.97** |    **0.18** | **1.4801** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 16,685.8 ns | 11,729.48 ns | 3,046.11 ns |  1.03 |    0.25 | 1.4648 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |    **107.8 ns** |     **14.20 ns** |     **2.20 ns** |  **1.00** |    **0.03** | **0.0033** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    169.8 ns |      1.65 ns |     0.43 ns |  1.58 |    0.03 | 0.0081 |      - |     136 B |        2.43 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **436.8 ns** |      **4.11 ns** |     **1.07 ns** |  **1.00** |    **0.00** | **0.0248** |      **-** |     **416 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    735.2 ns |     19.44 ns |     5.05 ns |  1.68 |    0.01 | 0.0477 |      - |     800 B |        1.92 |
|                                              |                                            |             |             |              |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **3,689.9 ns** |    **107.54 ns** |    **27.93 ns** |  **1.00** |    **0.01** | **0.6180** | **0.0229** |   **10384 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  4,982.6 ns |    157.71 ns |    40.96 ns |  1.35 |    0.01 | 0.6409 | 0.0229 |   10776 B |        1.04 |
