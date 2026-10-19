# 架构与扩展边界

程序按“查询时机 → 枚举与适配 → 统一模型 → 界面”组织。设备协议、温度后端和皮肤各自有独立入口，避免贡献一个设备时改动主窗口。

## 查询链路

```text
App/PetWindow：展开或刷新
  ├─ WindowsBatteryProvider.Scan(token)
  │    ├─ Windows PnP / BLE / HID 枚举
  │    ├─ DeviceProviderRegistry：匹配厂商适配器
  │    ├─ 标准 HID fallback / PnP 缓存属性
  │    └─ 合并物理设备 → 应用名称配置 → ScanResult
  └─ ThermalProvider.Read(token)
       └─ TemperatureProviderRegistry → CPU / GPU 后端 → ThermalSnapshot

UI/BubbleView：只消费模型，显示未知、数据来源和采样时间
```

`DeviceBattery`、`TemperatureReading`、`ScanResult`、`ThermalSnapshot` 在 `src/Core/`。界面不解析字节，适配器不创建 WPF 控件。

## 设备模块

`Devices/WindowsBatteryProvider.cs` 负责扫描、蓝牙连接判定、复合接口合并和名称映射。`Platform/Windows/` 封装 SetupAPI、设备节点、HID 描述符及有超时的 HID 查询通道。`Devices/Standard/` 实现 BLE 180F/2A19 与标准 HID 电池字段。

`IHidBatteryAdapter` 接收单个 HID 描述符；`IPhysicalBatteryAdapter` 接收物理 PnP 节点和扫描上下文，适用于 GSP 370 之类串口或其他传输。两者都在 `DeviceProviderRegistry.cs` 注册，按顺序只调用匹配者。返回 `null` 允许继续尝试后续适配器；返回电量未知的对象表示已识别但本次无法取得有效值。标准 HID 在厂商适配器后尝试。

注册表隔离适配异常，校验百分比，并清除已确认断线设备的电量；Windows 层继续处理其他接口。名称配置只负责名称、分类和连接标签。旧版根目录 `profiles.json` 的有效条目优先，随后按文件名加载 `devices/profiles/*.json`；单个损坏文件不会阻止其他文件加载。

## 温度模块与服务

`ITemperatureProvider` 在统一模型中返回一组 CPU 或 GPU 传感器。`ThermalProvider` 隔离各提供器异常，拒绝过期数据、无效温度和错误类型；一个提供器失败不会阻止其他提供器。多张 GPU 保留各自状态；无硬件支持的通用占位在同类后端取得有效值时移除。

CPU 客户端通过本机命名管道请求服务。`services/thermal/` 单独编译，依赖 LibreHardwareMonitor / PawnIO；只启用 CPU 硬件组，收到 `READ` 才采样。共享的 `Core/TemperatureModels.cs` 同时编入挂件与服务，改动管道协议时需验证两端兼容性。服务安装脚本仍保留签名/哈希校验和目录权限限制。

## 皮肤模块

构建脚本遍历 `skins/*/skin.json` 并内嵌清单与引用的 PNG。`Skins/PetSkins.cs` 先加载内嵌皮肤，再读取 EXE 旁和用户目录中的皮肤包，菜单统一遍历该目录。默认皮肤 ID 为 `whale`，未知的已保存 ID 回退到默认值；内嵌皮肤优先，重复 ID 跳过。

皮肤只包含数据和 PNG，不执行脚本。加载时校验清单、文件名、图片大小和画布尺寸，先验证 PNG 头部再解码；坏包记入日志并跳过。日常/弹窗帧复用现有过渡与 Windows 动画设置。

## 构建和兼容

保持现有 `BatteryPet` 命名空间以兼容设置、反射验证和诊断工具；目录表达职责。扩展接口目前为程序集内部接口，面向源码贡献，不承诺第三方 DLL 的二进制兼容。

`build.ps1` 递归编译 `src/` 与 `tests/`；`examples/` 不参与编译；CPU 服务独立构建，避免默认构建依赖本地研究文件。设置路径、服务名、管道名、命令行诊断参数和按需刷新行为保留。
