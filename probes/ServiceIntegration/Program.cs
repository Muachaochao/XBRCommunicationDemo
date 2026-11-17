using System.Net;
using System.Net.Sockets;
using System.Text.Json;


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
    var service = new Xbr.Communication.ModbusService();
    var options = new Xbr.Communication.ConnectionOptions { Port=port };
    await service.ConnectAsync(options);
    if(await service.ReadAsync("100","UInt16",1)!="1234") throw new Exception("Service read mismatch");
    await service.WriteAsync("100","UInt16","4321");
    if(await service.ReadAsync("100","UInt16",1)!="4321") throw new Exception("Service write/readback mismatch");
    if(await service.RawAsync("010300640001",true)!="10E1") throw new Exception("Raw response mismatch");
    await service.DisconnectAsync(); await server;
    bool disconnectedRejected=false;
    try { await service.ReadAsync("100","UInt16",1); } catch(InvalidOperationException) {disconnectedRejected=true;}
    if(!disconnectedRejected) throw new Exception("Disconnected read was accepted");
    var xml=Xbr.Communication.ConfigurationStore.Save(options,new System.Xml.Linq.XElement("Device",new System.Xml.Linq.XAttribute("Custom","keep")));
    var restored=Xbr.Communication.ConfigurationStore.Load(xml);
    if(restored!=options || xml.Attribute("Custom")?.Value!="keep") throw new Exception("Configuration roundtrip failed");
    Console.WriteLine(JsonSerializer.Serialize(new {passed=true, checks=new[]{"connect","typed-read","write-readback","core-message","disconnect-rejection","xml-roundtrip-preserve-unknown"}, evidence},new JsonSerializerOptions{WriteIndented=true}));
} catch(Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new {passed=false,error=ex.ToString(),evidence})); Environment.ExitCode=1; }
finally { stop.Cancel(); listener.Stop(); }

