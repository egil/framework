```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8039)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                             | Categories                    | TagCount | Mean        | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------------------ |--------- |------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **2**        |   **943.47 ns** |  **34.585 ns** |   **5.352 ns** |  **2.70** |    **0.03** | **0.0896** |      **-** |    **1144 B** |        **1.13** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 2        |   348.93 ns |  20.033 ns |   3.100 ns |  1.00 |    0.01 | 0.0801 |      - |    1008 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **32**       | **2,139.35 ns** | **140.885 ns** |  **21.802 ns** |  **2.14** |    **0.03** | **0.2441** |      **-** |    **3104 B** |        **1.05** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 32       |   998.31 ns |  35.274 ns |   9.160 ns |  1.00 |    0.01 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **256**      | **9,984.50 ns** | **228.456 ns** |  **59.329 ns** |  **1.89** |    **0.02** | **1.3885** | **0.1068** |   **17512 B** |        **1.01** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 256      | 5,269.86 ns | 153.044 ns |  39.745 ns |  1.00 |    0.01 | 1.3809 | 0.1144 |   17376 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **2**        |   **605.39 ns** |  **19.313 ns** |   **2.989 ns** |  **1.75** |    **0.01** | **0.0696** |      **-** |     **880 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 2        |   345.30 ns |  12.984 ns |   2.009 ns |  1.00 |    0.01 | 0.0701 |      - |     880 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **32**       | **1,548.54 ns** |  **39.843 ns** |  **10.347 ns** |  **1.54** |    **0.02** | **0.2251** | **0.0019** |    **2840 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 32       | 1,002.81 ns |  59.614 ns |   9.225 ns |  1.00 |    0.01 | 0.2251 | 0.0019 |    2840 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **256**      | **7,704.27 ns** | **423.257 ns** | **109.918 ns** |  **1.44** |    **0.02** | **1.3733** | **0.1068** |   **17248 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 256      | 5,363.43 ns | 165.417 ns |  42.958 ns |  1.00 |    0.01 | 1.3733 | 0.1144 |   17248 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **2**        |   **310.17 ns** |  **20.410 ns** |   **5.300 ns** |  **1.00** |    **0.02** | **0.0672** |      **-** |     **848 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 2        |   605.47 ns |  26.382 ns |   6.851 ns |  1.95 |    0.04 | 0.0668 |      - |     848 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **32**       | **1,000.72 ns** |  **29.678 ns** |   **4.593 ns** |  **1.00** |    **0.01** | **0.2232** | **0.0019** |    **2808 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 32       | 1,559.62 ns |  59.773 ns |  15.523 ns |  1.56 |    0.02 | 0.2232 | 0.0019 |    2808 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **256**      | **5,482.68 ns** | **286.327 ns** |  **44.309 ns** |  **1.00** |    **0.01** | **1.3657** | **0.1068** |   **17216 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 256      | 7,629.14 ns | 296.564 ns |  77.017 ns |  1.39 |    0.02 | 1.3580 | 0.1068 |   17216 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **2**        |   **956.14 ns** |   **9.169 ns** |   **2.381 ns** |  **2.73** |    **0.02** | **0.0896** |      **-** |    **1136 B** |        **1.13** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 2        |   350.48 ns |   9.864 ns |   2.562 ns |  1.00 |    0.01 | 0.0801 |      - |    1008 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **32**       | **2,144.21 ns** |  **52.897 ns** |  **13.737 ns** |  **2.12** |    **0.02** | **0.2441** |      **-** |    **3096 B** |        **1.04** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 32       | 1,011.16 ns |  26.728 ns |   6.941 ns |  1.00 |    0.01 | 0.2365 | 0.0019 |    2968 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **256**      | **9,912.87 ns** | **527.970 ns** | **137.112 ns** |  **1.83** |    **0.02** | **1.3885** | **0.1068** |   **17504 B** |        **1.01** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 256      | 5,428.16 ns |  63.817 ns |   9.876 ns |  1.00 |    0.00 | 1.3809 | 0.1144 |   17376 B |        1.00 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **2**        |    **90.17 ns** |   **3.951 ns** |   **1.026 ns** |  **1.00** |    **0.01** | **0.0069** |      **-** |      **88 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 2        |   194.28 ns |   5.343 ns |   1.388 ns |  2.15 |    0.03 | 0.0381 |      - |     480 B |        5.45 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **32**       |   **458.15 ns** |  **13.045 ns** |   **3.388 ns** |  **1.00** |    **0.01** | **0.0305** |      **-** |     **384 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 32       |   552.56 ns |  17.949 ns |   4.661 ns |  1.21 |    0.01 | 0.0610 |      - |     776 B |        2.02 |
|                                    |                               |          |             |            |            |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **256**      | **3,125.41 ns** | **201.272 ns** |  **52.270 ns** |  **1.00** |    **0.02** | **0.2060** |      **-** |    **2624 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 256      | 3,087.17 ns |  47.966 ns |  12.457 ns |  0.99 |    0.02 | 0.2403 |      - |    3016 B |        1.15 |
