# Source-Generated Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8328)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.203
  [Host] : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                       | Categories                                 | TagCount | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |--------- |-----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **2**        |   **663.6 ns** |  **19.49 ns** |   **3.02 ns** |  **1.37** |    **0.01** | **0.0601** |      **-** |    **1008 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 2        |   485.3 ns |  13.54 ns |   3.52 ns |  1.00 |    0.01 | 0.0601 |      - |    1008 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **32**       | **1,661.4 ns** |  **25.40 ns** |   **3.93 ns** |  **1.13** |    **0.01** | **0.1774** | **0.0019** |    **2968 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 32       | 1,472.9 ns |  38.22 ns |   9.92 ns |  1.00 |    0.01 | 0.1774 | 0.0019 |    2968 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **256**      | **8,513.6 ns** | **139.64 ns** |  **36.26 ns** |  **1.01** |    **0.01** | **1.0376** | **0.0916** |   **17376 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 256      | 8,406.2 ns | 234.40 ns |  60.87 ns |  1.00 |    0.01 | 1.0376 | 0.0916 |   17376 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **2**        |   **566.9 ns** |   **7.70 ns** |   **2.00 ns** |  **1.13** |    **0.01** | **0.0525** |      **-** |     **880 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 2        |   499.6 ns |  13.66 ns |   3.55 ns |  1.00 |    0.01 | 0.0525 |      - |     880 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **32**       | **1,586.7 ns** |  **21.27 ns** |   **5.52 ns** |  **1.07** |    **0.00** | **0.1698** | **0.0019** |    **2840 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 32       | 1,486.1 ns |  20.81 ns |   5.40 ns |  1.00 |    0.00 | 0.1698 | 0.0019 |    2840 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **256**      | **8,510.0 ns** | **260.11 ns** |  **67.55 ns** |  **1.01** |    **0.01** | **1.0223** | **0.0916** |   **17248 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 256      | 8,423.3 ns | 223.72 ns |  58.10 ns |  1.00 |    0.01 | 1.0223 | 0.0916 |   17248 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **2**        |   **425.2 ns** |  **10.17 ns** |   **2.64 ns** |  **1.00** |    **0.01** | **0.0505** |      **-** |     **848 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 2        |   464.1 ns |  15.34 ns |   3.98 ns |  1.09 |    0.01 | 0.0505 |      - |     848 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 2        |   525.3 ns |   8.77 ns |   2.28 ns |  1.24 |    0.01 | 0.0505 |      - |     848 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **32**       | **1,405.9 ns** |  **36.50 ns** |   **9.48 ns** |  **1.00** |    **0.01** | **0.1678** | **0.0019** |    **2808 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 32       | 1,456.6 ns |  39.65 ns |  10.30 ns |  1.04 |    0.01 | 0.1678 | 0.0019 |    2808 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 32       | 1,517.7 ns |  27.40 ns |   7.12 ns |  1.08 |    0.01 | 0.1678 | 0.0019 |    2808 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **256**      | **8,399.5 ns** | **162.26 ns** |  **42.14 ns** |  **1.00** |    **0.01** | **1.0223** | **0.0916** |   **17216 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 256      | 8,468.0 ns | 416.86 ns | 108.26 ns |  1.01 |    0.01 | 1.0223 | 0.0916 |   17216 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 256      | 8,529.3 ns | 159.40 ns |  41.39 ns |  1.02 |    0.01 | 1.0223 | 0.0763 |   17216 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **2**        |   **699.2 ns** |  **21.49 ns** |   **5.58 ns** |  **1.42** |    **0.01** | **0.0601** |      **-** |    **1008 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 2        |   492.0 ns |  13.32 ns |   3.46 ns |  1.00 |    0.01 | 0.0601 |      - |    1008 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **32**       | **1,701.2 ns** |  **85.06 ns** |  **13.16 ns** |  **1.14** |    **0.01** | **0.1774** | **0.0019** |    **2968 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 32       | 1,491.8 ns |  28.94 ns |   7.52 ns |  1.00 |    0.01 | 0.1774 | 0.0019 |    2968 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **256**      | **8,565.8 ns** | **231.65 ns** |  **60.16 ns** |  **1.02** |    **0.01** | **1.0376** | **0.0763** |   **17376 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 256      | 8,437.9 ns | 186.70 ns |  48.48 ns |  1.00 |    0.01 | 1.0376 | 0.0763 |   17376 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **2**        |   **587.8 ns** |  **47.36 ns** |   **7.33 ns** |  **0.99** |    **0.08** | **0.0601** |      **-** |    **1008 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 2        |   599.2 ns | 189.12 ns |  49.11 ns |  1.01 |    0.11 | 0.0601 |      - |    1008 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **32**       | **1,847.2 ns** | **233.05 ns** |  **60.52 ns** |  **1.07** |    **0.10** | **0.1774** | **0.0019** |    **2968 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 32       | 1,737.2 ns | 655.45 ns | 170.22 ns |  1.01 |    0.13 | 0.1774 | 0.0019 |    2968 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **256**      | **8,540.4 ns** | **241.11 ns** |  **37.31 ns** |  **1.02** |    **0.01** | **1.0376** | **0.0763** |   **17376 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 256      | 8,364.0 ns | 192.45 ns |  49.98 ns |  1.00 |    0.01 | 1.0376 | 0.0763 |   17376 B |        1.00 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **2**        |   **144.2 ns** |   **1.42 ns** |   **0.37 ns** |  **1.00** |    **0.00** | **0.0052** |      **-** |      **88 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 2        |   290.8 ns |   7.82 ns |   2.03 ns |  2.02 |    0.01 | 0.0262 |      - |     440 B |        5.00 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 2        |   260.9 ns |   4.99 ns |   0.77 ns |  1.81 |    0.01 | 0.0286 |      - |     480 B |        5.45 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **32**       |   **658.6 ns** |   **8.84 ns** |   **2.30 ns** |  **1.00** |    **0.00** | **0.0229** |      **-** |     **384 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 32       |   710.9 ns |  13.79 ns |   3.58 ns |  1.08 |    0.01 | 0.0439 |      - |     744 B |        1.94 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 32       |   713.2 ns |   9.24 ns |   2.40 ns |  1.08 |    0.00 | 0.0458 |      - |     776 B |        2.02 |
|                                              |                                            |          |            |           |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **256**      | **4,343.2 ns** |  **33.28 ns** |   **8.64 ns** |  **1.00** |    **0.00** | **0.1526** |      **-** |    **2624 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 256      | 3,713.0 ns |  75.63 ns |  11.70 ns |  0.85 |    0.00 | 0.1755 |      - |    2984 B |        1.14 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 256      | 3,880.7 ns |  18.08 ns |   2.80 ns |  0.89 |    0.00 | 0.1755 |      - |    3016 B |        1.15 |
