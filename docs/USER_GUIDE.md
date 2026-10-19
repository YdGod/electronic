# 小电量管家 BatteryPet

一个 Windows 桌面挂件：使用用户提供的透明动漫小人图片，拖动到任意屏幕位置，点击查看外设电量、CPU 温度和显卡温度。无需安装 Node、Python 或开发 SDK。

## 启动与操作

双击 `BatteryPet.exe`（源码项目中位于 `dist` 文件夹）。适用于 Windows 10 / 11 x64，使用系统内置 .NET Framework 4.8。运行不需要管理员权限。

- 按住小人拖动；松开后保存位置，支持多显示器，显示器断开后自动回到可见区域。
- 单击小人展开 / 收起电量与温度对话框；点击其他窗口自动收起。
- 点击小人右上角的“−”，或对话框底部“刷新设备”旁的“隐藏小人”，小人和对话框一起消失；双击任务栏右下角托盘中的“小电量管家”图标恢复小人。右键菜单也提供隐藏操作。隐藏会保留程序和托盘图标。
- 电池中显示百分比；白色表示普通电量，绿色仅在设备明确报告正在充电时显示。
- 未知电量显示 `—` 和“暂未读取电量”；未知充电状态显示“充电状态未知”。不会生成模拟电量，也不会依据电量上涨猜测正在充电。
- 每次展开对话框即时查询一次电量和温度；需要再次查询时点击“刷新设备”。没有周期刷新，启动、收起、隐藏以及仅从托盘恢复小人时均不发起查询。悬停卡片可查看完整名称、传感器、数据来源和读取时间，也可按鼠标 / 键盘 / 耳机筛选。
- 右键小人设置大小、置顶、USB 候选项、开机启动、恢复主屏位置、导出诊断或退出。
- 右键菜单最上方的“小人皮肤”可随时切换“大肥鱼”和“菲比啾比”，选择会保存，重启后保持。大肥鱼平时显示日常表情，电量与温度弹窗显示期间切换到敬礼图，收起后恢复；菲比啾比也使用日常 / 敬礼两种状态。素材使用透明背景和相同尺寸画布，切换有约 200 毫秒的淡入过渡；关闭 Windows 界面动画时直接切换。
- 弹窗标题“电量与温度”和关闭按钮之间也有“切换皮肤”按钮，可直接选择皮肤；与右键菜单同步，切换时保持弹窗打开。
- 默认不开启开机启动；只有勾选菜单后才写入当前用户的启动项。
- 双击系统托盘的小电池图标可以找回小人。
- 再次运行 EXE 会找回已运行的小人，不会启动重复实例或额外查询；`--show-devices` 参数才会展开并查询。

## CPU 与显卡温度

弹窗上方显示 CPU 和显卡两张温度卡片，单位为 °C。本机已实测 i7-12700 的 **CPU Package（封装温度）** 与 RTX 5070 的 **GPU Core（显卡核心温度）**。温度随负载变化；不使用示例数值作为实时数据。

显卡通过 NVIDIA 驱动自带 NVML 接口直接读取，无需额外安装监控软件。本机已验证 RTX 5070；AMD / Intel 显卡暂未接入，读取失败时显示“温度未知”。多个 NVIDIA GPU 分别显示。

CPU 使用 LibreHardwareMonitor 0.9.6 和官方签名的 PawnIO 2.1.0 驱动，需要一次管理员启用：**完整解压便携包后，双击 `Enable-Cpu-Temperature.cmd`**。从旧版升级也需运行一次该脚本，以替换已安装的周期采集服务。脚本核验安装器签名和 SHA256，安装常规 PawnIO 驱动，将专用采集器放到 `%ProgramFiles%\BatteryPet\Thermal`，注册自动启动服务 `BatteryPetThermal`，并依赖 PawnIO 驱动。

服务空闲时阻塞等待本机请求，不打开 CPU 传感器。每次请求才启用 CPU 硬件组、采样一次并立即关闭，将最新快照写入 `%ProgramData%\BatteryPet\temperatures.json` 供诊断；小人直接获取本次请求的结果，不用旧快照替代实时查询。程序目录和快照限制普通用户为只读；小人日常运行无需管理员权限。

优先显示封装温度；只有核心温度可用时显示核心最高温度，并明确标注。不会把主板、ACPI 热区或距离 TjMax 的数值作为 CPU 温度。没有有效传感器数据、服务不可用或响应采样时间无效时显示未知。弹窗内保留本次查询快照，悬停可查看采样时间。

服务开机自动启动，退出小人后继续等待，不进行后台采样。如需停用，双击 `Disable-Cpu-Temperature.cmd`，停止服务并改为手动启动；保留可能被其他监控工具使用的 PawnIO 驱动。再次启用可恢复。此操作不改动 EPOS 耳机服务。收起弹窗后会取消尚未开始的查询，已发给设备的短请求会完成并释放接口，不继续发起查询。

温度组件、许可证与可重编译的采集器源码随 `thermal` 文件夹分发；第三方组件及源码地址见 `thermal/licenses/THIRD_PARTY.md`。其他 CPU 型号尚未实机验证。

## 电量支持与实际限制

**无法保证读取所有无线设备。** 电量能否显示由设备和驱动提供的接口决定，特别是 2.4 GHz USB 接收器的私有协议。当前版本接入了真实数据源：

| 数据来源 | 电量 | 充电状态 | 说明 |
| --- | --- | --- | --- |
| Windows PnP 电池属性 | 驱动公开时可读取 | 不推测 | 属性可能是系统缓存，更新时间取决于驱动 |
| 蓝牙 BLE Battery Service (`180F / 2A19`) | 服务可访问时可读取 | 可选 `2A1A` Battery Power State | 未提供充电状态时保持未知 |
| 标准 USB HID Feature Report | Battery Strength / Relative State of Charge | 可选 Charging usage | 读取标准电池特征 |
| 赛洛斯 V12 专用 HID 协议 | 已实机读取 | 已实机读取充电及未充电状态 | 根据官方驱动，仅查询连接、电量和充电状态 |
| 迈从 K5 Ultra 专用 HID 协议 | 已实机读取 | 已实测未充电，已接入官方充电字段 | 接收器模式即时读取，无需官方驱动常驻 |
| GSP 370 直接串口查询 | 已实机读取 | 暂未提供 | 使用 Windows USB 串口驱动，EPOS 后台服务停止时可独立查询 |
| GSP 370 / EPOS 兼容读取 | 已读取官方软件显示值 | 暂未提供 | 端口被占用时尝试读取官方显示值，更新时间由官方软件决定 |
| 其他私有接收器 / 耳机协议 | 需逐型号适配 | 需逐型号适配 | 未支持的设备显示名称和未知电量 |

USB 接口**不告诉 Windows 外设究竟是有线还是无线**，因此默认显示已插入的 USB 鼠标、键盘和音频外设作为候选项，并标记“USB · 无线待确认”。可在右键菜单关闭。虚拟输入设备、内置键盘、PCI 声卡不作为 USB 候选项。一个接收器的多个 HID / 播放 / 录音接口合并为一个物理设备；未公开子设备身份的多设备接收器只能显示接收器名称。

蓝牙设备的“存在”可能仅表示已配对；如果 Windows 只提供缓存电量，说明中会标记系统缓存，不能据此确认正在连接。能够从蓝牙设备枚举确认未连接时会清除电量。蓝牙耳机不一定实现标准 Battery Service。

本机已适配如下设备（既有设备于 2026-10-13 实测，迈从 K5 Ultra 于 2026-10-19 新增；百分比会随使用变化）：

| 设备 | 实测结果 | 使用条件 |
| --- | --- | --- |
| 赛洛斯 V12 | **无线实测 15%，未充电；有线充电实测 60%，正在充电** | 接收器 V12M RECEIVER 和插线后的 V12M 均支持，可直接读取，无需官方驱动常驻 |
| 迈从 K5 Ultra | **70%，在线，未充电** | 已验证 3837:1018 接收器，通过官方只读设备信息指令直接获取；有线充电模式尚未实机验证 |
| GSP 370 | **87%，在线，充电状态未知** | 已验证直接查询接收器，不需要 EPOS 软件运行；电池保持白色 |
| SKN 青龙 4.0 Ultra 绯 | **型号、连接状态已识别，百分比未知** | 官方网页驱动不显示百分比，用户确认用 FN+B 灯光查看电量；当前无法从 2.4 GHz 接口读取百分比 |

GSP 370 现在优先直接查询接收器，获得电量、连接状态及本次读取时间。此方式不调用 EPOS 程序库，也不需要 EPOS 界面或服务运行。本机接收器使用 Windows 自带的 `usbser` 驱动。

EPOS 后台服务会独占耳机串口，不能与挂件同时直接查询。仅关闭 EPOS 界面不一定释放端口。便携包提供两个可选操作：

- **启用独立模式**：先从系统托盘退出 EPOS Gaming Suite，再双击 `Enable-Gsp370-Direct.cmd`。管理员确认后，仅停止 `EPOSGamingSuiteService` 并改为手动启动，保存原服务设置，随后以普通权限打开挂件。EPOS 后台设备管理将停止。
- **恢复 EPOS**：双击 `Restore-Epos.cmd`，管理员确认后恢复保存的启动类型和运行状态，再按需打开官方软件。

原服务设置保存在启用脚本所在文件夹的 `epos-service-settings.json`；切换后移动便携文件夹时，请一并保留此文件，以便恢复。

挂件日常读取无需管理员权限，不会自行停止或改动 EPOS 服务。若以后重新运行 EPOS 导致端口被占用，会尝试读取官方界面的显示值；无法读取时显示未知。耳机充电状态仍未找到明确接口，不根据电量变化猜测。

`devices/profiles/` 中每设备一个 JSON 配置，用产品字符串和 VID/PID 双重匹配校正名称与分类；旧版根目录 `profiles.json` 可继续作为本地优先配置。一个设备的复合接口会合并显示。名称配置不产生电量数据。详细适配与测试结果见 `设备支持说明.md`（源码中为 `DEVICE_SUPPORT.md`）。

## 数据与隐私

设备读取和设置完全在本机进行，不使用网络、不上传设备信息。只发送已确认的电量 / 连接状态查询，不修改外设设置或固件。设置与循环日志保存在 `%LOCALAPPDATA%\BatteryPet`，启用后的 CPU 温度缓存位于 `%ProgramData%\BatteryPet`。导出的诊断包含设备名称、容器 ID、电量、温度、传感器来源及读取时间，只有手动选择导出时才保存。

皮肤素材：`skins/whale/idle.png` 与 `skins/whale/popup.png` 为大肥鱼的日常 / 敬礼帧；`skins/phoebe/idle.png` 与 `skins/phoebe/popup.png` 为菲比啾比的日常 / 敬礼帧。素材来自用户提供的图片，内嵌在 EXE 中，便携运行无需单独复制图片。

也可安装社区皮肤：把包含 `skin.json` 和透明 PNG 的完整文件夹放到 EXE 旁的 `skins/<id>/` 或 `%LOCALAPPDATA%\BatteryPet\skins/<id>/`，重启后在皮肤菜单选择。文件夹名要与清单 Id 一致；同名内嵌皮肤优先。素材独立授权，见随包的 `NOTICE.md` 和 `licenses/skins/`。

## 从源码构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

构建脚本调用 Windows 自带 .NET Framework C# 编译器，自动内嵌 `skins/` 中的皮肤。普通源码构建不依赖第三方组件；完整 CPU 采集器需要准备 LibreHardwareMonitor 0.9.6 Framework 库和固定版本的 PawnIO 安装器到 `vendor/`，然后运行 `build.ps1 -WithThermal`，详见源码中的 `docs/BUILDING.md`。便携包使用 `tools/package-release.ps1` 生成。请完整复制便携文件夹，保留 `devices/profiles/`、CPU 启用脚本和 `thermal` 组件。

命令行诊断 / 界面渲染（命令会完成后退出，不启动桌面挂件）：

```powershell
Start-Process .\dist\BatteryPet.exe -ArgumentList '--diagnose', 'devices.json' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--self-test', 'self-test.json' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--render-preview', 'preview.png' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--render-live', 'live-preview.png' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--gsp370-direct', 'gsp370.json' -WindowStyle Hidden -Wait
Start-Process .\dist\BatteryPet.exe -ArgumentList '--temperatures', 'temperatures.json' -WindowStyle Hidden -Wait
```

`--gsp370-direct` 是独立查询接口：只读取 GSP 370，将 `Percent`、`Connected`、`Charging`、`ReadAt`、`Source` 与说明写入 JSON，完成后立即退出；不使用 EPOS 界面作为备用数据源。端口被占用或返回无效数据时，写入未知值并返回非零退出码；确认离线时不显示旧电量。

`--temperatures` 请求一次真实 CPU 采样并读取 NVIDIA 温度，输出独立 JSON。`--render-preview` 使用明确标注的示例数据，只用于界面检查；`--render-live` 和正式挂件展开弹窗时读取真实数据。通用读取器位于 `src/Devices/WindowsBatteryProvider.cs`，赛洛斯与 SKN 适配位于 `src/Devices/Adapters/`，迈从适配位于 `src/Devices/Adapters/MchoseK5Battery.cs`，耳机读取位于 `src/Devices/Adapters/EposGsp370Battery.cs`，温度读取位于 `src/Temperatures/`，CPU 服务位于 `services/thermal/ThermalService.cs`。运行便携包无需复制 `research` 文件夹或任何 EPOS 程序文件。
