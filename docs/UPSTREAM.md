# 上游来源与重构边界

上游： https://github.com/dathlin/HslCommunication

Fork： https://github.com/Muachaochao/XBRCommunicationDemo

本次检出的上游 master 提交：`f64bfc994787bbe1923723c9ac34ddf74c332043`（v13.0.0）。此前本地行为基线使用发布包 v13.0.3；两个版本不能视为完全相同。

保留上游 WinForms 项目与 Git 历史，新增 `src/Xbr.Desktop`（Avalonia + Arc）和 `src/Xbr.Communication`（通信服务）。项目显示名为 **XBR Communication Studio / XBR 通信工作台**。界面和新服务为本次重构内容；HSL 通信能力及上游源码保留原作者归属。

上游 README 列明 Richard.Hu 的版权和通信库授权方式，仓库根目录未提供可确认的开源许可证。公开可见和可 Fork 不代表所有库及源代码可无条件重新授权。保留上游 README，不为整个 Fork 添加 MIT 许可证，不改动库的授权机制。
TKCode.Arc 为独立 MIT 组件，声明保存在 docs/third-party。

## 源码映射

| 新实现 | 官方参考源码 |
|---|---|
| 连接参数及保存/恢复 | HslCommunicationDemo/Modbus/FormModbus.cs、FormModbus.Designer.cs |
| FC23 特殊功能 | HslCommunicationDemo/Modbus/ModbusControl.cs |
| 站号搜索 | HslCommunicationDemo/Modbus/StationSearchControl.cs |
| 普通读写、编码与数组表达式 | HslCommunicationDemo/DemoControl/UserControlReadWriteOp.cs |
| 批量读写、按字反转与行宽 | HslCommunicationDemo/DemoControl/BatchReadControl.cs |
| 数据表、表达式、曲线与导出 | HslCommunicationDemo/DemoControl/DataTableControl.cs、DataTableItem.cs、FormCurveMonitor.cs |

后续优先从上述官方源码迁移；此前反编译目录只在本地留作 v13.0.3 对照，不加入公开提交。
本轮未改变已验证的 HSL 13.0.3 二进制依赖，避免切换版本造成行为漂移。依赖由使用者在本机提供，不新增上传通信 DLL、反编译源码、配置或授权信息。

## 当前完成与验收

Arc 深色页面、基础类型读写、原始报文、轮询、单设备明文配置、FC23、站号搜索、批量工具、数据表和多曲线已接入。
23 项协议矩阵与 12 项监控集成场景通过；前者比较旧 DLL 请求/响应，后者覆盖表格、编码、取消、锁和曲线历史。二者都不等同于原版 EXE 全部按钮或真机验收。
尚待：批量解析面板、表格双击写值、曲线样式/时间窗、导出格式扩展、其他管道与完整设备矩阵。
