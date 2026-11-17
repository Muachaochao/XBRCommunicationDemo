# Modbus TCP 迁移基线与调用链

## 已确认默认参数

| 参数 | 原版界面默认值 | 生效值/来源 |
|---|---|---|
| PipeSetting | TcpPipe | PipeSelectControl.cs:25 |
| IP / Port | 127.0.0.1 / 502 | PipeSelectControl.cs:1233；FormModbus.cs:420 |
| ConnectTimeOut / ReceiveTimeOut | 5000 / 10000 ms | PipeSelectControl.cs:1207 / 1185 |
| Station | 1 | FormModbus.cs:458 |
| AddressStartWithZero | true | FormModbus.cs:446 |
| DataFormat | index 2 = CDAB | FormModbus.cs:98、134 |
| IsStringReverse | false | 默认未勾选；DLL 探针确认 |
| IsCheckMessageId | true | FormModbus.cs:423 |
| StationCheckMatch | true | FormModbus.cs:382 |
| DisableFunctionCode06 | false | 默认未勾选；DLL 探针确认 |
| BroadcastStation | 文本空白 | 不赋值，DLL 默认 -1 |
| WordReadBatchLength | 文本空白 | 不赋值，DLL 默认 120 |
| 读/写地址 | 连接成功后设 100 | FormModbus.cs:208；UserControlReadWriteOp.cs:470 |
| 字符串长度 | 10 | SetReadWriteNet 的可选参数 |
| 异步模式 | true | FormModbus.cs:208 |

来源都相对于 reverse/demo，DLL 源码位于 reverse/hsl。默认值同时受载入配置及全局设置影响，表中是新建页面基线。

## 连接与释放

FormModbus 构造 → InitializeComponent → 注册复选框事件。
FormSiemens_Load（原版命名如此）→ 字节序 CDAB → 添加 ModbusControl、AddressExampleControl、StationSearchControl、CodeExampleControl → 禁用读写区。

button1_Click → byte.TryParse(Station) → 关闭旧连接 → new ModbusTcpNet → 复制站号、地址基准、消息号/站号校验、06 开关、日志、广播站号、分批长度 → ComboBox1_SelectedIndexChanged 设置字节序 → 字符串反转。

PipeSelectControl.IniPipe → TcpPipe 分支 → CreatePipeTcpNet → 设置 IP/Port、连接及接收超时 → SetPipeTcpSetting 应用高级参数 → CommunicationPipe 赋值。
其他分支包括 Debug、UDP、Serial、MoxaCom、SSL、MQTT、DTU，不能因为页面名含 TCP 就漏掉。

HslFormContent.DeviceConnectPLC → 普通管道调用 ConnectServer；DTU 管道跳过主动连接 → 成功后绑定数据表、模拟及代码生成上下文 → FormModbus 启用读写，绑定 IReadWriteNet、批量读、原始报文、特殊功能、搜索与代码示例。

button2_Click → 按钮/读写区禁用状态切换 → ExtraCloseAction → ConnectClose。
FormClosing → CheckTableDataChanged（可取消关闭）→ 未取消且已连接时调用断开流程。

## 读写链（UInt16 示例）

UserControlReadWriteOp.button_read_ushort_Click → GetReadAddress → 根据数组/标量及同步/异步选择 ReadUInt16[Async] → HSL 类型转换与字节序 → ModbusTcpNet.Read[Async] → ModbusHelper.Read[Async] → 地址解析和分批构帧 → ReadFromCoreServer → PackCommandWithHeader（MBAP/事务 ID）→ PipeTcpNet 发送/接收 → 报文校验 → UnpackResponseContent/ExtractActualData → 类型转换 → RenderReadResult/ReadResultRender → 耗时统计、结果及代码示例。

button_write_ushort_Click → GetWriteValueText → 方括号数组走 GetArrayFromString，普通输入走 try_get_ushort_value → Write[Async](address, ushort[]/ushort) → HSL Modbus 构帧 → TCP 请求/响应 → RenderWriteResult → 日志、结果、耗时和代码示例。标量与数组不能简单合并：标量默认用 06，数组用 16；DisableFunctionCode06 会影响标量路径。

轮询由 UserControlReadWriteOp 的读/写计时控制驱动；失败是否继续受 Program.Settings.TimerReadWriteFailedContinue 影响。曲线通过独立监控窗体；不等同于单纯轮询文本显示。

原始报文分两种委托：完整帧 usePackAndUnpack=false；Modbus Core 使用库默认封装/解包。Core 读取响应只返回提取后的数据载荷，例如 10E1，不含站号/功能码。

## 配置链与兼容边界

FormModbus.SaveXmlParameter → PipeSelectControl.SaveXmlParameter → Station、AddressStartWithZero、DataFormat、StringReverse、BroadcastStation、StationCheckMatch、WordReadBatchLength、DisableFunctionCode06 → GetDataTable。
LoadXmlParameter 反向恢复；旧配置缺失的广播/批次字段保持空值，站号校验默认 true，禁用 06 默认 false；数据表非空时切换数据表标签。
IsCheckMessageId 原版没有写入/恢复，迁移版也不添加此属性。
HslFormContent 外层还负责 GUID、名称、类型、密码加密和文件管理，这一层尚未迁移，当前只支持单设备明文元素；拒绝加密和非 TCP 配置，不能称为完整旧配置兼容。

## 兼容性决策

已在本机 Windows .NET 10.0.15 验证原 HSL 13.0.3 DLL 的 TCP 连接、UInt16 读、标量写、回读，证据见 baseline/hsl-net10-probe.log。
当前选择 Avalonia 12.1.6 + CommunityToolkit.Mvvm 8.4.2 + net10.0-windows，进程内 HSL 适配。
WindowsDesktop 框架引用满足旧 DLL 对 WinForms 的依赖，不表示新 UI 使用 WinForms。
尚未证明其他协议、System.Web 路径、本机驱动或非 Windows 平台兼容；发现不兼容功能时再评估独立 Framework worker。

## 页面实现与差异登记

已实现连接参数、连接/断开、类型化数组读取、标量/数组写入、UTF-8 字符串、Hex、原始报文、只读轮询、结果和有限日志、单设备明文 XML、批量读取与写入、官方点表语义和多曲线。
连接参数当前在连接期间锁定，原版部分参数可即时改变；普通读写页字符串暂限 UTF-8，数据表支持六种原版编码；轮询至少 50ms 且错误停止；读写暂共用地址、类型；尚无定时写、原版统计和代码生成。
待迁移：批量解析面板与行内搜索、表格双击写值、曲线线型/颜色/时间窗设置、帧消息工具细节、压力测试、模拟、远程调试、逻辑程序、地址与代码示例、高级 TCP 参数和其他管道。
因此当前是可运行的首个页面基础版本，不是“完整等价页面”，不计入全量验收通过。

## 可复现命令

```powershell
dotnet run --project probes/HslCompatibility
dotnet run --project probes/ServiceIntegration
dotnet build src/Xbr.Desktop
dotnet run --project src/Xbr.Desktop
```

探针仅连接自己创建的 loopback 模拟器，不访问现场设备。早期探针只覆盖单寄存器；新 ModbusMatrix 模拟器已覆盖限定的分包、异常、超时、UInt32 字节序、线圈、FC23 等场景。仍不能据此声称任意设备和所有类型均通过。


## 第三轮实现与验收

- 接入 TKCode.Arc **1.1.0** 稳定版（MIT），官方兼容范围 Avalonia 12.1.5 至 13.0 之前；当前 12.1.6 已构建和渲染验证。UseArc 注册控件，明确使用深色主题；按钮采用 Arc 原生控件。许可与 notices 保留在 docs/third-party。
- FC23 特殊功能调用与原版 ModbusControl 一致：独立读取地址/字长度、写入地址/Hex 值，返回 Hex 和调用示例。
- 站号搜索默认 0–255、500ms、地址 0、长度 1、Word 模式、匹配即停默认关闭，与恢复源码一致。支持 Word/Bool、匹配即停及取消。
- 扫描持有连接锁，阻止普通操作插入；finally 恢复站号、超时及 TCP 关闭策略。恢复进入扫描前的实际关闭策略，是对原版固定恢复为 1 的明确改进。
- 停止需要等待当前同步请求结束，最长受当前请求接收超时限制；关闭窗口会先取消并等待搜索/轮询，再释放连接。
- 新服务补齐 HslOperationException，保留库的 ErrorCode 与原始 Message，便于对照。

### 23 项自动场景

21 项旧 DLL/net481 API 与现代服务对照：异常 83/02、200ms 接收超时、UInt16 数组写、禁用06标量写、FC23、四种 UInt32 字节序、119/120/121/240 字分批、Bool读、单线圈写、多线圈写，以及 Hex/UInt16/UInt32/Bits/按字反转五类批量渲染。
另 2 项扫描集成检查：匹配即停后恢复站号、取消后恢复站号。

报告 baseline/modbus-matrix.json 保存双方请求、响应、结果、错误码。仅移除 MBAP 前两字节事务 ID；未忽略功能码、站号、地址、数据和分包数量。
对字节序、FC23、Bool读还有独立预期值断言。超时比较错误码，未将墙钟耗时作为等价结论。
**旧基线执行原 DLL 的 API 分支，与反编译窗体一致，但尚未自动点击原版 EXE；不能称为原版整机交互验收。**

### 组件与界面检查

DesktopSmoke 在真实 Avalonia/Skia 渲染路径中加载 App、Arc 和窗口，检查未连接、连接、扫描三个状态的操作可用性，并输出寄存器/FC23/搜索截图。FC23 与搜索截图中的连接状态为布局模拟状态，不是真机通信证据。
目前未验证多缩放、高对比度、键盘遍历、长稳与真机。

```powershell
dotnet build probes/LegacyModbus
dotnet run --project probes/ModbusMatrix
dotnet run --project probes/DesktopSmoke -p:BaseOutputPath=E:\ChaoChao\XBRCommunicationDemo\artifacts\smoke\
```

下一步优先补批量读/数据表、编码与数组表达式，扩展 Bool 搜索、超时取消与恢复测试，再建立原版 EXE 同步骤交互对照；尚未迁移的子标签继续按清单逐项补齐。

## 第四轮实现与验收

BatchReadControl 的主要读写语义已迁移：地址与字长度、Hex/ASCII/Byte/Int16/UInt16/Int32/UInt32/Float/Double/Int64/UInt64/Bits、按字反转、每行数量、缓存重解析，以及 Bits 不反写。读取仍由 HSL `Read` 负责 120 字默认分包。

DataTableItem 字段、XML 结构和保存时的未知字段保留均与原版源码一致。列表每轮按完整扫描结束时间生成共享时间戳；单项网络或表达式失败不会阻断下一项。长度空白表示标量，正数表示数组；字符串长度直接传给 HSL `ReadString`，单位为 Modbus 字，末尾 NUL 不主动裁剪。

曲线按原版勾选语义改为多点位，采用 Arc 原生 `ChartSeries`。数值/Bool/表达式标量可绘制，数组、Byte 和字符串不绘制。Arc 会把 `NaN` 映射为 0，因此任一选中曲线失败时会将该轮次从曲线窗口剔除，避免伪造零点；表格与 CSV 仍保留该轮和原始错误。历史按 4000 轮截断。

### 12 项监控集成场景

1. 121 字分包与缓存反转不污染。
2. 批量 UInt16 写入后逐点回读。
3. 标量、数组、表达式、Bool、Byte 混合读取。
4. 单轮所有点共享时间戳。
5. 设备错误和表达式错误后继续后续点位。
6. 每行数量修改后的缓存重解析。
7. ASCII 与 UTF8 字长度及多字节字符串。
8. 原版 net481 DataTableItem XML 官方实现往返。
9. XML 重排时未知属性和子元素跟随原行。
10. 点间等待期间取消不再产生下一请求并释放连接锁。
11. 单表扫描期间普通读取不会插入分包序列。
12. 4000 轮上限、失败时间戳、CSV 转义、失败补位与双曲线对齐。

证据：baseline/monitoring-integration.json。测试使用本地 Modbus TCP 模拟器，尚未与原版 EXE 同步点击，也未覆盖真实设备。

### 界面渲染证据

DesktopSmoke 输出 `arc-batch-read.png`、`arc-data-table.png` 和 `arc-live-curve.png`，并断言曲线集合挂载了 2 条 `ChartSeries`。这些是布局与 Skia 渲染证据，不是通信成功证据。

### 已知差异与下一步

原版 BatchRead 的解析子表、行内搜索、代码示例尚未迁移；数据表暂未实现双击当前值写入；Arc 曲线暂未提供原版的线宽、颜色、线型、格式化与自由缩放时间窗。下一步优先补这些差异，再做原版 EXE 同步骤报文/结果对照。
