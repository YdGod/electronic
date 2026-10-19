# 贡献与安装皮肤

每个皮肤是一个独立文件夹：JSON 清单、透明 PNG 和素材授权。名称、菜单描述与作者信息都从清单读取，添加皮肤无需修改 C#。

```text
skins/my-pet/
  skin.json
  idle.png
  popup.png       可选
  LICENSE.txt     授权正文或作者明确授权
```

## 清单格式 v1

```json
{
  "SchemaVersion": 1,
  "Id": "my-pet",
  "Name": "我的小人",
  "Description": "点击时向你打招呼",
  "Author": "你的名字或公开署名",
  "License": "CC-BY-4.0",
  "Frames": {
    "Idle": "idle.png",
    "Popup": "popup.png"
  }
}
```

`Id` 使用小写字母开头、随后小写字母/数字/连字符，最多 64 个字符，与文件夹名一致。Name、Author、License 为必填，Description 可省略。Schema 见 `schemas/skin.schema.json`，模板在 `examples/skins/my-pet/`。

Idle 是平时显示的帧；Popup 是电量与温度弹窗可见时的帧，省略则始终使用 Idle。两帧不是充电或温度状态，也不会触发硬件查询。切换沿用约 200 毫秒淡入过渡，并尊重 Windows 动画设置。

## 素材要求

- PNG 透明背景，建议 610×610 正方形画布；最大每边 4096 像素，单文件最大 8 MB，清单最大 64 KB。
- 两帧使用相同画布大小，角色位置、视觉大小和脚底尽量对齐，四周留出边缘，避免过渡时跳动或裁切。
- 文件名使用英文字母、数字、点、下划线和连字符，以 `.png` 结尾，只引用包内直接文件；不支持 URL、绝对路径、子目录或 `../`。
- 保留图片中的白色衣服和饰品，勿把白色整体当作背景删除。

## 安装到现有挂件

把整个 `my-pet` 文件夹放入以下任意位置，退出并重新启动挂件，再打开“切换皮肤”：

```text
BatteryPet.exe 所在目录/skins/my-pet/
%LOCALAPPDATA%/BatteryPet/skins/my-pet/
```

不需要重新编译或管理员权限。内嵌皮肤优先，其次 EXE 旁，再次用户目录；相同 ID 不会覆盖已加载皮肤。修改内嵌皮肤应通过源码重新构建，或使用新 ID 安装。坏包会被跳过，原因写入 `%LOCALAPPDATA%\BatteryPet\app.log`；不能识别的已保存皮肤回退到 `whale`。

## 贡献到源码仓库

把包放入仓库的 `skins/<id>/`，执行 `build.ps1 -Test`。构建会自动发现清单并把它和引用的 PNG 内嵌进 EXE；运行便携包不需要额外复制内嵌图片。构建验证清单和 PNG 头部，自检还会实际解码内嵌皮肤。提交前检查日常/弹窗两种状态，附截图和素材授权。

原始素材、生成记录和大体积编辑工程留在本地，不与运行素材一起提交。不要复制现有皮肤的 `LicenseRef-Permission-Pending` 作为新贡献的授权；这表示没有可确认的公开再分发许可，不是开源许可证。原创可选择 CC0、CC BY 4.0 等合适的图片许可证；有角色或第三方素材时，需要确认相应权利。代码 MIT 不会自动覆盖图片。
