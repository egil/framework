# Reflection Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8117)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                  | Categories                    | TagCount | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |------------------------------ |--------- |-----------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **2**        |   **500.3 ns** |  **17.70 ns** |  **4.60 ns** |  **1.51** |    **0.01** | **0.0925** |      **-** |    **1168 B** |        **1.13** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 2        |   330.4 ns |   6.64 ns |  1.03 ns |  1.00 |    0.00 | 0.0820 |      - |    1032 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **32**       | **1,153.3 ns** |  **38.87 ns** | **10.09 ns** |  **1.20** |    **0.01** | **0.2480** | **0.0038** |    **3128 B** |        **1.05** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 32       |   959.8 ns |   9.42 ns |  1.46 ns |  1.00 |    0.00 | 0.2384 | 0.0019 |    2992 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **256**      | **5,541.6 ns** | **278.67 ns** | **43.12 ns** |  **1.04** |    **0.01** | **1.3962** | **0.1373** |   **17536 B** |        **1.01** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 256      | 5,347.9 ns | 188.47 ns | 48.95 ns |  1.00 |    0.01 | 1.3809 | 0.1373 |   17400 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **2**        |   **391.6 ns** |  **22.74 ns** |  **3.52 ns** |  **1.17** |    **0.02** | **0.0720** |      **-** |     **904 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 2        |   334.8 ns |  14.50 ns |  3.77 ns |  1.00 |    0.01 | 0.0720 |      - |     904 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **32**       | **1,057.5 ns** |  **71.07 ns** | **11.00 ns** |  **1.05** |    **0.01** | **0.2270** | **0.0019** |    **2864 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 32       | 1,005.3 ns |  37.09 ns |  9.63 ns |  1.00 |    0.01 | 0.2270 | 0.0019 |    2864 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **256**      | **5,512.5 ns** |  **78.17 ns** | **20.30 ns** |  **1.02** |    **0.01** | **1.3733** | **0.1297** |   **17272 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 256      | 5,424.0 ns | 159.87 ns | 41.52 ns |  1.00 |    0.01 | 1.3733 | 0.1297 |   17272 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **2**        |   **290.5 ns** |   **7.10 ns** |  **1.84 ns** |  **1.00** |    **0.01** | **0.0691** |      **-** |     **872 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 2        |   323.3 ns |  10.98 ns |  1.70 ns |  1.11 |    0.01 | 0.0691 |      - |     872 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 2        |   376.3 ns |  12.70 ns |  1.96 ns |  1.30 |    0.01 | 0.0691 |      - |     872 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **32**       |   **954.2 ns** |  **57.83 ns** | **15.02 ns** |  **1.00** |    **0.02** | **0.2251** | **0.0029** |    **2832 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 32       |   986.3 ns |  35.78 ns |  9.29 ns |  1.03 |    0.02 | 0.2251 | 0.0019 |    2832 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 32       | 1,028.9 ns |  48.44 ns |  7.50 ns |  1.08 |    0.02 | 0.2251 | 0.0019 |    2832 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **256**      | **5,288.3 ns** | **256.32 ns** | **66.57 ns** |  **1.00** |    **0.02** | **1.3733** | **0.1297** |   **17240 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 256      | 5,456.1 ns | 106.67 ns | 27.70 ns |  1.03 |    0.01 | 1.3733 | 0.1297 |   17240 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 256      | 5,469.0 ns | 291.00 ns | 75.57 ns |  1.03 |    0.02 | 1.3733 | 0.1297 |   17240 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **2**        |   **519.2 ns** |  **13.71 ns** |  **3.56 ns** |  **1.56** |    **0.01** | **0.0916** |      **-** |    **1160 B** |        **1.12** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 2        |   333.4 ns |   8.02 ns |  1.24 ns |  1.00 |    0.00 | 0.0820 |      - |    1032 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **32**       | **1,170.8 ns** |  **62.26 ns** | **16.17 ns** |  **1.13** |    **0.02** | **0.2480** | **0.0038** |    **3120 B** |        **1.04** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 32       | 1,039.7 ns |  63.15 ns | 16.40 ns |  1.00 |    0.02 | 0.2384 | 0.0019 |    2992 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **256**      | **5,653.1 ns** | **206.52 ns** | **53.63 ns** |  **1.03** |    **0.02** | **1.3962** | **0.1373** |   **17528 B** |        **1.01** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 256      | 5,499.5 ns | 308.17 ns | 80.03 ns |  1.00 |    0.02 | 1.3809 | 0.1373 |   17400 B |        1.00 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **2**        |   **146.7 ns** |   **2.15 ns** |  **0.33 ns** |  **1.00** |    **0.00** | **0.0317** |      **-** |     **400 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 2        |   224.0 ns |   1.30 ns |  0.34 ns |  1.53 |    0.00 | 0.0350 |      - |     440 B |        1.10 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 2        |   188.3 ns |   2.64 ns |  0.69 ns |  1.28 |    0.01 | 0.0381 |      - |     480 B |        1.20 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **32**       |   **499.0 ns** |   **2.84 ns** |  **0.74 ns** |  **1.00** |    **0.00** | **0.0553** |      **-** |     **696 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 32       |   574.5 ns |  13.62 ns |  3.54 ns |  1.15 |    0.01 | 0.0591 |      - |     744 B |        1.07 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 32       |   551.2 ns |   5.10 ns |  1.32 ns |  1.10 |    0.00 | 0.0610 |      - |     776 B |        1.11 |
|                                         |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **256**      | **2,962.1 ns** | **118.41 ns** | **30.75 ns** |  **1.00** |    **0.01** | **0.2327** |      **-** |    **2936 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 256      | 3,044.5 ns | 135.97 ns | 35.31 ns |  1.03 |    0.01 | 0.2365 |      - |    2984 B |        1.02 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 256      | 3,034.6 ns |  56.42 ns | 14.65 ns |  1.02 |    0.01 | 0.2403 |      - |    3016 B |        1.03 |
