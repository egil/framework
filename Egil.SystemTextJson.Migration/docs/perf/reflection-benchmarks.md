# Reflection Benchmarks

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
| Method                                       | Categories                                 | TagCount | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |--------- |-----------:|------------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **2**        |   **641.7 ns** |    **26.62 ns** |   **6.91 ns** |  **1.38** |    **0.02** | **0.0610** |      **-** |    **1032 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 2        |   466.2 ns |    11.40 ns |   2.96 ns |  1.00 |    0.01 | 0.0615 |      - |    1032 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **32**       | **1,653.2 ns** |    **20.74 ns** |   **5.39 ns** |  **1.13** |    **0.01** | **0.1774** | **0.0019** |    **2992 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 32       | 1,461.3 ns |    39.66 ns |  10.30 ns |  1.00 |    0.01 | 0.1774 | 0.0019 |    2992 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **256**      | **8,622.3 ns** |   **215.99 ns** |  **56.09 ns** |  **1.02** |    **0.01** | **1.0376** | **0.0916** |   **17400 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | 256      | 8,456.8 ns |   336.65 ns |  87.43 ns |  1.00 |    0.01 | 1.0376 | 0.0916 |   17400 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **2**        |   **549.8 ns** |    **26.55 ns** |   **6.89 ns** |  **1.18** |    **0.01** | **0.0534** |      **-** |     **904 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 2        |   467.2 ns |     9.61 ns |   2.50 ns |  1.00 |    0.01 | 0.0539 |      - |     904 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **32**       | **1,570.0 ns** |    **40.64 ns** |   **6.29 ns** |  **1.06** |    **0.01** | **0.1698** | **0.0019** |    **2864 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 32       | 1,484.4 ns |    34.00 ns |   8.83 ns |  1.00 |    0.01 | 0.1698 | 0.0019 |    2864 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **256**      | **8,480.5 ns** |   **198.67 ns** |  **51.60 ns** |  **1.02** |    **0.01** | **1.0223** | **0.0916** |   **17272 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | 256      | 8,296.1 ns |   265.61 ns |  68.98 ns |  1.00 |    0.01 | 1.0223 | 0.0916 |   17272 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **2**        |   **414.7 ns** |     **6.40 ns** |   **1.66 ns** |  **1.00** |    **0.01** | **0.0520** |      **-** |     **872 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 2        |   453.9 ns |     6.39 ns |   0.99 ns |  1.09 |    0.00 | 0.0520 |      - |     872 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 2        |   510.9 ns |    12.45 ns |   1.93 ns |  1.23 |    0.01 | 0.0515 |      - |     872 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **32**       | **1,392.1 ns** |    **24.82 ns** |   **6.45 ns** |  **1.00** |    **0.01** | **0.1678** | **0.0019** |    **2832 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 32       | 1,436.1 ns |    28.64 ns |   7.44 ns |  1.03 |    0.01 | 0.1678 | 0.0019 |    2832 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 32       | 1,522.4 ns |    37.28 ns |   9.68 ns |  1.09 |    0.01 | 0.1678 | 0.0019 |    2832 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **256**      | **8,212.0 ns** |   **176.11 ns** |  **27.25 ns** |  **1.00** |    **0.00** | **1.0223** | **0.0916** |   **17240 B** |        **1.00** |
| PolymorphicPlainStjNoMigration               | Deserialize,NoMigration                    | 256      | 8,254.3 ns |   113.97 ns |  29.60 ns |  1.01 |    0.00 | 1.0223 | 0.0916 |   17240 B |        1.00 |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | 256      | 8,307.1 ns |   124.08 ns |  19.20 ns |  1.01 |    0.00 | 1.0223 | 0.0916 |   17240 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **2**        |   **642.4 ns** |     **8.30 ns** |   **2.16 ns** |  **1.42** |    **0.01** | **0.0610** |      **-** |    **1032 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 2        |   452.3 ns |     8.31 ns |   2.16 ns |  1.00 |    0.01 | 0.0615 |      - |    1032 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **32**       | **1,646.7 ns** |    **25.86 ns** |   **4.00 ns** |  **1.14** |    **0.00** | **0.1774** | **0.0019** |    **2992 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 32       | 1,441.5 ns |    14.68 ns |   3.81 ns |  1.00 |    0.00 | 0.1774 | 0.0019 |    2992 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **256**      | **8,581.1 ns** |   **406.20 ns** | **105.49 ns** |  **1.03** |    **0.01** | **1.0376** | **0.0916** |   **17400 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | 256      | 8,345.4 ns |   141.01 ns |  36.62 ns |  1.00 |    0.01 | 1.0376 | 0.0916 |   17400 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **2**        |   **555.0 ns** |    **16.97 ns** |   **2.63 ns** |  **1.14** |    **0.01** | **0.0610** |      **-** |    **1032 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 2        |   484.9 ns |    11.86 ns |   3.08 ns |  1.00 |    0.01 | 0.0610 |      - |    1032 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **32**       | **1,554.2 ns** |    **37.16 ns** |   **9.65 ns** |  **1.07** |    **0.01** | **0.1774** | **0.0019** |    **2992 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 32       | 1,458.8 ns |    52.00 ns |  13.50 ns |  1.00 |    0.01 | 0.1774 | 0.0019 |    2992 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **256**      | **9,609.7 ns** | **1,446.95 ns** | **375.77 ns** |  **1.00** |    **0.05** | **1.0376** | **0.0916** |   **17400 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | 256      | 9,617.4 ns | 1,562.82 ns | 405.86 ns |  1.00 |    0.05 | 1.0376 | 0.0916 |   17400 B |        1.00 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **2**        |   **235.9 ns** |    **33.40 ns** |   **5.17 ns** |  **1.00** |    **0.03** | **0.0238** |      **-** |     **400 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 2        |   338.0 ns |    75.35 ns |  19.57 ns |  1.43 |    0.08 | 0.0262 |      - |     440 B |        1.10 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 2        |   294.9 ns |    42.36 ns |   6.56 ns |  1.25 |    0.03 | 0.0286 |      - |     480 B |        1.20 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **32**       |   **771.0 ns** |   **104.49 ns** |  **27.14 ns** |  **1.00** |    **0.05** | **0.0410** |      **-** |     **696 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 32       |   847.5 ns |   130.34 ns |  33.85 ns |  1.10 |    0.05 | 0.0439 |      - |     744 B |        1.07 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 32       |   714.0 ns |     9.64 ns |   1.49 ns |  0.93 |    0.03 | 0.0458 |      - |     776 B |        1.11 |
|                                              |                                            |          |            |             |           |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**                 | **Serialize,NoMigration**                      | **256**      | **3,810.3 ns** |    **34.32 ns** |   **8.91 ns** |  **1.00** |    **0.00** | **0.1755** |      **-** |    **2936 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration      | Serialize,NoMigration                      | 256      | 3,919.9 ns |   110.11 ns |  17.04 ns |  1.03 |    0.00 | 0.1755 |      - |    2984 B |        1.02 |
| JsonMigratableSerializeNoMigration           | Serialize,NoMigration                      | 256      | 3,875.3 ns |    18.89 ns |   2.92 ns |  1.02 |    0.00 | 0.1755 |      - |    3016 B |        1.03 |
