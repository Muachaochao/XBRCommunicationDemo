using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using Xbr.Communication;

var root=Path.GetFullPath(args.Length>0?args[0]:".");
var legacy=Path.Combine(root,"probes/LegacyModbus/bin/Debug/net481/LegacyModbus.exe");
var results=new List<object>();bool all=true;
var scenarios=new List<Scenario>{new("exception","read",0,false,1),new("timeout","read",0,false,1),new("normal","array",0,false,3),new("normal","disable06",0,true,1),new("normal","fc23",0,false,2)};
foreach(var op in new[]{"bool-read","bool-scalar","bool-array"})scenarios.Add(new("normal",op,0,false,3));
for(int i=0;i<4;i++)scenarios.Add(new("normal","format",i,false,1));
foreach(ushort count in new ushort[]{119,120,121,240})scenarios.Add(new("normal","read",2,false,count));
foreach(var scenario in scenarios) {
 var baseline=await RunLegacy(scenario);
 var modern=await RunModern(scenario);
 var expectedChunks=scenario.Operation=="read" && scenario.Mode=="normal"?(scenario.Count+119)/120:1;
 bool same=baseline.Result==modern.Result && baseline.Frames.SequenceEqual(modern.Frames) && baseline.Responses.SequenceEqual(modern.Responses) && modern.Frames.Count==expectedChunks;
 if(scenario.Mode=="exception"||scenario.Mode=="timeout")same &= !modern.Result.Ok;
 else same &= modern.Result.Ok;
 if(scenario.Operation=="fc23")same &=modern.Result.Content=="000A0014";
 if(scenario.Operation=="format")same &=modern.Result.Content==new[]{"287454020","571556915","860098850","1144201745"}[scenario.Format];
 if(scenario.Operation=="bool-read")same &=modern.Result.Content=="True, False, True";
 all &= same;
 results.Add(new{scenario,passed=same,baseline,modern});
}
// Search uses the same service as the UI. Station 2 alone returns success.
await using(var fixture=new Fixture("search")) {
 var service=new ModbusService();await service.ConnectAsync(new(){Port=fixture.Port});
 var found=new List<StationSearchResult>();
 await service.SearchStationsAsync(0,4,200,"0",1,true,true,new InlineProgress(found.Add),CancellationToken.None);
 string restored="";try {restored=await service.ReadAsync("100","UInt16",1);}catch(HslOperationException ex){restored=ex.ErrorCode.ToString();} // station must have been restored to 1
 await service.DisconnectAsync();
 bool passed=found.Count==3&&found[^1].Station==2&&found[^1].Success&&fixture.Frames[^1].Substring(8,2)=="01";
 all &=passed;results.Add(new{scenario="search-stop-on-match-and-restore",passed,found,frames=fixture.Frames,restored});
}
await using(var fixture=new Fixture("normal")) {
 var service=new ModbusService();await service.ConnectAsync(new(){Port=fixture.Port});
 using var cancel=new CancellationTokenSource();int reports=0;bool cancelled=false;
 try {await service.SearchStationsAsync(0,255,200,"0",1,true,false,new InlineProgress(_=>{reports++;cancel.Cancel();}),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
 await service.ReadAsync("100","UInt16",1);await service.DisconnectAsync();
 bool passed=cancelled&&reports==1&&fixture.Frames.Count==2&&fixture.Frames[^1].Substring(8,2)=="01";
 all &=passed;results.Add(new{scenario="search-cancel-and-restore",passed,reports,frames=fixture.Frames});
}
var report=new{passed=all,baseline="Original HSL DLL under .NET Framework 4.8.1; same public API branches as recovered WinForms handlers. Not original EXE UI automation.",normalization="Only first two MBAP transaction-ID bytes removed; response timeout code compared, wall clock not compared.",results};
Directory.CreateDirectory(Path.Combine(root,"docs/baseline"));File.WriteAllText(Path.Combine(root,"docs/baseline/modbus-matrix.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Matrix: {results.Count} scenarios, passed={all}");Environment.ExitCode=all?0:1;

async Task<RunResult> RunLegacy(Scenario s) {
 await using var fixture=new Fixture(s.Mode);
 var psi=new ProcessStartInfo(legacy){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
 foreach(var a in new[]{fixture.Port.ToString(),s.Operation,s.Format.ToString(),s.Disable06?"true":"false",s.Count.ToString()})psi.ArgumentList.Add(a);
 using var p=Process.Start(psi)!;var output=await p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();
 if(p.ExitCode!=0)throw new Exception(await p.StandardError.ReadToEndAsync());
 using var json=JsonDocument.Parse(output);var e=json.RootElement;
 return new(new(e.GetProperty("ok").GetBoolean(),e.GetProperty("content").GetString()!,e.GetProperty("code").GetInt32()),fixture.Frames.ToList(),fixture.Responses.ToList());
}
async Task<RunResult> RunModern(Scenario s) {
 await using var fixture=new Fixture(s.Mode);var service=new ModbusService();Outcome result;
 try {
  await service.ConnectAsync(new(){Port=fixture.Port,ReceiveTimeout=200,DataFormat=s.Format,DisableFunctionCode06=s.Disable06});
  string content=s.Operation switch {
   "bool-read"=>await service.ReadAsync("100","Bool",3),
   "bool-scalar"=>await service.WriteAsync("100","Bool","true"),
   "bool-array"=>await service.WriteAsync("100","Bool","[true,false,true]"),
   "array"=>await service.WriteAsync("100","UInt16","[10,20,30]"),
   "disable06"=>await service.WriteAsync("100","UInt16","4321"),
   "format"=>await service.ReadAsync("100","UInt32",1),
   "fc23"=>await service.ReadWriteAsync("100",2,"100","000A0014"),
   _=>await service.ReadAsync("100","UInt16",s.Count)
  };result=new(true,content,0);
 }catch(HslOperationException ex){result=new(false,"",ex.ErrorCode);}
 finally {await service.DisconnectAsync();}
 return new(result,fixture.Frames.ToList(),fixture.Responses.ToList());
}
record Scenario(string Mode,string Operation,int Format,bool Disable06,ushort Count);
record Outcome(bool Ok,string Content,int Code);
record RunResult(Outcome Result,List<string> Frames,List<string> Responses);
sealed class InlineProgress(Action<StationSearchResult> callback):IProgress<StationSearchResult>{public void Report(StationSearchResult r)=>callback(r);}
sealed class Fixture:IAsyncDisposable {
 readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource stop=new();readonly Task server;
 public int Port{get;} public List<string> Frames{get;}=new();public List<string> Responses{get;}=new();
 public Fixture(string mode){listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;server=Serve(mode);}
 async Task Serve(string mode) {
  try {using var socket=await listener.AcceptTcpClientAsync(stop.Token);using var stream=socket.GetStream();
   var registers=new Dictionary<int,ushort>();
   for(int i=0;i<1000;i++)registers[i]=(ushort)(i%2==0?0x1122:0x3344);
   while(!stop.IsCancellationRequested) {
    var h=new byte[7];await stream.ReadExactlyAsync(h,stop.Token);var b=new byte[(h[4]<<8|h[5])-1];await stream.ReadExactlyAsync(b,stop.Token);
    Frames.Add(Convert.ToHexString(h.Concat(b).ToArray()[2..]));
    if(mode=="timeout"){await Task.Delay(1000,stop.Token);continue;}
    byte[] response;
    if(mode=="exception" || mode=="search" && h[6]!=2)response=[(byte)(b[0]|128),2];
    else {
     int address=b[1]<<8|b[2];int quantity=b[3]<<8|b[4];
     if(b[0]==1){response=new byte[2+(quantity+7)/8];response[0]=1;response[1]=(byte)(response.Length-2);for(int i=2;i<response.Length;i++)response[i]=0x55;}
     else if(b[0]==5||b[0]==15){response=b[..5];}
     else if(b[0]==3){response=new byte[2+quantity*2];response[0]=3;response[1]=(byte)(quantity*2);for(int i=0;i<quantity;i++){ushort v=registers[address+i];response[2+i*2]=(byte)(v>>8);response[3+i*2]=(byte)v;}}
     else if(b[0]==6){registers[address]=(ushort)quantity;response=b;}
     else if(b[0]==16){for(int i=0;i<quantity;i++)registers[address+i]=(ushort)(b[6+i*2]<<8|b[7+i*2]);response=b[..5];}
     else if(b[0]==23){int wa=b[5]<<8|b[6];int wq=b[7]<<8|b[8];for(int i=0;i<wq;i++)registers[wa+i]=(ushort)(b[10+i*2]<<8|b[11+i*2]);response=new byte[2+quantity*2];response[0]=23;response[1]=(byte)(quantity*2);for(int i=0;i<quantity;i++){var v=registers[address+i];response[2+i*2]=(byte)(v>>8);response[3+i*2]=(byte)v;}}
     else response=[(byte)(b[0]|128),1];
    }
    var reply=h.Concat(response).ToArray();reply[4]=0;reply[5]=(byte)(response.Length+1);Responses.Add(Convert.ToHexString(reply[2..]));await stream.WriteAsync(reply,stop.Token);
   }
  }catch(Exception ex)when(ex is EndOfStreamException or OperationCanceledException or IOException){}
 }
 public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await server;stop.Dispose();}
}
