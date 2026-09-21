# Source-Generated Benchmarks

> Auto-generated from BenchmarkDotNet output by `scripts/update-perf-docs.ps1`.
> Do not edit manually. Re-run benchmarks and this script to update.
> Public reports omit the internal `PolymorphicPlainStj*` guardrail benchmarks.
```

BenchmarkDotNet v0.15.6, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 11.0.0 (11.0.0-rc.1.26425.128, 11.0.26.42628), X64 RyuJIT x86-64-v3

Toolchain=InProcessNoEmitToolchain  IterationCount=5  LaunchCount=1  
WarmupCount=1  

```
| Method                                       | Categories                                 | PayloadSize | Mean         | Error        | StdDev       | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------------- |------------------------------------------- |------------ |-------------:|-------------:|-------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Small**       |    **490.48 ns** |   **106.169 ns** |    **16.430 ns** |  **1.50** |    **0.17** | **0.0186** |      **-** |     **312 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Small       |    329.82 ns |   157.450 ns |    40.889 ns |  1.01 |    0.16 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Medium**      |  **2,063.24 ns** | **1,376.117 ns** |   **357.373 ns** |  **1.23** |    **0.21** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Medium      |  1,688.97 ns |   760.830 ns |   117.739 ns |  1.00 |    0.09 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableExternalMigration**              | **Deserialize,ExternalMigration**              | **Large**       | **14,137.09 ns** | **3,264.960 ns** |   **847.900 ns** |  **1.06** |    **0.08** | **1.4648** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjExternalMigrationManual              | Deserialize,ExternalMigration              | Large       | 13,380.76 ns | 4,954.401 ns |   766.699 ns |  1.00 |    0.07 | 1.4648 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Small**       |    **400.39 ns** |    **97.088 ns** |    **25.213 ns** |  **1.38** |    **0.08** | **0.0114** |      **-** |     **192 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Small       |    290.99 ns |     9.344 ns |     1.446 ns |  1.00 |    0.01 | 0.0114 |      - |     192 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Medium**      |  **1,794.47 ns** |   **423.744 ns** |   **110.045 ns** |  **1.14** |    **0.06** | **0.0992** |      **-** |    **1688 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Medium      |  1,572.89 ns |    68.435 ns |    17.772 ns |  1.00 |    0.01 | 0.0992 |      - |    1688 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableLegacyPayload**                  | **Deserialize,LegacyPayload**                  | **Large**       | **12,685.72 ns** | **1,099.416 ns** |   **170.136 ns** |  **1.02** |    **0.02** | **1.4648** | **0.1526** |   **24656 B** |        **1.00** |
| PlainStjLegacyPayloadManual                  | Deserialize,LegacyPayload                  | Large       | 12,407.33 ns |   961.437 ns |   148.783 ns |  1.00 |    0.02 | 1.4648 | 0.1526 |   24656 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Small**       |    **233.89 ns** |     **4.187 ns** |     **0.648 ns** |  **1.00** |    **0.00** | **0.0095** |      **-** |     **160 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Small       |    380.09 ns |   219.039 ns |    56.884 ns |  1.63 |    0.22 | 0.0095 |      - |     160 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Medium**      |  **1,578.58 ns** |   **144.942 ns** |    **37.641 ns** |  **1.00** |    **0.03** | **0.0973** |      **-** |    **1656 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Medium      |  1,834.29 ns |   712.905 ns |   185.139 ns |  1.16 |    0.11 | 0.0973 |      - |    1656 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjNoMigration**                          | **Deserialize,NoMigration**                    | **Large**       | **12,367.64 ns** |   **877.045 ns** |   **227.766 ns** |  **1.00** |    **0.02** | **1.4648** | **0.1526** |   **24624 B** |        **1.00** |
| JsonMigratableNoMigration                    | Deserialize,NoMigration                    | Large       | 13,136.24 ns | 2,332.589 ns |   360.971 ns |  1.06 |    0.03 | 1.4648 | 0.1526 |   24624 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Small**       |    **491.07 ns** |    **31.571 ns** |     **4.886 ns** |  **1.57** |    **0.10** | **0.0181** |      **-** |     **312 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Small       |    313.21 ns |    84.765 ns |    22.013 ns |  1.00 |    0.09 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Medium**      |  **2,264.20 ns** | **1,062.098 ns** |   **275.823 ns** |  **1.32** |    **0.17** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Medium      |  1,718.06 ns |   462.573 ns |   120.129 ns |  1.00 |    0.09 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableStaticMigration**                | **Deserialize,StaticMigration**                | **Large**       | **15,620.61 ns** | **9,123.404 ns** | **2,369.319 ns** |  **1.14** |    **0.16** | **1.4801** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjStaticMigrationManual                | Deserialize,StaticMigration                | Large       | 13,700.87 ns | 2,306.157 ns |   598.902 ns |  1.00 |    0.06 | 1.4648 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Small**       |    **434.87 ns** |   **267.784 ns** |    **69.543 ns** |  **1.28** |    **0.23** | **0.0186** |      **-** |     **312 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Small       |    342.72 ns |   151.464 ns |    39.335 ns |  1.01 |    0.15 | 0.0186 |      - |     312 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Medium**      |  **1,753.54 ns** |   **151.264 ns** |    **39.283 ns** |  **1.11** |    **0.02** | **0.1068** |      **-** |    **1808 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Medium      |  1,584.87 ns |    59.368 ns |     9.187 ns |  1.00 |    0.01 | 0.1068 |      - |    1808 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **JsonMigratableUndiscriminatedSourceMigration** | **Deserialize,UndiscriminatedSourceMigration** | **Large**       | **14,061.32 ns** | **8,567.432 ns** | **2,224.935 ns** |  **1.00** |    **0.15** | **1.4801** | **0.1526** |   **24776 B** |        **1.00** |
| PlainStjUndiscriminatedSourceMigrationManual | Deserialize,UndiscriminatedSourceMigration | Large       | 14,065.53 ns | 2,246.090 ns |   583.302 ns |  1.00 |    0.05 | 1.4801 | 0.1526 |   24776 B |        1.00 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Small**       |    **718.78 ns** |   **400.432 ns** |   **103.991 ns** |  **1.02** |    **0.19** | **0.0372** |      **-** |     **632 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Small       |    522.86 ns |    50.213 ns |    13.040 ns |  0.74 |    0.09 | 0.0095 |      - |     160 B |        0.25 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Small       |    720.58 ns |   213.371 ns |    33.019 ns |  1.02 |    0.13 | 0.0181 |      - |     312 B |        0.49 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Medium**      |  **2,709.53 ns** |   **803.659 ns** |   **124.367 ns** |  **1.00** |    **0.06** | **0.0954** |      **-** |    **1656 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Medium      |  2,397.28 ns |   486.837 ns |   126.430 ns |  0.89 |    0.06 | 0.0954 |      - |    1656 B |        1.00 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Medium      |  2,478.37 ns |   215.551 ns |    55.978 ns |  0.92 |    0.04 | 0.1068 |      - |    1808 B |        1.09 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjUnionDispatchStructural**              | **Deserialize,UnionDispatch**                  | **Large**       | **22,888.62 ns** | **7,604.107 ns** | **1,974.762 ns** |  **1.01** |    **0.11** | **1.5259** | **0.1526** |   **25544 B** |        **1.00** |
| JsonMigratableUnionDispatch                  | Deserialize,UnionDispatch                  | Large       | 18,482.30 ns | 5,700.795 ns |   882.204 ns |  0.81 |    0.07 | 1.4648 | 0.1526 |   24624 B |        0.96 |
| JsonMigratableUnionDispatchWithMigration     | Deserialize,UnionDispatch                  | Large       | 17,061.51 ns |   377.616 ns |    98.066 ns |  0.75 |    0.06 | 1.4648 | 0.1526 |   24776 B |        0.97 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Small**       |     **93.19 ns** |    **45.967 ns** |    **11.937 ns** |  **1.01** |    **0.16** | **0.0033** |      **-** |      **56 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Small       |    212.18 ns |    19.336 ns |     5.021 ns |  2.31 |    0.26 | 0.0081 |      - |     136 B |        2.43 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Medium**      |    **541.32 ns** |   **307.143 ns** |    **79.764 ns** |  **1.02** |    **0.19** | **0.0248** |      **-** |     **416 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Medium      |    947.83 ns |   328.432 ns |    50.825 ns |  1.78 |    0.25 | 0.0477 |      - |     800 B |        1.92 |
|                                              |                                            |             |              |              |              |       |         |        |        |           |             |
| **PlainStjSerialize**                            | **Serialize**                                  | **Large**       |  **4,168.54 ns** | **1,269.086 ns** |   **196.392 ns** |  **1.00** |    **0.06** | **0.6180** | **0.0229** |   **10384 B** |        **1.00** |
| JsonMigratableSerialize                      | Serialize                                  | Large       |  6,537.31 ns | 1,512.097 ns |   392.687 ns |  1.57 |    0.11 | 0.6409 | 0.0153 |   10776 B |        1.04 |
