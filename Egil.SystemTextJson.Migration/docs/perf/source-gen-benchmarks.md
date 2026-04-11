```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8117)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                  | Categories                    | TagCount | Mean        | Error         | StdDev       | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |------------------------------ |--------- |------------:|--------------:|-------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **2**        |   **532.19 ns** |      **6.297 ns** |     **1.635 ns** |  **1.50** |    **0.01** | **0.0906** |      **-** |    **1144 B** |        **1.13** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 2        |   354.75 ns |      7.036 ns |     1.827 ns |  1.00 |    0.01 | 0.0801 |      - |    1008 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **32**       | **1,218.09 ns** |     **13.330 ns** |     **3.462 ns** |  **1.18** |    **0.01** | **0.2460** | **0.0019** |    **3104 B** |        **1.05** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 32       | 1,032.50 ns |     26.009 ns |     6.754 ns |  1.00 |    0.01 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**         | **Deserialize,ExternalMigration** | **256**      | **5,753.82 ns** |    **114.869 ns** |    **29.831 ns** |  **1.04** |    **0.02** | **1.3885** | **0.1068** |   **17512 B** |        **1.01** |
| PlainStjExternalMigrationManual         | Deserialize,ExternalMigration | 256      | 5,544.82 ns |    346.269 ns |    89.925 ns |  1.00 |    0.02 | 1.3809 | 0.1144 |   17376 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **2**        |   **411.91 ns** |     **23.743 ns** |     **3.674 ns** |  **1.16** |    **0.01** | **0.0701** |      **-** |     **880 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 2        |   356.45 ns |     11.833 ns |     3.073 ns |  1.00 |    0.01 | 0.0701 |      - |     880 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **32**       | **1,069.53 ns** |     **29.367 ns** |     **4.545 ns** |  **1.05** |    **0.01** | **0.2251** | **0.0019** |    **2840 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 32       | 1,017.64 ns |     23.083 ns |     5.995 ns |  1.00 |    0.01 | 0.2251 | 0.0019 |    2840 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**             | **Deserialize,LegacyPayload**     | **256**      | **5,716.62 ns** |    **365.545 ns** |    **94.931 ns** |  **0.82** |    **0.20** | **1.3733** | **0.1144** |   **17248 B** |        **1.00** |
| PlainStjLegacyPayloadManual             | Deserialize,LegacyPayload     | 256      | 7,394.41 ns | 13,156.649 ns | 2,036.006 ns |  1.06 |    0.37 | 1.3733 | 0.1144 |   17248 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **2**        |   **469.33 ns** |     **39.333 ns** |     **6.087 ns** |  **1.00** |    **0.02** | **0.0668** |      **-** |     **848 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 2        |   516.91 ns |     11.987 ns |     3.113 ns |  1.10 |    0.01 | 0.0668 |      - |     848 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 2        |   586.99 ns |     25.298 ns |     6.570 ns |  1.25 |    0.02 | 0.0668 |      - |     848 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **32**       | **1,391.81 ns** |    **293.564 ns** |    **76.238 ns** |  **1.00** |    **0.07** | **0.2232** |      **-** |    **2808 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 32       |   990.08 ns |     16.216 ns |     2.509 ns |  0.71 |    0.04 | 0.2232 |      - |    2808 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 32       | 1,049.61 ns |     42.667 ns |    11.081 ns |  0.76 |    0.04 | 0.2232 | 0.0019 |    2808 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                     | **Deserialize,NoMigration**       | **256**      | **5,371.34 ns** |    **290.755 ns** |    **75.508 ns** |  **1.00** |    **0.02** | **1.3657** | **0.1068** |   **17216 B** |        **1.00** |
| PolymorphicPlainStjNoMigration          | Deserialize,NoMigration       | 256      | 5,403.22 ns |    474.154 ns |    73.376 ns |  1.01 |    0.02 | 1.3657 | 0.1068 |   17216 B |        1.00 |
| JsonMigratableNoMigration               | Deserialize,NoMigration       | 256      | 5,453.46 ns |     56.438 ns |     8.734 ns |  1.02 |    0.01 | 1.3657 | 0.1068 |   17216 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **2**        |   **544.65 ns** |     **24.360 ns** |     **3.770 ns** |  **1.43** |    **0.01** | **0.0896** |      **-** |    **1136 B** |        **1.13** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 2        |   379.62 ns |     16.715 ns |     2.587 ns |  1.00 |    0.01 | 0.0801 |      - |    1008 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **32**       | **1,271.72 ns** |     **57.484 ns** |    **14.928 ns** |  **1.22** |    **0.02** | **0.2460** | **0.0019** |    **3096 B** |        **1.04** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 32       | 1,045.61 ns |     36.755 ns |     9.545 ns |  1.00 |    0.01 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**           | **Deserialize,StaticMigration**   | **256**      | **5,845.01 ns** |    **449.893 ns** |   **116.836 ns** |  **1.06** |    **0.02** | **1.3885** | **0.1144** |   **17504 B** |        **1.01** |
| PlainStjStaticMigrationManual           | Deserialize,StaticMigration   | 256      | 5,535.72 ns |    244.447 ns |    63.482 ns |  1.00 |    0.01 | 1.3809 | 0.1144 |   17376 B |        1.00 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **2**        |    **93.45 ns** |      **5.564 ns** |     **1.445 ns** |  **1.00** |    **0.02** | **0.0069** |      **-** |      **88 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 2        |   224.60 ns |     17.018 ns |     4.419 ns |  2.40 |    0.06 | 0.0350 |      - |     440 B |        5.00 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 2        |   196.40 ns |      7.253 ns |     1.884 ns |  2.10 |    0.04 | 0.0381 |      - |     480 B |        5.45 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **32**       |   **469.83 ns** |     **10.670 ns** |     **1.651 ns** |  **1.00** |    **0.00** | **0.0305** |      **-** |     **384 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 32       |   551.10 ns |      7.936 ns |     2.061 ns |  1.17 |    0.01 | 0.0591 |      - |     744 B |        1.94 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 32       |   551.24 ns |     23.090 ns |     5.996 ns |  1.17 |    0.01 | 0.0610 |      - |     776 B |        2.02 |
|                                         |                               |          |             |               |              |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**            | **Serialize,NoMigration**         | **256**      | **3,231.88 ns** |     **67.147 ns** |    **17.438 ns** |  **1.00** |    **0.01** | **0.2060** |      **-** |    **2624 B** |        **1.00** |
| PolymorphicPlainStjSerializeNoMigration | Serialize,NoMigration         | 256      | 2,864.80 ns |    106.002 ns |    27.528 ns |  0.89 |    0.01 | 0.2365 |      - |    2984 B |        1.14 |
| JsonMigratableSerializeNoMigration      | Serialize,NoMigration         | 256      | 3,016.84 ns |     99.137 ns |    25.746 ns |  0.93 |    0.01 | 0.2403 |      - |    3016 B |        1.15 |
