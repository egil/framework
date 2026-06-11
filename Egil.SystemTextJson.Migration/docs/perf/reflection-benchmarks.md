# Reflection Benchmarks

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
| Method                                       | Categories                                 | PayloadSize | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |------------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |    **529.1 ns** |    **66.94 ns** |    **10.36 ns** |  **1.46** |    **0.08** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    363.2 ns |    85.01 ns |    22.08 ns |  1.00 |    0.08 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **2,305.0 ns** |   **423.38 ns** |   **109.95 ns** |  **1.30** |    **0.08** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  1,783.0 ns |   349.52 ns |    90.77 ns |  1.00 |    0.07 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **15,026.2 ns** | **2,895.85 ns** |   **752.04 ns** |  **0.97** |    **0.07** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 15,506.3 ns | 3,606.56 ns |   936.61 ns |  1.00 |    0.08 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **457.5 ns** |    **14.59 ns** |     **3.79 ns** |  **1.25** |    **0.04** | **0.0129** |      **-** |     **216 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    365.9 ns |    52.85 ns |    13.72 ns |  1.00 |    0.05 | 0.0129 |      - |     216 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **2,316.1 ns** |   **772.17 ns** |   **200.53 ns** |  **1.16** |    **0.12** | **0.1030** |      **-** |    **1760 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  2,003.9 ns |   567.00 ns |   147.25 ns |  1.00 |    0.10 | 0.1030 |      - |    1760 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **14,745.9 ns** | **5,020.19 ns** | **1,303.73 ns** |  **1.29** |    **0.11** | **1.4343** | **0.1526** |   **24488 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 11,412.0 ns |   666.48 ns |   173.08 ns |  1.00 |    0.02 | 1.4496 | 0.1678 |   24488 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **214.1 ns** |    **14.70 ns** |     **2.27 ns** |  **1.00** |    **0.01** | **0.0110** |      **-** |     **184 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    393.2 ns |    55.51 ns |    14.42 ns |  1.84 |    0.06 | 0.0110 |      - |     184 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,792.1 ns** |   **257.80 ns** |    **66.95 ns** |  **1.00** |    **0.05** | **0.1030** |      **-** |    **1728 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  1,907.1 ns |   671.04 ns |   103.84 ns |  1.07 |    0.06 | 0.1030 |      - |    1728 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **11,793.4 ns** | **2,358.73 ns** |   **365.02 ns** |  **1.00** |    **0.04** | **1.4496** | **0.1678** |   **24456 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 15,605.1 ns | 2,718.11 ns |   705.88 ns |  1.32 |    0.07 | 1.4343 | 0.1526 |   24456 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **575.5 ns** |    **65.20 ns** |    **10.09 ns** |  **1.71** |    **0.05** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    336.1 ns |    35.32 ns |     9.17 ns |  1.00 |    0.04 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **2,353.4 ns** |   **840.48 ns** |   **218.27 ns** |  **1.25** |    **0.11** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  1,885.4 ns |   214.74 ns |    55.77 ns |  1.00 |    0.04 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **11,877.9 ns** |   **555.00 ns** |   **144.13 ns** |  **1.07** |    **0.01** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 11,113.6 ns |   168.78 ns |    43.83 ns |  1.00 |    0.01 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **352.5 ns** |    **10.36 ns** |     **2.69 ns** |  **1.33** |    **0.01** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    264.8 ns |     6.21 ns |     1.61 ns |  1.00 |    0.01 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **1,840.8 ns** | **1,035.10 ns** |   **268.81 ns** |  **0.93** |    **0.18** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  2,024.1 ns | 1,226.94 ns |   318.63 ns |  1.02 |    0.21 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **13,011.3 ns** | **3,674.11 ns** |   **954.15 ns** |  **0.74** |    **0.07** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 17,763.2 ns | 4,990.79 ns | 1,296.09 ns |  1.00 |    0.10 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |    **149.2 ns** |    **24.33 ns** |     **6.32 ns** |  **1.00** |    **0.06** | **0.0033** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    241.3 ns |    97.89 ns |    25.42 ns |  1.62 |    0.17 | 0.0081 |      - |     136 B |        2.43 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **793.2 ns** |   **291.33 ns** |    **45.08 ns** |  **1.00** |    **0.07** | **0.0429** |      **-** |     **728 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    955.8 ns |   128.40 ns |    33.34 ns |  1.21 |    0.07 | 0.0477 |      - |     800 B |        1.10 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **7,111.7 ns** | **1,509.26 ns** |   **391.95 ns** |  **1.00** |    **0.07** | **0.6332** | **0.0229** |   **10696 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  6,248.2 ns | 1,040.23 ns |   160.98 ns |  0.88 |    0.05 | 0.6409 | 0.0229 |   10776 B |        1.01 |
