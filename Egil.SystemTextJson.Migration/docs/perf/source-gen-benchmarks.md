# Source-Generated Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
> Public reports omit the internal `PolymorphicPlainStj*` guardrail benchmarks.
```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.9457)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v3

Affinity=00000000000000001100  Toolchain=InProcessNoEmitToolchain  IterationCount=15  
LaunchCount=1  WarmupCount=3  

```
| Method                                       | Categories                                 | PayloadSize | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |-------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |    **562.98 ns** |  **17.684 ns** |  **16.541 ns** |  **1.76** |    **0.05** | **0.0248** |      **-** |     **312 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    319.04 ns |   0.639 ns |   0.597 ns |  1.00 |    0.00 | 0.0248 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **2,181.95 ns** |  **35.896 ns** |  **33.577 ns** |  **1.12** |    **0.02** | **0.1411** |      **-** |    **1808 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  1,947.25 ns |   9.136 ns |   8.546 ns |  1.00 |    0.01 | 0.1411 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **14,761.47 ns** |  **45.019 ns** |  **42.111 ns** |  **1.05** |    **0.01** | **1.9684** | **0.2136** |   **24776 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 14,055.56 ns |  70.414 ns |  62.420 ns |  1.00 |    0.01 | 1.9684 | 0.1984 |   24776 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **431.97 ns** |   **0.446 ns** |   **0.396 ns** |  **1.33** |    **0.00** | **0.0153** |      **-** |     **192 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    324.82 ns |   0.805 ns |   0.753 ns |  1.00 |    0.00 | 0.0153 |      - |     192 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **2,091.69 ns** |  **15.461 ns** |  **14.462 ns** |  **1.09** |    **0.01** | **0.1335** |      **-** |    **1688 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  1,922.71 ns |  20.818 ns |  18.455 ns |  1.00 |    0.01 | 0.1335 |      - |    1688 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **14,673.94 ns** |  **94.015 ns** |  **78.506 ns** |  **1.02** |    **0.01** | **1.9531** | **0.2136** |   **24656 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 14,393.11 ns |  89.743 ns |  83.945 ns |  1.00 |    0.01 | 1.9531 | 0.2136 |   24656 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **251.02 ns** |   **1.297 ns** |   **1.213 ns** |  **1.00** |    **0.01** | **0.0124** |      **-** |     **160 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    383.56 ns |   0.682 ns |   0.638 ns |  1.53 |    0.01 | 0.0124 |      - |     160 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,865.72 ns** |   **6.905 ns** |   **6.459 ns** |  **1.00** |    **0.00** | **0.1316** |      **-** |    **1656 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  2,025.74 ns |   7.236 ns |   6.768 ns |  1.09 |    0.01 | 0.1297 |      - |    1656 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **14,357.30 ns** | **113.937 ns** | **106.577 ns** |  **1.00** |    **0.01** | **1.9531** | **0.2289** |   **24624 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 14,519.17 ns |  82.567 ns |  77.234 ns |  1.01 |    0.01 | 1.9531 | 0.2289 |   24624 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **554.32 ns** |   **0.944 ns** |   **0.788 ns** |  **1.76** |    **0.00** | **0.0248** |      **-** |     **312 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    315.56 ns |   0.697 ns |   0.652 ns |  1.00 |    0.00 | 0.0248 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **2,228.92 ns** |  **24.782 ns** |  **23.181 ns** |  **1.11** |    **0.04** | **0.1411** |      **-** |    **1808 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  2,003.50 ns |  84.048 ns |  78.618 ns |  1.00 |    0.05 | 0.1411 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **14,826.24 ns** | **119.514 ns** | **111.793 ns** |  **1.02** |    **0.01** | **1.9531** | **0.1831** |   **24776 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 14,584.96 ns |  67.354 ns |  63.003 ns |  1.00 |    0.01 | 1.9684 | 0.1984 |   24776 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **452.95 ns** |   **1.196 ns** |   **1.119 ns** |  **1.42** |    **0.00** | **0.0248** |      **-** |     **312 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    318.95 ns |   0.577 ns |   0.540 ns |  1.00 |    0.00 | 0.0248 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **2,281.10 ns** |   **8.849 ns** |   **8.277 ns** |  **1.16** |    **0.01** | **0.1411** |      **-** |    **1808 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  1,960.61 ns |   8.741 ns |   8.176 ns |  1.00 |    0.01 | 0.1411 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **14,664.90 ns** | **149.653 ns** | **139.986 ns** |  **1.01** |    **0.01** | **1.9684** | **0.2136** |   **24776 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 14,450.53 ns |  52.404 ns |  49.019 ns |  1.00 |    0.00 | 1.9684 | 0.2136 |   24776 B |        1.00 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Small**       |    **621.03 ns** |   **1.388 ns** |   **1.231 ns** |  **1.00** |    **0.00** | **0.0496** |      **-** |     **632 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Small       |    572.33 ns |   0.803 ns |   0.712 ns |  0.92 |    0.00 | 0.0124 |      - |     160 B |        0.25 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Small       |    763.34 ns |   3.732 ns |   3.117 ns |  1.23 |    0.01 | 0.0248 |      - |     312 B |        0.49 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Medium**      |  **3,178.05 ns** |   **3.906 ns** |   **3.050 ns** |  **1.00** |    **0.00** | **0.1297** |      **-** |    **1656 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Medium      |  2,681.66 ns |   6.595 ns |   6.169 ns |  0.84 |    0.00 | 0.1297 |      - |    1656 B |        1.00 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Medium      |  2,967.83 ns |  26.438 ns |  24.730 ns |  0.93 |    0.01 | 0.1411 |      - |    1808 B |        1.09 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Large**       | **22,504.99 ns** | **142.537 ns** | **133.330 ns** |  **1.00** |    **0.01** | **2.0142** | **0.2136** |   **25544 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Large       | 18,793.45 ns |  60.185 ns |  56.297 ns |  0.84 |    0.01 | 1.9531 | 0.2136 |   24624 B |        0.96 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Large       | 19,124.73 ns | 100.196 ns |  93.723 ns |  0.85 |    0.01 | 1.9531 | 0.1831 |   24776 B |        0.97 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |     **84.97 ns** |   **0.405 ns** |   **0.379 ns** |  **1.00** |    **0.01** | **0.0044** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    168.78 ns |   0.279 ns |   0.247 ns |  1.99 |    0.01 | 0.0069 |      - |      88 B |        1.57 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **504.53 ns** |   **0.636 ns** |   **0.595 ns** |  **1.00** |    **0.00** | **0.0324** |      **-** |     **416 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    692.47 ns |   1.875 ns |   1.754 ns |  1.37 |    0.00 | 0.0591 |      - |     752 B |        1.81 |
|                                              |                                            |             |              |            |            |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **4,800.09 ns** |   **8.347 ns** |   **7.399 ns** |  **1.00** |    **0.00** | **0.8240** | **0.0305** |   **10384 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  5,110.45 ns |  18.880 ns |  17.660 ns |  1.06 |    0.00 | 0.8545 | 0.0305 |   10728 B |        1.03 |
