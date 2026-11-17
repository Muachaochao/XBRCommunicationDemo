using System;
using System.Xml.Linq;
using HslCommunication.BasicFramework;
using System.Linq;
using System.Globalization;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using Newtonsoft.Json;
class Program {
 static void Main(string[] args) {
  if(args[0]=="tablexml") {
   var source=XElement.Load(args[1]);var result=new XElement("DataTable");
   foreach(var e in source.Elements("DataTableItem")){var tag=new HslCommunicationDemo.DemoControl.DataTableItem();tag.LoadByXmlElement(e);result.Add(tag.ToXmlElement());}
   Console.WriteLine(result.ToString(SaveOptions.DisableFormatting));return;
  }
  var c=new ModbusTcpNet("127.0.0.1",int.Parse(args[0]),1){ReceiveTimeOut=200,ConnectTimeOut=2000,DataFormat=(DataFormat)int.Parse(args[2]),WordReadBatchLength=120,DisableFunctionCode06=args[3]=="true"};
  object output;
  try {
   var connected=c.ConnectServer();if(!connected.IsSuccess)throw new Exception(connected.Message);
   string op=args[1];
   if(op.StartsWith("batch-")) {
    var r=c.Read("100",ushort.Parse(args[4]));string content="";
    if(r.IsSuccess) {
     byte[] b=op=="batch-reverse"?SoftBasic.BytesReverseByWord(r.Content):r.Content;
     if(op=="batch-UInt16")content=string.Join(", ",c.ByteTransform.TransUInt16(b,0,b.Length/2));
     else if(op=="batch-UInt32")content=string.Join(", ",c.ByteTransform.TransUInt32(b,0,b.Length/4));
     else if(op=="batch-Bits")content=string.Join(Environment.NewLine,Enumerable.Range(0,(b.Length+7)/8).Select(line=>string.Join("   ",b.Skip(line*8).Take(8).Select(x=>string.Concat(Enumerable.Range(0,8).Select(i=>(x&(1<<i))!=0?'1':'0'))))));
     else content=b.ToHexString(' ');
    }
    output=new{ok=r.IsSuccess,content,code=r.ErrorCode};
   } else if(op=="bool-read") {
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
