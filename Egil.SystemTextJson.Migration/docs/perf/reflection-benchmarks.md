```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.8039)
13th Gen Intel Core i7-13800H 2.90GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                             | Categories                    | TagCount | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------------------ |--------- |-----------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **2**        | **1,010.8 ns** | **191.31 ns** | **49.68 ns** |  **3.04** |    **0.14** | **0.0925** |      **-** |    **1168 B** |        **1.13** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 2        |   332.0 ns |   3.88 ns |  1.01 ns |  1.00 |    0.00 | 0.0820 |      - |    1032 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **32**       | **2,048.3 ns** |  **76.40 ns** | **19.84 ns** |  **2.14** |    **0.02** | **0.2480** | **0.0038** |    **3128 B** |        **1.05** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 32       |   958.8 ns |  16.14 ns |  2.50 ns |  1.00 |    0.00 | 0.2384 | 0.0029 |    2992 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**    | **Deserialize,ExternalMigration** | **256**      | **9,745.0 ns** | **156.84 ns** | **40.73 ns** |  **1.86** |    **0.02** | **1.3885** | **0.1373** |   **17536 B** |        **1.01** |
| PlainStjExternalMigrationManual    | Deserialize,ExternalMigration | 256      | 5,246.1 ns | 204.00 ns | 52.98 ns |  1.00 |    0.01 | 1.3809 | 0.1373 |   17400 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **2**        |   **581.4 ns** |  **12.14 ns** |  **1.88 ns** |  **1.77** |    **0.01** | **0.0715** |      **-** |     **904 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 2        |   328.3 ns |   8.69 ns |  1.34 ns |  1.00 |    0.01 | 0.0720 |      - |     904 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **32**       | **1,493.7 ns** |  **42.98 ns** | **11.16 ns** |  **1.53** |    **0.01** | **0.2270** | **0.0019** |    **2864 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 32       |   978.6 ns |  31.07 ns |  4.81 ns |  1.00 |    0.01 | 0.2270 | 0.0019 |    2864 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**        | **Deserialize,LegacyPayload**     | **256**      | **7,545.0 ns** |  **88.44 ns** | **22.97 ns** |  **1.42** |    **0.01** | **1.3733** | **0.1297** |   **17272 B** |        **1.00** |
| PlainStjLegacyPayloadManual        | Deserialize,LegacyPayload     | 256      | 5,318.7 ns | 134.25 ns | 34.86 ns |  1.00 |    0.01 | 1.3733 | 0.1297 |   17272 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **2**        |   **285.6 ns** |   **8.97 ns** |  **2.33 ns** |  **1.00** |    **0.01** | **0.0691** |      **-** |     **872 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 2        |   574.7 ns |  11.88 ns |  3.09 ns |  2.01 |    0.02 | 0.0687 |      - |     872 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **32**       |   **928.6 ns** |  **25.24 ns** |  **6.55 ns** |  **1.00** |    **0.01** | **0.2251** | **0.0029** |    **2832 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 32       | 1,508.3 ns |  43.51 ns | 11.30 ns |  1.62 |    0.02 | 0.2251 | 0.0019 |    2832 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjNoMigration**                | **Deserialize,NoMigration**       | **256**      | **5,256.2 ns** | **352.78 ns** | **54.59 ns** |  **1.00** |    **0.01** | **1.3733** | **0.1297** |   **17240 B** |        **1.00** |
| JsonMigratableNoMigration          | Deserialize,NoMigration       | 256      | 7,492.2 ns | 100.21 ns | 15.51 ns |  1.43 |    0.01 | 1.3733 | 0.1297 |   17240 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **2**        |   **928.0 ns** |  **25.60 ns** |  **6.65 ns** |  **2.76** |    **0.02** | **0.0916** |      **-** |    **1160 B** |        **1.12** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 2        |   336.8 ns |   6.51 ns |  1.69 ns |  1.00 |    0.01 | 0.0820 |      - |    1032 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **32**       | **2,116.5 ns** | **102.80 ns** | **26.70 ns** |  **2.14** |    **0.03** | **0.2480** | **0.0038** |    **3120 B** |        **1.04** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 32       |   988.5 ns |  10.06 ns |  2.61 ns |  1.00 |    0.00 | 0.2384 | 0.0019 |    2992 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**      | **Deserialize,StaticMigration**   | **256**      | **9,875.4 ns** | **159.18 ns** | **24.63 ns** |  **1.90** |    **0.02** | **1.3885** | **0.1373** |   **17528 B** |        **1.01** |
| PlainStjStaticMigrationManual      | Deserialize,StaticMigration   | 256      | 5,196.2 ns | 259.06 ns | 67.28 ns |  1.00 |    0.02 | 1.3809 | 0.1373 |   17400 B |        1.00 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **2**        |   **145.1 ns** |   **7.83 ns** |  **1.21 ns** |  **1.00** |    **0.01** | **0.0317** |      **-** |     **400 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 2        |   185.0 ns |  10.21 ns |  2.65 ns |  1.28 |    0.02 | 0.0381 |      - |     480 B |        1.20 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **32**       |   **495.5 ns** |  **30.11 ns** |  **7.82 ns** |  **1.00** |    **0.02** | **0.0553** |      **-** |     **696 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 32       |   546.7 ns |  19.98 ns |  5.19 ns |  1.10 |    0.02 | 0.0610 |      - |     776 B |        1.11 |
|                                    |                               |          |            |           |          |       |         |        |        |           |             |
| **PlainStjSerializeNoMigration**       | **Serialize,NoMigration**         | **256**      | **2,998.3 ns** |  **75.09 ns** | **11.62 ns** |  **1.00** |    **0.00** | **0.2327** |      **-** |    **2936 B** |        **1.00** |
| JsonMigratableSerializeNoMigration | Serialize,NoMigration         | 256      | 3,045.5 ns | 108.27 ns | 28.12 ns |  1.02 |    0.01 | 0.2403 |      - |    3016 B |        1.03 |
