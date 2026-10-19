# 构建、测试和打包

## 普通挂件

Windows 10 / 11 x64，启用 .NET Framework 4.8。构建脚本调用系统 `Framework64/v4.0.30319/csc.exe`，无需 Visual Studio、.NET SDK、Node、Python 或联网下载。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

产物在 `dist/`。可以通过 `-OutputDirectory .\outputs\my-build` 指定独立输出，验证时不覆盖正在运行的 EXE。`-Test` 构建后调用 `--self-test`；报告保存在输出目录的 `self-test.json`，失败时脚本抛出异常。

默认编译挂件和自检，复制设备配置、说明、授权及服务操作脚本；不会构建或安装 CPU 服务，不要求 `vendor/` 存在。没有已安装 CPU 服务时 CPU 温度保持未知；电量、皮肤和 NVIDIA 读取仍可用。脚本使用 Windows PowerShell 兼容的 UTF-8 BOM 编码，避免中文路径错误。

## 可选 CPU 服务

准备以下组件到本地忽略的 `vendor/`：

```text
vendor/
  LibreHardwareMonitor/
    LibreHardwareMonitorLib.dll
    System.Memory.dll
    System.Runtime.CompilerServices.Unsafe.dll
    System.Buffers.dll
    System.Numerics.Vectors.dll
  PawnIO/
    PawnIO_setup.exe
```

从 [LibreHardwareMonitor v0.9.6 官方发布](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6) 获取 .NET Framework 发布包，完整解压后复制这些文件；PawnIO 使用该官方发布配套的签名安装器，版本 2.1.0。此项目固定安装器 SHA256：

```text
A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0
```

不要用版本不匹配的文件绕过检查。组件来源、上游源码和许可证见 `third_party/THIRD_PARTY.md`，许可证原文已保存于 `third_party/licenses/`。依赖 DLL 和安装器不提交到 Git。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -WithThermal
```

产物包含 `dist/thermal/` 里的采集器、依赖、安装器、许可证和可重编译源码。也可单独构建服务：

```powershell
.\tools\build-thermal.ps1 -LibraryDirectory D:\dependencies\lhm -PawnInstaller D:\dependencies\PawnIO_setup.exe -OutputDirectory .\outputs\thermal
```

构建不启动服务、不安装驱动。实机启用通过完整便携包中的 `Enable-Cpu-Temperature.cmd`，需要一次管理员确认，详见使用说明。

## 验证范围

`build.ps1 -Test` 检查配置、协议解析、坏包、未知状态、温度规则、扩展容错、皮肤加载及离屏界面渲染，不访问真实外设或采集器。`tools/validate-contributions.ps1` 也可单独运行，用于校验仓库的设备配置和皮肤清单/PNG 头部。

GitHub Actions 在干净的 Windows runner 上运行默认构建、自检与示例界面渲染，并上传 `dist/` 作为检查产物。它不会安装硬件驱动或假定 runner 有 NVIDIA / 特定外设；不能替代实机协议与 CPU 服务安装验证。

仅渲染示例 UI：

```powershell
Start-Process .\dist\BatteryPet.exe -ArgumentList '--render-preview', 'dist\preview.png' -WindowStyle Hidden -Wait
```

其他诊断参数见 `USER_GUIDE.md`。`--render-live`、`--diagnose`、`--temperatures` 会查询真实硬件；仅在需要实机验证时运行。`tools/verify-skins.ps1` 可对独立输出目录验证真实窗口交互；部分其他 `tools/` 脚本针对既有本机设备，不作为通用 CI 使用。

## 完整便携包

完成带服务的构建并关闭使用该输出的挂件后执行：

```powershell
.\tools\package-release.ps1
```

脚本从 `dist/` 收集明确的运行文件、`devices/profiles/`、授权和 `thermal/`，生成根目录的“小电量管家-便携版.zip”，逐文件校验哈希。它不打包研究文件、诊断或个人覆盖配置，也不会修改系统服务。完整包要求所有热量组件到位，缺少时停止而不生成不完整包。

发布前另行确认皮肤再分发授权，参见 `NOTICE.md`。
