```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8117)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                  | Categories                    | TagCount | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |------------------------------ |--------- |------------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **2**        |    **971.0 ns** |    **33.97 ns** |     **8.82 ns** |  **2.97** |    **0.03** | **0.0925** |      **-** |    **1168 B** |        **1.13** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 2        |    326.8 ns |     5.79 ns |     1.50 ns |  1.00 |    0.01 | 0.0820 |      - |    1032 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **32**       |  **2,050.5 ns** |    **72.06 ns** |    **18.71 ns** |  **2.13** |    **0.02** | **0.2480** | **0.0038** |    **3128 B** |        **1.05** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 32       |    960.5 ns |    20.87 ns |     5.42 ns |  1.00 |    0.01 | 0.2384 | 0.0029 |    2992 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **256**      |  **9,367.6 ns** |   **150.45 ns** |    **39.07 ns** |  **0.89** |    **0.01** | **1.3885** | **0.1373** |   **17536 B** |        **1.01** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 256      | 10,473.7 ns |   299.54 ns |    77.79 ns |  1.00 |    0.01 | 1.3809 | 0.1373 |   17400 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **2**        |    **806.5 ns** |    **27.73 ns** |     **4.29 ns** |  **2.06** |    **0.45** | **0.0715** |      **-** |     **904 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 2        |    411.2 ns |   411.97 ns |   106.99 ns |  1.05 |    0.34 | 0.0715 |      - |     904 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **32**       |  **2,516.1 ns** |   **738.19 ns** |   **114.24 ns** |  **1.37** |    **0.06** | **0.2270** | **0.0019** |    **2864 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 32       |  1,843.5 ns |   170.78 ns |    26.43 ns |  1.00 |    0.02 | 0.2270 | 0.0019 |    2864 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **256**      | **14,732.7 ns** | **1,056.72 ns** |   **274.43 ns** |  **1.39** |    **0.04** | **1.3733** | **0.1221** |   **17272 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 256      | 10,577.9 ns |   973.53 ns |   252.82 ns |  1.00 |    0.03 | 1.3733 | 0.1221 |   17272 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **2**        |    **465.7 ns** |    **17.81 ns** |     **2.76 ns** |  **1.00** |    **0.01** | **0.0687** |      **-** |     **872 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 2        |    524.3 ns |    10.94 ns |     2.84 ns |  1.13 |    0.01 | 0.0687 |      - |     872 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 2        |    803.1 ns |    39.18 ns |    10.17 ns |  1.72 |    0.02 | 0.0687 |      - |     872 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **32**       |  **1,463.5 ns** | **1,202.52 ns** |   **312.29 ns** |  **1.04** |    **0.30** | **0.2251** | **0.0019** |    **2832 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 32       |  1,750.6 ns |    42.99 ns |    11.16 ns |  1.25 |    0.26 | 0.2251 | 0.0019 |    2832 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 32       |  2,491.5 ns |    92.20 ns |    23.94 ns |  1.77 |    0.38 | 0.2251 |      - |    2832 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **256**      | **10,284.3 ns** |   **527.70 ns** |   **137.04 ns** |  **1.00** |    **0.02** | **1.3733** | **0.1221** |   **17240 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 256      | 10,316.4 ns |   668.44 ns |   173.59 ns |  1.00 |    0.02 | 1.3733 | 0.1221 |   17240 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 256      | 14,282.0 ns |   435.04 ns |    67.32 ns |  1.39 |    0.02 | 1.3733 | 0.1221 |   17240 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **2**        |  **1,231.3 ns** |    **75.50 ns** |    **19.61 ns** |  **2.40** |    **0.04** | **0.0916** |      **-** |    **1160 B** |        **1.12** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 2        |    512.4 ns |    35.17 ns |     5.44 ns |  1.00 |    0.01 | 0.0820 |      - |    1032 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **32**       |  **3,430.7 ns** |   **163.95 ns** |    **42.58 ns** |  **1.86** |    **0.06** | **0.2480** | **0.0038** |    **3120 B** |        **1.04** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 32       |  1,846.1 ns |   254.50 ns |    66.09 ns |  1.00 |    0.05 | 0.2384 | 0.0019 |    2992 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **256**      | **18,785.5 ns** |   **605.09 ns** |   **157.14 ns** |  **1.84** |    **0.02** | **1.3733** | **0.1221** |   **17528 B** |        **1.01** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 256      | 10,217.5 ns |   530.67 ns |    82.12 ns |  1.00 |    0.01 | 1.3733 | 0.1221 |   17400 B |        1.00 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **2**        |    **249.3 ns** |     **3.21 ns** |     **0.50 ns** |  **1.00** |    **0.00** | **0.0315** |      **-** |     **400 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 2        |    340.6 ns |    16.60 ns |     4.31 ns |  1.37 |    0.02 | 0.0348 |      - |     440 B |        1.10 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 2        |    313.8 ns |    15.32 ns |     3.98 ns |  1.26 |    0.01 | 0.0381 |      - |     480 B |        1.20 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **32**       |    **915.1 ns** |    **75.31 ns** |    **19.56 ns** |  **1.00** |    **0.03** | **0.0553** |      **-** |     **696 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 32       |  1,003.5 ns |    27.78 ns |     7.22 ns |  1.10 |    0.02 | 0.0591 |      - |     744 B |        1.07 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 32       |    984.9 ns |   135.83 ns |    35.27 ns |  1.08 |    0.04 | 0.0610 |      - |     776 B |        1.11 |
|                                         |                               |          |             |             |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **256**      |  **4,865.1 ns** | **4,785.54 ns** | **1,242.79 ns** |  **1.08** |    **0.44** | **0.2289** |      **-** |    **2936 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 256      |  5,740.4 ns |   154.44 ns |    23.90 ns |  1.27 |    0.41 | 0.2365 |      - |    2984 B |        1.02 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 256      |  5,854.2 ns |   600.68 ns |   156.00 ns |  1.30 |    0.42 | 0.2365 |      - |    3016 B |        1.03 |
