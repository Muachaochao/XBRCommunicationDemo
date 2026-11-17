using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Xbr.Communication;

var results=new List<object>();bool passed=true;
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
async Task Check(string name,Func<Task> action){try{await action();results.Add(new{name,passed=true});}catch(Exception ex){passed=false;results.Add(new{name,passed=false,error=ex.ToString()});}}
await using var fixture=new Fixture("table");var service=new ModbusService();await service.ConnectAsync(new(){Port=fixture.Port});
await Check("batch-split-and-cache-reverse",async()=>{
 var offset=fixture.Frames.Count;var hex=await service.ReadBatchAsync("100",121,"Hex");
 Assert(service.BatchByteCount==242&&fixture.Frames.Count-offset==2,"121 words not split in two");
 Assert(hex.StartsWith("11 22 33 44"),"Raw bytes mismatch");
 Assert((await service.RenderBatchAsync("Hex",true)).StartsWith("22 11 44 33"),"Word reverse mismatch");
 Assert(await service.RenderBatchAsync("Hex")==hex,"Reverse mutated cache");
 Assert((await service.RenderBatchAsync("Byte",false,3)).Split(Environment.NewLine).Length==81,"Line count mismatch");
});
await Check("batch-write-conversion-and-readback",async()=>{
 await service.WriteBatchAsync("100","UInt16","10,20,30");
 var rows=await service.ReadTableAsync([new("a","100","ushort"),new("b","101","ushort"),new("c","102","ushort")]);
 Assert(string.Join(",",rows.Select(r=>r.Value))=="10,20,30","UInt16 batch write mismatch");
});
await Check("mixed-scalar-array-expression-bool-byte",async()=>{
 var tags=new TagDefinition[]{new("scalar","100","ushort"),new("array","100","ushort",3),new("scaled","102","ushort",Expression:"x * 0.1 + 10"),new("coil","100","bool"),new("bytes","100","byte",2),new("first","100","byte")};
 var rows=await service.ReadTableAsync(tags);
 Assert(rows.All(r=>r.Success),"Mixed read failed");
 Assert(rows[0].Value=="10"&&rows[1].Value=="[10, 20, 30]"&&rows[2].NumericValue==13&&rows[3].NumericValue==1&&rows[4].Value=="00 0A 00 14"&&rows[5].Value=="0","Mixed values mismatch");
 Assert(rows[1].NumericValue==null&&rows[4].NumericValue==null,"Arrays must not plot as a scalar");
 Assert(rows.All(r=>r.Timestamp==rows[0].Timestamp),"Rows in one scan must share a curve timestamp");
});
await Check("per-row-network-expression-failure-continue",async()=>{
 var rows=await service.ReadTableAsync([new("bad-device","900","ushort"),new("bad-expression","100","ushort",Expression:"x +"),new("next","102","ushort")]);
 Assert(!rows[0].Success&&rows[0].ErrorCode==2&&!rows[1].Success&&rows[2].Success&&rows[2].Value=="30","Row failures stopped next read or leaked stale value");
 Assert(rows[0].Value==""&&rows[0].NumericValue==null,"Error replaced by stale/zero sample");
});
await Check("batch-line-width-and-format-reparse",async()=>{
 var text=await service.RenderBatchAsync("Byte",false,3);
 Assert(text.Split(Environment.NewLine).Length==81,"Byte line width mismatch");
 Assert((await service.RenderBatchAsync("Hex",false,8)).Contains(Environment.NewLine),"Hex line width mismatch");
});
await Check("ascii-utf8-string-no-numeric-plot",async()=>{
 await service.WriteBatchAsync("200","Hex","41 42 43 44");
 await service.WriteBatchAsync("204","Hex","E4 BD A0 E5 A5 BD");
 var rows=await service.ReadTableAsync([new("text","200","string",4,"ASCII"),new("utf8-cn","204","string",4,"UTF8")]);
 Assert(rows.All(r=>r.Success&&r.NumericValue==null),"String rows must not become numeric curve samples");
 // Official DataTableControl passes Length directly to the HSL ReadString overload,
 // where the unit is Modbus words. Trailing NUL padding therefore remains in Content.
 Assert(rows[0].Value.TrimEnd('\0')=="ABCD"&&rows[0].Value.Length==8,$"ASCII decoding mismatch: {Escape(rows[0].Value)}");
 Assert(rows[1].Value.TrimEnd('\0')=="你好"&&rows[1].Value.Length==4,$"UTF8 decoding mismatch: {Escape(rows[1].Value)}");
});
await Check("table-xml-official-source-roundtrip",async()=>{
 var tags=new[]{new TagDefinition("a","100","ushort"),new("b","200","string",4,"UTF8",Unit:"m,\"s"),new("c","102","float",Expression:"x*0.1",Description:"pressure")};
 var xml=new TableDefinition(tags,1000,10).Save();var file=Path.GetTempFileName();
 try{
  xml.Save(file);var psi=new ProcessStartInfo(Path.GetFullPath("probes/LegacyModbus/bin/Debug/net481/LegacyModbus.exe")){UseShellExecute=false,RedirectStandardOutput=true};psi.ArgumentList.Add("tablexml");psi.ArgumentList.Add(file);
  using var process=Process.Start(psi)!;var text=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();Assert(process.ExitCode==0,"Official XML baseline failed");
  var official=TableDefinition.Load(XElement.Parse(text));Assert(official.Tags.SequenceEqual(tags),"Official DataTableItem did not restore identical fields");
  var restored=TableDefinition.Load(xml);Assert(restored.Tags.SequenceEqual(tags)&&restored.Interval==1000&&restored.SleepTime==10,"Local table roundtrip mismatch");
 }finally{File.Delete(file);}
});
await Check("xml-unknown-fields-follow-row-on-reorder",()=>{
 var xml=XElement.Parse("<DataTable Other='keep'><Child/><DataTableItem Name='a' Address='100' DataTypeCode='ushort' Vendor='A'/><DataTableItem Name='b' Address='102' DataTypeCode='ushort' Vendor='B'/></DataTable>");
 var table=TableDefinition.Load(xml);var saved=new TableDefinition(table.Tags.Reverse().ToArray()).Save(xml);
 Assert((string?)saved.Attribute("Other")=="keep"&&saved.Element("Child")!=null&&(string?)saved.Elements("DataTableItem").First().Attribute("Vendor")=="B","Unknown fields changed ownership");return Task.CompletedTask;
});
await Check("cancel-during-row-delay-no-further-requests",async()=>{
 using var cancel=new CancellationTokenSource();var start=fixture.Frames.Count;bool cancelled=false;
 var task=service.ReadTableAsync([new("a","100","ushort"),new("b","102","ushort")],500,cancel.Token);
 var deadline=Stopwatch.StartNew();while(fixture.Frames.Count==start&&deadline.ElapsedMilliseconds<2000)await Task.Delay(5);
 cancel.Cancel();try{await task;}catch(OperationCanceledException){cancelled=true;}
 Assert(cancelled&&fixture.Frames.Count==start+1,"Cancellation sent another row");
 var r=await service.ReadAsync("100","UInt16",1);Assert(r=="10","Gate not released after cancel");
});
await Check("table-scan-lock-prevents-interleaving",async()=>{
 int start=fixture.Frames.Count;var scan=service.ReadTableAsync([new("a","100","ushort"),new("b","102","ushort")],50);
 var single=service.ReadAsync("101","UInt16",1);await Task.WhenAll(scan,single);
 var addresses=fixture.Frames.Skip(start).Select(h=>Convert.ToInt32(h.Substring(12,4),16)).ToArray();
 Assert(addresses.SequenceEqual(new[]{100,102,101}),"Requests interleaved inside table scan");
});
await Check("bounded-history-failure-timestamp-csv-escaping",()=>{
 var buffer=new TrendBuffer(3);var tag=new TagDefinition("a,\"quoted","100","ushort",Unit:"m/s");
 for(int i=0;i<5;i++)buffer.Add("a",new(tag,DateTimeOffset.UnixEpoch.AddSeconds(i),i==3?"":i.ToString(),i==3?null:i,i==3?2:0,i==3?"failure":""));
 var samples=buffer.Get("a");Assert(samples.Count==3&&samples[0].Timestamp==DateTimeOffset.UnixEpoch.AddSeconds(2)&&!samples[1].Success&&samples[1].NumericValue==null,"Retention/failure mismatch");
 var csv=buffer.ToCsv();Assert(csv.Contains("\"a,\"\"quoted\"")&&csv.Contains("1970-01-01T00:00:04")&&csv.Contains("failure"),"CSV escaping or timestamp lost");buffer.Clear();Assert(buffer.Get("a").Count==0,"Clear failed");return Task.CompletedTask;
});
await Check("multi-curve-aligned-failure-gap",()=>{
 var left=new TagDefinition("left","100","ushort");var right=new TagDefinition("right","101","ushort");
 var aligned=new TrendBuffer();var first=DateTimeOffset.UnixEpoch;var second=first.AddSeconds(1);
 aligned.Add("left",new(left,first,"10",10));aligned.Add("left",new(left,second,"",null,2,"failure"));
 aligned.Add("right",new(right,first,"20",20));aligned.Add("right",new(right,second,"30",30));
 var frames=aligned.GetFrames(["left","right"]);
 Assert(aligned.Capacity==4000&&frames.Count==2&&frames[1].Values["left"]==null&&frames[1].Values["right"]==30,"Aligned curve frame mismatch");
 Assert(aligned.GetCompleteFrames(["left","right"]).Count==1,"Failed curve round must be excluded");
 return Task.CompletedTask;
});
await service.DisconnectAsync();
Directory.CreateDirectory("docs/baseline");File.WriteAllText("docs/baseline/monitoring-integration.json",JsonSerializer.Serialize(new{passed,results,requests=fixture.Frames,responses=fixture.Responses},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Monitoring integration: {results.Count} scenarios, passed={passed}");Environment.ExitCode=passed?0:1;

static string Escape(string value)=>value.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n");
