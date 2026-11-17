# XBR Communication Studio

基于 [dathlin/HslCommunication](https://github.com/dathlin/HslCommunication) Demo 的 Avalonia 桌面界面重构项目。显示名：**XBR 通信工作台**。

保留上游 WinForms 示例和提交历史，新界面采用 **Avalonia 12.1.6 + TKCode.Arc 1.1.0 + CommunityToolkit.Mvvm + .NET 10**。

![Modbus TCP](docs/baseline/screenshots/arc-modbus.png)

## 当前功能

- Modbus TCP 连接、站号、字节序、超时、广播与分批参数。
- 类型化读写、原始报文、只读轮询、有限日志、单设备明文 XML 配置。
- FC23 读写寄存器，Word/Bool 站号搜索、匹配即停和取消。
- 18 项限定测试通过：异常/超时、数组、四种字节序、分包边界、线圈、FC23 和搜索恢复。

**迁移进行中，尚未实现原版全部页面和功能，也未完成真机验收。**

## 运行

Windows、.NET 10 SDK。请从自己的原版安装/发布包准备 **HslCommunication.dll 13.0.3** 和 Newtonsoft.Json.dll；不在本分支新增分发这些依赖或授权信息。

```powershell
.\tools\Set-HslReferences.ps1 -SourceDirectory '你的原版发布包目录'
dotnet run --project src/Xbr.Desktop
```

也可通过 `-p:HslReferenceDirectory=依赖目录` 指定位置。

```powershell
dotnet build probes/LegacyModbus
dotnet run --project probes/ModbusMatrix
dotnet run --project probes/DesktopSmoke
```

LegacyModbus 使用 Windows .NET Framework 4.8.1；比较旧 DLL 的 API 分支与现代服务，不是原版 EXE 按钮自动化。矩阵测试只访问本机模拟器。

## 目录

| 目录 | 用途 |
|---|---|
| src/Xbr.Desktop | Avalonia + Arc 界面及 MVVM |
| src/Xbr.Communication | 连接、排队读写、搜索与配置服务 |
| probes | 兼容性、Framework 对照、服务及渲染检查 |
| HslCommunicationDemo | 保留的官方 WinForms 示例 |
| docs | 来源、迁移边界、验收场景及证据 |

## 来源与授权

上游版权及授权说明保留在 [README.upstream.md](README.upstream.md)。上游 README 列明 Richard.Hu 版权和 HSL 授权方式；本 Fork 不将通信库改名为自有协议库、不更改授权逻辑，也不为整个项目重新授予 MIT 许可证。

TKCode.Arc 的 MIT 许可及 notices 位于 [docs/third-party](docs/third-party)。详细源码映射、13.0.0/13.0.3 版本差异和验收范围见 [docs/UPSTREAM.md](docs/UPSTREAM.md)。此前本地反编译目录未加入本分支。

## 下一步

继续迁移批量读取、数据表、编码与数组表达式；补原版 EXE 同步骤交互对照，再逐项推进其他协议页面。
