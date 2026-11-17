using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HslCommunication.ModBus;

// Only a loopback fixture. Never connects to factory equipment.
var evidence = new List<object>();
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var server = Task.Run(async () => {
    using var socket = await listener.AcceptTcpClientAsync(stop.Token);
    using var stream = socket.GetStream();
    ushort register = 1234;
    while (!stop.IsCancellationRequested) {
        var header = new byte[7];
        try { await stream.ReadExactlyAsync(header, stop.Token); } catch (EndOfStreamException) { break; }
        int length = (header[4] << 8 | header[5]) - 1;
        var body = new byte[length]; await stream.ReadExactlyAsync(body, stop.Token);
        byte[] response;
        if(body[0] == 3) response = [3, 2, (byte)(register >> 8), (byte)register];
        else if(body[0] == 6) { register = (ushort)(body[3] << 8 | body[4]); response = body; }
        else if(body[0] == 16) { register = (ushort)(body[6] << 8 | body[7]); response = body[..5]; }
        else response = [(byte)(body[0] | 128), 1];
        var reply = header.Concat(response).ToArray(); reply[4]=0; reply[5]=(byte)(response.Length+1);
        evidence.Add(new { request=Convert.ToHexString(header.Concat(body).ToArray()), response=Convert.ToHexString(reply) });
        await stream.WriteAsync(reply, stop.Token);
    }
});
try {
    var client = new ModbusTcpNet("127.0.0.1", port, 1);
    client.ConnectTimeOut=2000; client.ReceiveTimeOut=2000;
    var defaults = new { client.Station, client.AddressStartWithZero, client.DataFormat, client.IsStringReverse,
        client.IsCheckMessageId, client.StationCheckMatch, client.BroadcastStation, client.WordReadBatchLength, client.DisableFunctionCode06 };
    var connected=client.ConnectServer();
    if(!connected.IsSuccess) throw new Exception(connected.Message);
    var read=client.ReadUInt16("100");
    if(!read.IsSuccess || read.Content!=1234) throw new Exception("Read failed: "+read.Message);
    var write=client.Write("100", (ushort)4321);
    if(!write.IsSuccess) throw new Exception("Write failed: "+write.Message);
    var reread=client.ReadUInt16("100");
    if(!reread.IsSuccess || reread.Content!=4321) throw new Exception("Readback mismatch");
    client.ConnectClose(); await server;
    Console.WriteLine(JsonSerializer.Serialize(new { passed=true, runtime=Environment.Version.ToString(), defaults, evidence },new JsonSerializerOptions{WriteIndented=true}));
} catch(Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new {passed=false,error=ex.ToString(),evidence})); Environment.ExitCode=1; }
finally { stop.Cancel(); listener.Stop(); }
