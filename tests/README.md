# 自检

执行根目录 `build.ps1 -Test`。测试与挂件一同编译，用 `BatteryPet.exe --self-test <report.json>` 执行并写 JSON 报告；它们不打开真实设备、不要求 CPU 服务或驱动。

- `Verification.cs`：现有协议、核心规则、布局及皮肤兼容回归。
- `ExtensionVerification.cs`：注册、错误隔离、取消、配置兼容、外部皮肤与采样时间等扩展边界。
- `fixtures/`：脱敏报文，自动内嵌为 `BatteryPet.Fixtures.<filename>`。新增报文后应添加对应解析断言，不能仅把文件放进去。

新增测试请接入 `Verification.Run`。实机测试与 UI 自动化工具在 `tools/`，其环境要求各异，不纳入通用 CI。贡献者需在 PR 中注明实机模式和未验证状态。
