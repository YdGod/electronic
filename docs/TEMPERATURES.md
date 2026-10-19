# 贡献 CPU / GPU 温度后端

当前后端为 CPU 的 LibreHardwareMonitor 0.9.6 / PawnIO 服务，以及显卡的 NVIDIA NVML。AMD、Intel 显卡及其他监控接口可通过独立提供器贡献，不需要改写温度卡片。

## 接口与注册

从 `examples/temperatures/TemperatureProvider.cs.example` 开始，在 `src/Temperatures/Providers/` 添加实现：

```csharp
internal interface ITemperatureProvider
{
    string Id { get; }
    string Kind { get; } // "CPU" 或 "GPU"
    IEnumerable<TemperatureReading> Read(CancellationToken cancellation);
}
```

在 `src/Temperatures/ThermalProvider.cs` 的 `TemperatureProviderRegistry.All` 登记实例。构造器和静态初始化只保存配置，不能在启动时采样。只在 `Read` 中初始化接口、采样并释放资源，为阻塞 I/O 设置有限超时并检查取消。

同一提供器可返回多张显卡的结果，每张单独填写 Name 与 Sensor。没有适用硬件可返回空集合，统一层补未知占位；个别已识别 GPU 读取失败时仍返回该 GPU 的未知项。一个后端失败不会阻断另一后端，统一层会拒绝非法温度、过期时间和类型不匹配的结果。

| 字段 | 内容 |
| --- | --- |
| `Kind` | 与提供器一致的 CPU 或 GPU |
| `Name` | 实际 CPU / GPU 名称 |
| `Sensor` | 明确的传感器，如 CPU Package、GPU Core、GPU Hotspot |
| `Celsius` | 摄氏度实测值；不可读为 `null`，0°C 不等于未知 |
| `ReadAt` | 本次真实采样时间，建议 `DateTime.UtcNow` |
| `Source` | 驱动 / 库 / 接口与必要版本信息 |
| `Note` | 传感器含义、限制和失败原因 |

统一层只接受 0～150°C 的有限数值，时间允许过去 15 秒和未来最多 5 秒；无时间或超期不会显示为实时温度。同一块 GPU 不应由多个后端重复登记为两个设备，接入替代后端时需明确选择顺序或互斥条件。

## CPU 服务边界

需要驱动或管理员权限的硬件访问放在独立服务中，挂件只用普通权限读取。现有 `services/thermal/ThermalService.cs` 只在命名管道收到固定 READ 请求后打开 CPU 硬件组、采样并关闭；空闲时不更新传感器。共享协议与模型在 `src/Core/TemperatureModels.cs`，响应有长度上限和采样时间校验。

现有 CPU 规则优先 CPU Package / Tdie，其次核心最高温度。主板温度、ACPI 热区、Distance to TjMax 不能作为 CPU 温度。不要用磁盘或外设硬件组采集 CPU 温度，也不要为增加支持而静默安装驱动或提高整个挂件的权限。

## 验证

纯规则和后端容错测试使用假提供器，加入 `tests/ExtensionVerification.cs`。覆盖空结果、DLL / 服务不可用、无效数值、过期时间、类型不匹配、多个 GPU、取消和读取中途失败。全量自检无需驱动。

实机验证记录 CPU/GPU、驱动和系统版本、传感器名称、协议来源及与可信工具的对照。运行一次 `--temperatures` 可生成诊断；该命令会执行真实采样。涉及 CPU 服务的修改还需按[构建说明](BUILDING.md)编译服务，明确列出需要人工验证的安装和权限场景。
