# HumanPlusMoCapUnity Packaging

这个目录参考 `E:\HumanPlus\HumanPlus_BNO085_BT_Demo\packaging` 的结构，提供 Inno Setup 安装包脚本。

## 目标输出

- Unity Windows 发布目录：`Build\Windows\HumanPlusMoCapUnity\`
- 安装包输出目录：`Build\Installer\`
- 安装包文件名：`HumanPlusMoCapUnity-Setup.exe`

## 先决条件

1. 在 Unity 中只保留 `Assets/Scenes/SimpleMoCap.unity` 到 `Scenes In Build`
2. `Player > Product Name` 设为 `HumanPlusMoCapUnity`
3. 用 Unity 打包 Windows x64，并把输出目录选到：
   `Build\Windows\HumanPlusMoCapUnity\`

打包完成后，目录中至少应包含：

- `Build\Windows\HumanPlusMoCapUnity\HumanPlusMoCapUnity.exe`
- `Build\Windows\HumanPlusMoCapUnity\HumanPlusMoCapUnity_Data\`

## 生成安装包

安装 Inno Setup 6 后，在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File packaging\build_inno_setup.ps1
```

如果 Inno Setup 安装在自定义目录，也可以显式传入：

```powershell
powershell -ExecutionPolicy Bypass -File packaging\build_inno_setup.ps1 -IsccPath "D:\Inno Setup 6\ISCC.exe"
```

## 风格对齐项

- 使用 `WizardStyle=modern`
- 默认中文安装界面
- 包含 EULA 页面
- 提供可选桌面快捷方式
- 安装目录默认到 `Program Files\HumanPlusMoCapUnity`
