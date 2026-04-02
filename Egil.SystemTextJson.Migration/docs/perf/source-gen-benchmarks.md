```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8117)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                  | Categories                    | TagCount | Mean        | Error        | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |------------------------------ |--------- |------------:|-------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **2**        |  **1,265.2 ns** |     **38.73 ns** |     **5.99 ns** |  **2.22** |    **0.06** | **0.0896** |      **-** |    **1144 B** |        **1.13** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 2        |    569.8 ns |     63.74 ns |    16.55 ns |  1.00 |    0.04 | 0.0801 |      - |    1008 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **32**       |  **3,420.5 ns** |     **96.45 ns** |    **14.93 ns** |  **1.84** |    **0.07** | **0.2441** |      **-** |    **3104 B** |        **1.05** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 32       |  1,861.7 ns |    288.11 ns |    74.82 ns |  1.00 |    0.05 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **256**      | **19,359.7 ns** |  **2,076.33 ns** |   **539.22 ns** |  **1.76** |    **0.06** | **1.3733** | **0.0916** |   **17512 B** |        **1.01** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 256      | 11,008.5 ns |    908.50 ns |   235.93 ns |  1.00 |    0.03 | 1.3733 | 0.1068 |   17376 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **2**        |    **870.9 ns** |     **13.46 ns** |     **2.08 ns** |  **1.46** |    **0.01** | **0.0696** |      **-** |     **880 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 2        |    594.9 ns |     21.43 ns |     5.56 ns |  1.00 |    0.01 | 0.0696 |      - |     880 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **32**       |  **2,617.4 ns** |    **114.73 ns** |    **29.80 ns** |  **1.39** |    **0.03** | **0.2251** |      **-** |    **2840 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 32       |  1,877.5 ns |    181.67 ns |    47.18 ns |  1.00 |    0.03 | 0.2251 | 0.0019 |    2840 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **256**      | **13,539.9 ns** | **13,087.41 ns** | **3,398.76 ns** |  **1.27** |    **0.29** | **1.3733** | **0.1068** |   **17248 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 256      | 10,625.2 ns |    785.42 ns |   203.97 ns |  1.00 |    0.02 | 1.3733 | 0.1068 |   17248 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **2**        |    **507.8 ns** |     **36.20 ns** |     **9.40 ns** |  **1.00** |    **0.02** | **0.0668** |      **-** |     **848 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 2        |    555.2 ns |     78.92 ns |    20.50 ns |  1.09 |    0.04 | 0.0668 |      - |     848 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 2        |    837.1 ns |     37.36 ns |     9.70 ns |  1.65 |    0.03 | 0.0668 |      - |     848 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **32**       |  **1,764.6 ns** |    **141.88 ns** |    **36.85 ns** |  **1.00** |    **0.03** | **0.2232** | **0.0019** |    **2808 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 32       |  1,847.3 ns |    133.86 ns |    34.76 ns |  1.05 |    0.03 | 0.2232 | 0.0019 |    2808 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 32       |  2,517.7 ns |     51.51 ns |    13.38 ns |  1.43 |    0.03 | 0.2213 |      - |    2808 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **256**      | **10,311.8 ns** |    **205.52 ns** |    **53.37 ns** |  **1.00** |    **0.01** | **1.3580** | **0.1068** |   **17216 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 256      | 10,582.2 ns |    595.13 ns |   154.55 ns |  1.03 |    0.01 | 1.3580 | 0.1068 |   17216 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 256      | 14,716.7 ns |  1,568.60 ns |   407.36 ns |  1.43 |    0.04 | 1.3580 | 0.1068 |   17216 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **2**        |    **962.6 ns** |     **23.72 ns** |     **6.16 ns** |  **2.70** |    **0.08** | **0.0896** |      **-** |    **1136 B** |        **1.13** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 2        |    357.0 ns |     42.85 ns |    11.13 ns |  1.00 |    0.04 | 0.0801 |      - |    1008 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **32**       |  **3,264.0 ns** |  **1,663.61 ns** |   **432.03 ns** |  **1.82** |    **0.22** | **0.2441** |      **-** |    **3096 B** |        **1.04** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 32       |  1,793.6 ns |    160.26 ns |    41.62 ns |  1.00 |    0.03 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **256**      | **19,010.3 ns** |  **1,197.21 ns** |   **310.91 ns** |  **1.77** |    **0.04** | **1.3733** | **0.0916** |   **17504 B** |        **1.01** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 256      | 10,762.2 ns |  1,338.63 ns |   207.15 ns |  1.00 |    0.02 | 1.3733 | 0.1068 |   17376 B |        1.00 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **2**        |    **171.1 ns** |     **14.52 ns** |     **2.25 ns** |  **1.00** |    **0.02** | **0.0069** |      **-** |      **88 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 2        |    346.5 ns |     10.70 ns |     2.78 ns |  2.03 |    0.03 | 0.0348 |      - |     440 B |        5.00 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 2        |    332.6 ns |     51.77 ns |     8.01 ns |  1.94 |    0.05 | 0.0381 |      - |     480 B |        5.45 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **32**       |    **890.5 ns** |     **19.77 ns** |     **5.13 ns** |  **1.00** |    **0.01** | **0.0305** |      **-** |     **384 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 32       |    974.1 ns |     21.93 ns |     3.39 ns |  1.09 |    0.01 | 0.0591 |      - |     744 B |        1.94 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 32       |    987.2 ns |     44.09 ns |     6.82 ns |  1.11 |    0.01 | 0.0610 |      - |     776 B |        2.02 |
|                                         |                               |          |             |              |             |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **256**      |  **6,264.4 ns** |     **78.34 ns** |    **20.34 ns** |  **1.00** |    **0.00** | **0.2060** |      **-** |    **2624 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 256      |  5,578.8 ns |    112.74 ns |    29.28 ns |  0.89 |    0.01 | 0.2365 |      - |    2984 B |        1.14 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 256      |  5,879.6 ns |     49.27 ns |     7.63 ns |  0.94 |    0.00 | 0.2365 |      - |    3016 B |        1.15 |
