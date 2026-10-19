# 温度组件与源码

本项目使用未修改的 LibreHardwareMonitor 0.9.6 .NET Framework 库，仅启用 CPU 硬件组。显卡通过本机 NVIDIA 驱动的 NVML 只读接口查询，便携包不分发 NVIDIA DLL。

- LibreHardwareMonitor：MPL 2.0，许可证见 LHM-LICENSE.txt，嵌入 PawnIO 模块为 LGPL 2.1，见 PawnIO-COPYING.txt 与 LHM-THIRD-PARTY-NOTICES.txt。完整对应源码：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 ，源码归档：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/archive/refs/tags/v0.9.6.zip 。
- PawnIO 2.1.0：保留来自上述官方发布标签的签名安装器，未修改。仅安装常规版本。驱动为 GPL 2 或后续版本并附 IOCTL 通信例外，见 PawnIO-README.md 与 PawnIO-LICENSE.txt。官方分发：https://pawnio.eu ，驱动源码：https://github.com/namazso/PawnIO 。嵌入模块为 0.1.6，源码：https://github.com/namazso/PawnIO.Modules/tree/0.1.6 。
- System.Memory、System.Buffers、System.Numerics.Vectors、System.Runtime.CompilerServices.Unsafe：来自官方 LibreHardwareMonitor 发布包，保留未修改的 Microsoft .NET 依赖。MIT 许可证如下，源码：https://github.com/dotnet/runtime 。

CPU 采集器源码位于 ../source；库为独立 DLL，可替换为自行构建且具有相同 API 的版本。编译 CPU 采集器：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:exe /platform:x64 /out:BatteryPet.ThermalService.exe /reference:LibreHardwareMonitorLib.dll /reference:System.ServiceProcess.dll /reference:System.Runtime.Serialization.dll source\ThermalService.cs source\TemperatureModels.cs
```

## MIT License

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
