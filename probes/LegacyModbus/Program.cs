using System;
using System.Linq;
using System.Globalization;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using Newtonsoft.Json;
class Program {
 static void Main(string[] args) {
  var c=new ModbusTcpNet("127.0.0.1",int.Parse(args[0]),1){ReceiveTimeOut=200,ConnectTimeOut=2000,DataFormat=(DataFormat)int.Parse(args[2]),WordReadBatchLength=120,DisableFunctionCode06=args[3]=="true"};
  object output;
  try {
   var connected=c.ConnectServer();if(!connected.IsSuccess)throw new Exception(connected.Message);
   string op=args[1];
   if(op=="bool-read") {
    var r=c.ReadBool("100",3);output=new{ok=r.IsSuccess,content=r.IsSuccess?string.Join(", ",r.Content):"",code=r.ErrorCode};
   } else if(op=="bool-scalar"||op=="bool-array") {
    var r=op=="bool-scalar"?c.Write("100",true):c.Write("100",new bool[]{true,false,true});output=new{ok=r.IsSuccess,content="写入成功",code=r.ErrorCode};
   } else if(op=="array"||op=="disable06") {
    OperateResult r=op=="array"?c.Write("100",new ushort[]{10,20,30}):c.Write("100",(ushort)4321);
    output=new{ok=r.IsSuccess,content="写入成功",code=r.ErrorCode};
   } else if(op=="fc23") {
    var r=c.ReadWrite("100",2,"100",new byte[]{0,10,0,20});output=new{ok=r.IsSuccess,content=r.IsSuccess?BitConverter.ToString(r.Content).Replace("-",""):"",code=r.ErrorCode};
   } else if(op=="format") {
    var r=c.ReadUInt32("100",1);output=new{ok=r.IsSuccess,content=r.IsSuccess?string.Join(", ",r.Content):"",code=r.ErrorCode};
   } else {
    var r=c.ReadUInt16("100",ushort.Parse(args[4]));output=new{ok=r.IsSuccess,content=r.IsSuccess?string.Join(", ",r.Content):"",code=r.ErrorCode};
   }
  } catch(Exception ex){output=new{ok=false,content="",code=-999,error=ex.ToString()};}
  finally{c.ConnectClose();}
  Console.WriteLine(JsonConvert.SerializeObject(output));
 }
}
