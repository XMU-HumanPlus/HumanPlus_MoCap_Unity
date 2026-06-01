# HumanPlus MoCap Unity

本项目用于接收外部动捕数据并驱动 SMPL 标准模型，可选地将追踪数据转发到 VRChat OSC 或 SteamVR（兼容 SlimeVR Driver）。适合快速预览动捕、做角色重定向与 VR 端对接。

## 环境要求

- Unity **2022.3.62f3**（见 `ProjectSettings/ProjectVersion.txt`）
- Windows（SteamVR/SlimeVR 使用命名管道 `\\.\pipe\SlimeVRDriver`）
- 网络：本地或局域网 TCP 连接

## 快速开始

### 从示例工程开始

1. 使用 Unity Hub 打开本工程。
2. 打开场景：
   - `Assets/Scenes/SimpleMoCap.unity`（基础动捕预览）
   - `Assets/Scenes/MoCapToVR.unity`（动捕 + VR 输出）
3. 选中场景中的 **HumanPlusMoCap** 物体，在 **MoCapManager** 里设置：
   - `Server Ip`（默认 `127.0.0.1`）
   - `Port`（默认 `8888`）
   - `Connect On Load`（需要自动连接时勾选）
4. 启动你的动捕数据发送端（TCP）。
5. 点击 **Play**。如需校准，点击 UI 的校准按钮并保持 T-Pose。

### 从插件开始

在 **Release** 页面下载 UnityPackage，并导入您的工程

## 场景说明

- **SimpleMoCap**：仅做动捕效果预览，适合验证数据流与骨骼映射。
- **MoCapToVR**：在动捕基础上，提供：
  - **VRChat OSC 输出**（`VRCOSCHandler`）
  - **SteamVR/SlimeVR 追踪输出**（`SteamVrServer` + `TrackerManager`）

## VRChat OSC

在 `VRCOSCHandler` 中配置：

- **OSC Output**
  - `Remote Ip`（默认 `127.0.0.1`）
  - `Remote Port`（默认 `9000`）
- **OSC Input**
  - `Receive Port`（默认 `9001`）

MoCapToVR 场景内包含 **YawAlign(OSC)** 按钮，用于发送 VRChat Yaw 校准消息。

在您的VRChat中开启OSC功能

## SteamVR / SlimeVR

`SteamVrServer` 默认通过命名管道 `\\.\pipe\SlimeVRDriver` 与驱动通信：

- 首先在 https://github.com/SlimeVR/SlimeVR-OpenVR-Driver 下载驱动，并放入 SteamVR 驱动目录中（C：\Program Files （x86）\Steam\steamapps\common\SteamVR\drivers）
- 启动 SteamVR ，使用串流的方式连接您的头显
- 启动Unity端，程序将自动注册追踪点（腰、胸、双脚、双膝、双肘）
- 启动您得到的服务端软件并连接，这时您应该看到 SteamVR 场景中各追踪点的位置

## 在Unity场景中使用自定义模型

- 如需重新映射，可在 `TrackerManager` 上使用 **Auto Map Default Trackers**。

## 自定义角色重定向

1. 将你的角色模型导入并设置为 **Humanoid**。
2. 在角色上添加 **MoCapSrc** 组件。
3. 将场景中的 **HumanPlusMoCap** 拖入角色 **MoCapSrc** 组件的 **src** 中。
4. 按需调整 `globalWeight / upperBodyWeight / lowerBodyWeight`。

`MoCapManager` 默认使用 **TCP** 接收数据，协议要点：

- 以 `$` 分隔帧
- 有效帧格式：`pose#pos`
  - `pose`：`关节数 × 3` 的浮点数列表（轴角向量，弧度）
  - `pos`：`x,y,z`（根节点位置）
- 命令帧：`CMD:CALIBRATION_DONE`，由服务端发出，表示校准成功
- 发送校准指令：Unity 端会发送 `RECALIBRATE\n`，由服务端接收并重新校准

默认关节数为 **24**（SMPL/SMPL-X 风格）。
