# Reflection Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
> Public reports omit the internal `PolymorphicPlainStj*` guardrail benchmarks.
```

BenchmarkDotNet v0.15.6, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                       | Categories                                 | PayloadSize | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |------------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |    **457.7 ns** |   **122.99 ns** |    **31.94 ns** |  **1.50** |    **0.25** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    312.5 ns |   195.52 ns |    50.78 ns |  1.02 |    0.22 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **1,672.4 ns** |    **54.12 ns** |    **14.05 ns** |  **1.09** |    **0.04** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  1,532.8 ns |   365.16 ns |    56.51 ns |  1.00 |    0.05 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **11,792.6 ns** |   **560.09 ns** |    **86.67 ns** |  **1.01** |    **0.01** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 11,619.8 ns |   544.95 ns |   141.52 ns |  1.00 |    0.02 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **349.3 ns** |     **4.50 ns** |     **1.17 ns** |  **1.33** |    **0.01** | **0.0129** |      **-** |     **216 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    262.8 ns |     7.10 ns |     1.10 ns |  1.00 |    0.01 | 0.0129 |      - |     216 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **1,563.1 ns** |    **72.61 ns** |    **11.24 ns** |  **1.07** |    **0.01** | **0.1049** |      **-** |    **1760 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  1,456.6 ns |    19.11 ns |     2.96 ns |  1.00 |    0.00 | 0.1049 |      - |    1760 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **11,608.7 ns** |   **362.40 ns** |    **94.11 ns** |  **1.00** |    **0.01** | **1.4496** | **0.1678** |   **24488 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 11,555.0 ns |   846.31 ns |   130.97 ns |  1.00 |    0.01 | 1.4496 | 0.1678 |   24488 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **225.6 ns** |    **90.63 ns** |    **23.54 ns** |  **1.01** |    **0.13** | **0.0110** |      **-** |     **184 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    335.6 ns |   112.33 ns |    17.38 ns |  1.50 |    0.15 | 0.0110 |      - |     184 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,567.5 ns** |   **317.74 ns** |    **49.17 ns** |  **1.00** |    **0.04** | **0.1030** |      **-** |    **1728 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  1,537.0 ns |   148.47 ns |    22.98 ns |  0.98 |    0.03 | 0.1030 |      - |    1728 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **11,462.3 ns** |   **368.02 ns** |    **56.95 ns** |  **1.00** |    **0.01** | **1.4496** | **0.1678** |   **24456 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 11,856.2 ns | 2,835.35 ns |   438.77 ns |  1.03 |    0.03 | 1.4496 | 0.1678 |   24456 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **457.3 ns** |    **46.57 ns** |    **12.10 ns** |  **1.72** |    **0.06** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    265.4 ns |    30.18 ns |     7.84 ns |  1.00 |    0.04 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **1,749.2 ns** |   **121.40 ns** |    **18.79 ns** |  **1.19** |    **0.03** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  1,469.0 ns |   251.87 ns |    38.98 ns |  1.00 |    0.03 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **11,621.7 ns** |   **389.20 ns** |    **60.23 ns** |  **1.02** |    **0.01** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 11,379.9 ns |   340.24 ns |    88.36 ns |  1.00 |    0.01 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **357.8 ns** |    **10.05 ns** |     **2.61 ns** |  **1.36** |    **0.07** | **0.0200** |      **-** |     **336 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    263.9 ns |    97.34 ns |    15.06 ns |  1.00 |    0.07 | 0.0200 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **1,679.8 ns** |   **409.86 ns** |   **106.44 ns** |  **1.13** |    **0.07** | **0.1106** |      **-** |    **1880 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  1,488.4 ns |    46.72 ns |     7.23 ns |  1.00 |    0.01 | 0.1106 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **12,860.4 ns** | **2,700.15 ns** |   **701.22 ns** |  **0.97** |    **0.10** | **1.4648** | **0.1831** |   **24608 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 13,431.7 ns | 5,294.46 ns | 1,374.95 ns |  1.01 |    0.13 | 1.4648 | 0.1831 |   24608 B |        1.00 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Small**       |    **610.8 ns** |    **61.79 ns** |     **9.56 ns** |  **1.00** |    **0.02** | **0.0391** |      **-** |     **656 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Small       |    472.6 ns |    47.62 ns |     7.37 ns |  0.77 |    0.02 | 0.0110 |      - |     184 B |        0.28 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Small       |    640.5 ns |    73.67 ns |    19.13 ns |  1.05 |    0.03 | 0.0200 |      - |     336 B |        0.51 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Medium**      |  **2,481.9 ns** |   **105.78 ns** |    **27.47 ns** |  **1.00** |    **0.01** | **0.1030** |      **-** |    **1728 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Medium      |  2,256.9 ns |   851.35 ns |   131.75 ns |  0.91 |    0.05 | 0.1030 |      - |    1728 B |        1.00 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Medium      |  2,243.7 ns |   114.58 ns |    17.73 ns |  0.90 |    0.01 | 0.1106 |      - |    1880 B |        1.09 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Large**       | **18,081.2 ns** |   **609.10 ns** |    **94.26 ns** |  **1.00** |    **0.01** | **1.4954** | **0.1831** |   **25376 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Large       | 14,626.9 ns |    63.13 ns |     9.77 ns |  0.81 |    0.00 | 1.4343 | 0.1526 |   24456 B |        0.96 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Large       | 16,692.1 ns | 4,599.80 ns | 1,194.55 ns |  0.92 |    0.06 | 1.4648 | 0.1831 |   24608 B |        0.97 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |    **129.2 ns** |    **34.54 ns** |     **8.97 ns** |  **1.00** |    **0.09** | **0.0033** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    204.8 ns |    10.48 ns |     1.62 ns |  1.59 |    0.10 | 0.0081 |      - |     136 B |        2.43 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **769.7 ns** |    **56.78 ns** |    **14.75 ns** |  **1.00** |    **0.02** | **0.0429** |      **-** |     **728 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    862.5 ns |   101.30 ns |    15.68 ns |  1.12 |    0.03 | 0.0477 |      - |     800 B |        1.10 |
|                                              |                                            |             |             |             |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **6,330.6 ns** | **1,814.95 ns** |   **471.34 ns** |  **1.00** |    **0.09** | **0.6332** | **0.0229** |   **10696 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  6,313.4 ns | 1,279.70 ns |   198.04 ns |  1.00 |    0.07 | 0.6409 | 0.0229 |   10776 B |        1.01 |
