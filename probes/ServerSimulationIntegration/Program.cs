using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xbr.Communication;

int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
void Equal(string expected,string actual){if(expected!=actual)throw new Exception($"Expected {expected}, actual {actual}");}
var checks=new List<object>();var failures=0;
async Task Check(string name,Func<Task> action){
    try{await action();checks.Add(new{name,passed=true,error=""});}
    catch(Exception ex){failures++;checks.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine($"FAIL {name}: {ex.Message}");}
}
var messages=new ConcurrentQueue<string>();
var server=new ModbusServerService();var client=new ModbusService();server.Message+=messages.Enqueue;
try{
    var port=FreePort();await server.StartAsync(new(Port:port));
    await client.ConnectAsync(new(){Host="127.0.0.1",Port=port,ReceiveTimeout=1000});
    await Check("Modbus expression simulation and cancellation",async()=>{
        await server.StartSimulationAsync([new("counter","100",20,"x * 10"),new("random","104",40,"r.Next(0, 1000)")],_=>{});
        await Task.Delay(250);
        if(long.Parse(await client.ReadAsync("100","Int64",1))<=0)throw new Exception("Counter did not advance");
        await server.StopSimulationAsync();var frozen=await client.ReadAsync("100","Int64",1);
        await Task.Delay(80);Equal(frozen,await client.ReadAsync("100","Int64",1));
    });
    await Check("Modbus client write/read",async()=>{await client.WriteAsync("200","UInt16","4321");Equal("4321",await client.ReadAsync("200","UInt16",1));});
    await Check("Modbus simulation stops on first write failure",async()=>{
        var failure=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await server.StartSimulationAsync([new("bad","INVALID_ADDRESS",0,"1"),new("untouched","220",0,"999")],snapshot=>{if(snapshot.Error.Length>0)failure.TrySetResult();});
        await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));await server.StopSimulationAsync();
        Equal("0",await client.ReadAsync("220","UInt16",1));
        if(server.IsSimulating)throw new Exception("Simulation did not stop after an error");
    });
}finally{await client.DisconnectAsync();await server.StopAsync();}
foreach(var mapping in ModbusAddressMappings.Names){
    await Check($"Modbus mapping: {mapping}",async()=>{
        var port=FreePort();
        try{
            await server.StartAsync(new(Port:port,AddressMapping:mapping));
            await client.ConnectAsync(new(){Host="127.0.0.1",Port=port,AddressMapping=mapping,ReceiveTimeout=1000});
            var address=mapping switch{"No Address Mapping"=>"100","Inovance AM" or "Inovance EVO"=>"MW100",_=>"D100"};
            await client.WriteAsync(address,"UInt16","2468");Equal("2468",await client.ReadAsync(address,"UInt16",1));
        }finally{await client.DisconnectAsync();await server.StopAsync();}
    });
}
await Check("Modbus remote writes disabled",async()=>{
    try{
        var port=FreePort();await server.StartAsync(new(Port:port,RemoteWrite:false));
        await client.ConnectAsync(new(){Host="127.0.0.1",Port=port,ReceiveTimeout=1000});
        try{await client.WriteAsync("100","UInt16","123");}catch(HslOperationException){return;}
        throw new Exception("Remote write unexpectedly succeeded");
    }finally{await client.DisconnectAsync();await server.StopAsync();}
});
for(int protocol=0;protocol<IndustrialPlcService.Protocols.Length;protocol++){
    var plc=new IndustrialPlcService();plc.Message+=messages.Enqueue;
    var address=IndustrialPlcService.DefaultAddress(protocol);var label=IndustrialPlcService.Protocols[protocol];
    try{
        await Check(label+" connect native HSL simulator",async()=>{
            var port=FreePort();await plc.StartServerAsync(protocol,port);await plc.ConnectAsync(protocol,"127.0.0.1",port);
        });
        foreach(var (type,text,count) in new[]{("Int16","-123",1),("UInt16","54321",1),("Int32","-123456",1),("UInt32","3234567890",1),
            ("Int64","-1234567890123",1),("UInt64","12345678901234",1),("Float","12.5",1),("Double","-1234.125",1),
            ("Int16","[1,-2,3]",3),("Float","[1.25,-2.5,3.75]",3)}){
            await Check(label+" "+type+" "+text,async()=>{
                var expected=text.Trim('[',']').Replace(",",", ");
                await plc.WriteAsync(address,type,text);Equal(expected,await plc.ReadAsync(address,type,(ushort)count,true));
                await plc.WriteAsync(address,type,text,true);Equal(expected,await plc.ReadAsync(address,type,(ushort)count));
            });
        }
        await Check(label+" Bool array",async()=>{
            var bitAddress=protocol switch{0=>"M100",1=>"M200.0",_=>"D200.0"};
            await plc.WriteAsync(bitAddress,"Bool","[true,false,true]");Equal("True, False, True",await plc.ReadAsync(bitAddress,"Bool",3,true));
            Equal("True, False, True",await plc.ReadAsync(bitAddress,"Bool",3));
        });
        await Check(label+" UTF-8 string",async()=>{
            await plc.WriteAsync(address,"String","HELLO!");
            // Fixed-length S7 reads include the header written by HSL Write(string).
            Equal("HELLO!",await plc.ReadAsync(protocol==1?"M102":address,"String",(ushort)(protocol==1?6:3)));
        });
        await Check(label+" data pool save/restore",async()=>{
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".plc-memory");
            try{
                await plc.WriteAsync(address,"Int16","789",true);await plc.SaveMemoryAsync(path);
                await plc.WriteAsync(address,"Int16","0",true);await plc.LoadMemoryAsync(path);
                Equal("789",await plc.ReadAsync(address,"Int16",1));
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        await Check(label+" invalid address",async()=>{
            try{await plc.ReadAsync("INVALID_ADDRESS","Int16",1);}catch(HslOperationException){return;}
            throw new Exception("Invalid address was accepted");
        });
    }finally{await plc.DisconnectAsync();await plc.StopServerAsync();}
}
Directory.CreateDirectory("docs/baseline");
await File.WriteAllTextAsync("docs/baseline/server-simulation.json",JsonSerializer.Serialize(new{
    passed=failures==0,total=checks.Count,failures,checks,receivedFrames=messages.Count(message=>message.StartsWith("RX ")||message.StartsWith("客户端报文 ")),messages=messages.ToArray()
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Server simulation: {checks.Count-failures}/{checks.Count} passed");
return failures==0?0:1;
