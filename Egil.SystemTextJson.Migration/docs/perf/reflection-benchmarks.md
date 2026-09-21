# Reflection Benchmarks

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
| Method                                       | Categories                                 | PayloadSize | Mean        | Error     | StdDev    | Median      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |------------:|----------:|----------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |  **1,668.0 ns** | **770.74 ns** | **720.95 ns** |  **1,542.4 ns** |  **4.90** |    **2.05** | **0.0267** |      **-** |     **336 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    340.8 ns |   3.47 ns |   3.07 ns |    341.6 ns |  1.00 |    0.01 | 0.0267 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **2,875.8 ns** |  **50.69 ns** |  **47.42 ns** |  **2,875.5 ns** |  **1.08** |    **0.02** | **0.1488** |      **-** |    **1880 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  2,660.3 ns |  30.02 ns |  23.44 ns |  2,665.1 ns |  1.00 |    0.01 | 0.1488 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **27,587.6 ns** | **450.18 ns** | **375.92 ns** | **27,571.1 ns** |  **1.64** |    **0.03** | **1.9531** | **0.2441** |   **24608 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 16,797.8 ns | 190.84 ns | 169.17 ns | 16,790.6 ns |  1.00 |    0.01 | 1.9531 | 0.2441 |   24608 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **486.5 ns** |  **93.42 ns** |  **87.38 ns** |    **417.8 ns** |  **1.58** |    **0.27** | **0.0172** |      **-** |     **216 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    307.8 ns |   0.34 ns |   0.30 ns |    307.8 ns |  1.00 |    0.00 | 0.0172 |      - |     216 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **1,958.7 ns** |  **15.82 ns** |  **14.80 ns** |  **1,959.8 ns** |  **1.05** |    **0.01** | **0.1373** |      **-** |    **1760 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  1,859.6 ns |   4.36 ns |   4.08 ns |  1,858.2 ns |  1.00 |    0.00 | 0.1392 |      - |    1760 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **14,240.7 ns** |  **41.93 ns** |  **35.02 ns** | **14,243.0 ns** |  **1.06** |    **0.00** | **1.9379** | **0.2289** |   **24488 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 13,460.4 ns |  47.42 ns |  37.02 ns | 13,471.2 ns |  1.00 |    0.00 | 1.9379 | 0.2289 |   24488 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **236.2 ns** |   **1.58 ns** |   **1.24 ns** |    **235.6 ns** |  **1.00** |    **0.01** | **0.0143** |      **-** |     **184 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    369.3 ns |   0.62 ns |   0.58 ns |    369.5 ns |  1.56 |    0.01 | 0.0143 |      - |     184 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,765.0 ns** |   **5.38 ns** |   **5.04 ns** |  **1,765.3 ns** |  **1.00** |    **0.00** | **0.1373** |      **-** |    **1728 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  1,920.2 ns |  16.69 ns |  15.61 ns |  1,923.3 ns |  1.09 |    0.01 | 0.1373 |      - |    1728 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **13,509.3 ns** |  **33.76 ns** |  **29.93 ns** | **13,510.0 ns** |  **1.00** |    **0.00** | **1.9379** | **0.2289** |   **24456 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 13,678.2 ns | 135.39 ns | 126.64 ns | 13,635.2 ns |  1.01 |    0.01 | 1.9379 | 0.2289 |   24456 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **537.6 ns** |   **0.86 ns** |   **0.76 ns** |    **537.6 ns** |  **1.73** |    **0.00** | **0.0267** |      **-** |     **336 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    311.3 ns |   0.44 ns |   0.41 ns |    311.4 ns |  1.00 |    0.00 | 0.0267 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **2,146.2 ns** |  **27.76 ns** |  **25.97 ns** |  **2,157.9 ns** |  **1.15** |    **0.01** | **0.1488** |      **-** |    **1880 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  1,858.8 ns |   8.63 ns |   8.07 ns |  1,862.4 ns |  1.00 |    0.01 | 0.1488 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **14,133.3 ns** |  **70.99 ns** |  **66.41 ns** | **14,152.1 ns** |  **1.01** |    **0.01** | **1.9531** | **0.2441** |   **24608 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 13,931.0 ns | 142.68 ns | 119.14 ns | 13,912.4 ns |  1.00 |    0.01 | 1.9531 | 0.2441 |   24608 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **447.3 ns** |   **0.62 ns** |   **0.58 ns** |    **447.4 ns** |  **1.45** |    **0.00** | **0.0267** |      **-** |     **336 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    308.8 ns |   0.70 ns |   0.62 ns |    308.9 ns |  1.00 |    0.00 | 0.0267 |      - |     336 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **2,023.5 ns** |  **13.52 ns** |  **11.99 ns** |  **2,019.6 ns** |  **1.08** |    **0.01** | **0.1488** |      **-** |    **1880 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  1,875.2 ns |  23.86 ns |  22.32 ns |  1,873.6 ns |  1.00 |    0.02 | 0.1488 |      - |    1880 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **14,061.8 ns** |  **60.61 ns** |  **53.73 ns** | **14,061.3 ns** |  **1.03** |    **0.01** | **1.9531** | **0.2441** |   **24608 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 13,697.3 ns |  63.58 ns |  59.47 ns | 13,706.7 ns |  1.00 |    0.01 | 1.9531 | 0.2441 |   24608 B |        1.00 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Small**       |    **692.0 ns** |   **1.14 ns** |   **0.95 ns** |    **691.8 ns** |  **1.00** |    **0.00** | **0.0515** |      **-** |     **656 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Small       |    562.0 ns |   0.56 ns |   0.44 ns |    562.1 ns |  0.81 |    0.00 | 0.0143 |      - |     184 B |        0.28 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Small       |    746.3 ns |   0.92 ns |   0.86 ns |    746.3 ns |  1.08 |    0.00 | 0.0267 |      - |     336 B |        0.51 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Medium**      |  **3,183.9 ns** |   **5.47 ns** |   **5.12 ns** |  **3,182.8 ns** |  **1.00** |    **0.00** | **0.1373** |      **-** |    **1728 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Medium      |  2,573.4 ns |   8.36 ns |   6.98 ns |  2,570.8 ns |  0.81 |    0.00 | 0.1373 |      - |    1728 B |        1.00 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Medium      |  2,827.1 ns |  33.09 ns |  30.95 ns |  2,845.6 ns |  0.89 |    0.01 | 0.1488 |      - |    1880 B |        1.09 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Large**       | **21,863.5 ns** |  **78.75 ns** |  **65.76 ns** | **21,891.1 ns** |  **1.00** |    **0.00** | **2.0142** | **0.2441** |   **25376 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Large       | 17,838.5 ns | 120.16 ns | 106.52 ns | 17,846.5 ns |  0.82 |    0.01 | 1.9226 | 0.2136 |   24456 B |        0.96 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Large       | 18,295.5 ns | 119.71 ns | 111.98 ns | 18,346.8 ns |  0.84 |    0.01 | 1.9531 | 0.2441 |   24608 B |        0.97 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |    **121.1 ns** |   **0.18 ns** |   **0.15 ns** |    **121.1 ns** |  **1.00** |    **0.00** | **0.0043** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    164.8 ns |   0.29 ns |   0.24 ns |    164.7 ns |  1.36 |    0.00 | 0.0069 |      - |      88 B |        1.57 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **801.8 ns** |   **4.60 ns** |   **3.59 ns** |    **800.2 ns** |  **1.00** |    **0.01** | **0.0572** |      **-** |     **728 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    846.4 ns |   4.11 ns |   3.85 ns |    848.5 ns |  1.06 |    0.01 | 0.0591 |      - |     752 B |        1.03 |
|                                              |                                            |             |             |           |           |             |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **6,104.8 ns** |  **25.55 ns** |  **21.33 ns** |  **6,094.1 ns** |  **1.00** |    **0.00** | **0.8469** | **0.0305** |   **10696 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  7,219.8 ns | 968.91 ns | 906.32 ns |  7,084.6 ns |  1.18 |    0.14 | 0.8545 | 0.0305 |   10728 B |        1.00 |
