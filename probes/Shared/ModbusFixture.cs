using System.Net;
using System.Net.Sockets;
sealed class Fixture:IAsyncDisposable {
 readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource stop=new();readonly Task server;
 public int Port{get;} public List<string> Frames{get;}=new();public List<string> Responses{get;}=new();
 public Fixture(string mode){listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;server=Serve(mode);}
 async Task Serve(string mode) {
  try {using var socket=await listener.AcceptTcpClientAsync(stop.Token);using var stream=socket.GetStream();
   var registers=new Dictionary<int,ushort>();
   for(int i=0;i<1000;i++)registers[i]=(ushort)(i%2==0?0x1122:0x3344);
   if(mode=="table")for(int i=200;i<210;i++)registers[i]=0;
   while(!stop.IsCancellationRequested) {
    var h=new byte[7];await stream.ReadExactlyAsync(h,stop.Token);var b=new byte[(h[4]<<8|h[5])-1];await stream.ReadExactlyAsync(b,stop.Token);
    Frames.Add(Convert.ToHexString(h.Concat(b).ToArray()[2..]));
    if(mode=="timeout"){await Task.Delay(1000,stop.Token);continue;}
    byte[] response;
    if(mode=="exception" || mode=="table" && (b[1]<<8|b[2])==900 || mode=="search" && h[6]!=2)response=[(byte)(b[0]|128),2];
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
