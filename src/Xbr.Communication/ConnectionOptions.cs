namespace Xbr.Communication;

public sealed record ConnectionOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 502;
    public int ConnectTimeout { get; init; } = 5000;
    public int ReceiveTimeout { get; init; } = 10000;
    public byte Station { get; init; } = 1;
    public int DataFormat { get; init; } = 2;
    public bool AddressStartWithZero { get; init; } = true;
    public bool StringReverse { get; init; }
    public bool CheckMessageId { get; init; } = true;
    public bool StationCheck { get; init; } = true;
    public bool DisableFunctionCode06 { get; init; }
    public int BroadcastStation { get; init; } = -1;
    public int WordReadBatchLength { get; init; } = 120;
    public string AddressMapping { get; init; } = "No Address Mapping";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535 || ConnectTimeout <= 0 || ReceiveTimeout <= 0
            || DataFormat is < 0 or > 3 || BroadcastStation is < -1 or > 255 || WordReadBatchLength is < 1 or > 125
            || !ModbusAddressMappings.Names.Contains(AddressMapping))
            throw new ArgumentException("检查主机、端口、超时、地址映射、广播站号及分批长度（1–125）。");
    }
}
