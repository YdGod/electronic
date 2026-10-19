# 贡献设备与电量读取

先分清贡献的是“显示正确名字”，还是“取得真实电量”。`devices/profiles/` 只解决身份显示，实际电量来自标准 Windows / BLE / HID 接口或 `src/Devices/Adapters/` 中的协议实现。

## 1. 收集最少必要信息

构建后，可运行以下只读诊断，程序输出 JSON 后退出：

```powershell
Start-Process .\dist\BatteryPet.exe -ArgumentList '--pnp-inventory', 'dist\pnp-inventory.json' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--hid-inventory', 'dist\hid-inventory.json' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--diagnose', 'dist\devices.json' -WindowStyle Hidden -Wait
```

记录型号、连接方式、VID/PID、产品字符串、UsagePage/Usage、Input/Output/Feature 长度、报告 ID、固件和官方驱动版本。上传前移除设备路径中的实例尾段、序列号、容器 ID、蓝牙地址和个人路径；只保留匹配需要的 VID/PID/接口号。

优先检查驱动是否已经提供 PnP 电池属性、标准 BLE Battery Service（180F / 2A19）或标准 HID 电池字段；这些通常无需新增厂商查询。私有接收器需要确认协议，不能仅凭产品名向所有接口试发报文。

## 2. 添加名称配置

新增 `devices/profiles/<brand-model-mode>.json`，参考：

```json
{
  "Product": "Example Receiver",
  "Vid": "1234",
  "Pid": "5678",
  "Name": "示例无线鼠标",
  "Kind": "鼠标",
  "Connection": "2.4 GHz 接收器"
}
```

VID/PID 是四位十六进制字符串，不加 `0x`。Product 与硬件 ID 必须同时匹配；Product 按不区分大小写的子串匹配，PID 要有完整边界。一个有线 / 无线模式可各用一个文件。Connection 省略时使用“2.4 GHz 接收器”；分类通常为“鼠标”“键盘”“耳机”。配置格式见 `schemas/device-profile.schema.json`。从旧版保留的根目录 `profiles.json` 仍优先使用，不应把个人覆盖配置提交到仓库。

## 3. 实现读取适配器

从 `examples/devices/HidBatteryAdapter.cs.example` 开始，或参考已验证的 [迈从实现](../src/Devices/Adapters/MchoseK5Battery.cs)。将完成的文件放入 `src/Devices/Adapters/`，在 `DeviceProviderRegistry.Default` 的对应列表登记一个实例。

`IHidBatteryAdapter` 的约定：

| 成员 | 职责 |
| --- | --- |
| `Id` | 稳定且唯一的适配器标识 |
| `Supports(HidDescriptor)` | 只判断完整身份及集合能力，不访问硬件 |
| `Read(descriptor, cancellation)` | 只发送已确认的状态/电量查询，返回统一模型 |

串口或其他传输可实现 `IPhysicalBatteryAdapter`，参考 [GSP 370](../src/Devices/Adapters/EposGsp370Battery.cs)，通过 `DeviceScanContext` 使用本次枚举结果，不重复枚举所有硬件。

将请求构造、响应解析与 I/O 分开。解析必须验证报告长度、ID、命令、状态、序列/校验和及接收器子设备身份，校验依据以具体协议为准。接收器可能被多个型号共用，不能把其他鼠标的数据当作当前型号。

HID 传输可复用 `HidQueryChannel.Query(request, matches, timeoutMilliseconds)`。该通道限制等待时间、取消超时 I/O 并释放句柄。只绑定已确认的供应商集合，避免向标准键盘 / 鼠标输入集合发送私有命令。收到取消后不发起后续请求；已发出的短请求可在有限超时内完成并释放。

## 4. 返回真实状态

| 字段 | 约定 |
| --- | --- |
| `Kind` / `Connection` | 可由适配器明确返回分类与连接方式；未提供时保留 Windows 推断或名称配置 |
| `Percent` | 0～100；无效、超时或协议未知用 `null`，0 是有效的没电 |
| `Charging` | 明确字段转换为 true / false；没有接口用 `null` |
| `Connected` | true=明确在线，false=明确离线，null=无法确认 |
| `ReadAt` | 取得有效本次电量时记录时间；未知值不用伪造时间 |
| `Source` | 可追溯的数据来源，例“品牌官方 HID 协议” |
| `Note` | 连接要求、未知原因、是否系统缓存等说明 |

不支持该设备时返回 `null`；已确认支持但超时或无有效数据时返回带来源与原因的未知对象。明确离线时清除电量、充电状态和时间；休眠状态只在有协议依据时归为离线。官方 UI 值属于官方缓存，不应标为直接实时查询。

## 5. 测试与提交

在 `tests/fixtures/` 添加脱敏后的真实报文，注明来源日期、固件、请求、响应和预期值。参见 `mchose-k5-ultra.json`；自检从内嵌资源读取它，不访问真实设备。新解析测试请接入 `tests/Verification.cs` 或 `ExtensionVerification.Run`。

覆盖正常响应、0/100、未知哨兵、截断/损坏/错误命令、错误子设备、未知充电状态和断线。运行 `build.ps1 -Test`，再在实机验证有线、无线、充电、休眠和拔出接收器中适用的场景，将实际结果与未验证状态写入 `DEVICE_SUPPORT.md`。只识别型号或连接状态的适配也可以贡献，电量应明确保持未知。
