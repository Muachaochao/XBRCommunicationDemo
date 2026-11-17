# 原版功能迁移与无实物验收

目标是一比一还原功能与通信行为，UI 使用 Avalonia + Arc 重构。保留官方源码与归属；更改产品显示名不等同于协议迁移完成。

原版参考基线为已检出的 v13.0.0 官方 Demo，运行通信依赖为用户本地 v13.0.3 DLL。`baseline/official-protocol-pages.csv` 从真实文件路径提取 PLC、Robot、Modbus、CNC、Instrument、Sensor、Light 下 189 个非 Designer 的 Form 源文件，包含部分辅助窗体；189 不是协议数量，也不是完成数量。UI 目录是设备族摘要，尚未覆盖全部窗体。

## 本轮完成范围

| 官方页面 / 控件 | 新界面与服务 | 当前可验收功能 | 尚待迁移 |
|---|---|---|---|
| FormModbusServer | PLC 模拟器 / ModbusServerService | TCP/UDP/双端口启动选项、站号、字节序、反转、站号隔离、写掩码、IPv6、远程写开关、接收帧日志 | 串口从站、在线会话、故障注入、服务端内存编辑/数据池等扩展 |
| DataSimulateControl | 表达式模拟表 | 单次/周期、x 次数、r 随机数、类型分派、编码、首错停止、取消、SimulateWrite 属性导入导出 | TSV/剪贴板、表达式帮助、完整配置根节点往返 |
| FormMelsecBinary / FormMcServer | 工业 PLC / IndustrialPlcService | MC 3E Binary TCP 客户端、原生虚拟服务端、基础读写、内存、数据池 | 网络号/站号/IO 等专用参数及高级功能 |
| FormSiemens / FormS7Server | 工业 PLC / IndustrialPlcService | S7-1200 TCP 连接、原生虚拟服务端、基础读写、内存、数据池 | 其他 S7 型号、机架槽位/TSAP、原生 STRING/WSTRING 控件、DB 管理 |
| FormOmron / FormOmronServer | 工业 PLC / IndustrialPlcService | FINS TCP 连接、原生虚拟服务端、基础读写、内存、数据池 | FINS 路由/机型/节点、SID、UDP 及高级命令 |
| Robot 下的官方页面 | 协议目录入口 | 检索厂家、定位源码及状态 | 机器人实际读写及专用命令尚未接入 |

Modbus 映射包括无映射、Inovance AM/H3U/H5U-Easy/EVO、Delta AS/DVP、MegMeet、XinJE XC/XD-XL、WeCon LX5V、INVT TS，共 12 个选项。H5U/Easy 合并为同一套映射；当前客户端仅 TCP，目录里的 RTU/串口是原版覆盖范围，不代表新界面已支持。

## 可复现验收标准

运行 `dotnet run --project probes/ServerSimulationIntegration`，要求退出码为 0，JSON 中 `passed=true`、`failures=0`。当前 61/61，通过场景及真实接收帧见 `baseline/server-simulation.json`。

1. Modbus：本机启动、客户端连接，表达式计数递增；停止后数值冻结；非法模拟写入在首错处停止，后续行不执行；客户端写入后回读一致。
2. 每种 Modbus 映射独立重启服务端，映射在两侧一致，写入 UInt16=2468 并回读。AM/EVO 用 MW100，其他厂家用 D100；无映射用数字地址。
3. 远程写开关关闭时客户端写入应返回 HSL 错误。
4. MC、S7、FINS 分别通过真正的 TCP 连接本地对应 HSL 服务端；8 种整数/浮点类型和 Int16/Float 数组：客户端写→服务端本地读，服务端本地写→客户端读，均逐值一致。
5. 三类协议的 Bool 数组、字符串、非法地址、保存数据池→改零→恢复→客户端回读均通过。
6. UI：目录搜索 Siemens 只匹配对应项；打开切到 S7 页面并填 M100；模拟表 XML 保留未知属性；新增页面能通过 Skia 实际渲染。

DesktopSmoke 还实际执行 ViewModel 命令：启动 S7 服务端并连接、写入/读取 123、停止并检查状态；启动 Modbus 服务端、自动填入本机客户端、运行模拟并读取递增 Int64，最后停止全部服务和连接。截图使用标注为布局预览的状态，与上述真实连接断言分开。

固定长度 String 调用保留官方通用读写控件的 HSL API 语义。S7 的 Write(string) 写入两字节头，ReadString(address,length,encoding) 是定长读取；探针对 M100 写字符串后从 M102 读取正文。不要将通用定长读取冒充原生 S7 STRING 功能。MC/FINS 的字符串读取长度按字计，S7 按字节计。

Modbus 模拟周期最低由 20ms 调度循环决定，与原版一致。空编码使用 HSL 默认字符串写入重载；显式编码按选择值写入。未知返回类型/写入错误停止循环。服务端参数在启动时生效，运行中需停止再修改。

## 无实物操作

Modbus：左侧 PLC 模拟器 → 默认 TCP :1502 启动 → 开始循环模拟 → 填入为本机设备 → 左侧 Modbus TCP 连接。默认 x 为 Int64，地址 100 占四字，随机数使用 104 避免重叠；可选 Int64 从 100 读取。点表和曲线可直接连接此模拟器。

其他 PLC：左侧工业 PLC → 选协议（MC/S7/FINS）→ 默认本机 :1600 启动服务端 → 连接 PLC → 写入/读取。可独立读写服务端本地内存，或保存/恢复数据池文件。连接/服务端均停止后切换协议。

## 下一步

按原版 MC/S7/FINS 的连接控件补齐专用参数，复用现有批量、点表、曲线和模拟表达式；扩展参数默认值、请求和返回值的对照用例。随后从 FormABBWebApi、FormFanucRobot/Server、FormKukaTcp 提取机器人调用链与读写入口，逐页实现并建立本机协议应答验收。持续将“已接入”“本机验收通过”“尚待迁移”分开记录，无需等待实物。
