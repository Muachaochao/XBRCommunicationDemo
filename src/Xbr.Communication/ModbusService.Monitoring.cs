using System.Globalization;
using System.Text;
using DynamicExpresso;
using HslCommunication;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.ModBus;
namespace Xbr.Communication;

public sealed partial class ModbusService
{
    private byte[] batchBuffer=[];
    public int BatchByteCount=>batchBuffer.Length;
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetACP();
    public Task<string> ReadBatchAsync(string address,ushort count,string display,bool reverse=false,int lineCount=-1)=>Execute(()=>{
        var c=client??throw new InvalidOperationException("请先连接设备。");
        if(count==0)throw new ArgumentException("批量长度须大于零。");
        batchBuffer=Value(c.Read(address,count));return RenderBatchCore(display,reverse,lineCount);
    });
    public Task<string> RenderBatchAsync(string display,bool reverse=false,int lineCount=-1)=>Execute(()=>RenderBatchCore(display,reverse,lineCount));
    private string RenderBatchCore(string display,bool reverse,int lineCount){
        var c=client??throw new InvalidOperationException("请先连接设备。");
        var data=reverse?SoftBasic.BytesReverseByWord(batchBuffer):batchBuffer.ToArray();
        var t=c.ByteTransform;
        return display switch{
            "Hex"=>RenderHex(data,lineCount),"ASCII"=>SoftBasic.GetAsciiStringRender(data),"Byte"=>Join(data,lineCount),
            "Int16"=>Join(t.TransInt16(data,0,data.Length/2),lineCount),"UInt16"=>Join(t.TransUInt16(data,0,data.Length/2),lineCount),
            "Int32"=>Join(t.TransInt32(data,0,data.Length/4),lineCount),"UInt32"=>Join(t.TransUInt32(data,0,data.Length/4),lineCount),
            "Float"=>Join(t.TransSingle(data,0,data.Length/4),lineCount),"Double"=>Join(t.TransDouble(data,0,data.Length/8),lineCount),
            "Int64"=>Join(t.TransInt64(data,0,data.Length/8),lineCount),"UInt64"=>Join(t.TransUInt64(data,0,data.Length/8),lineCount),
            "Bits"=>RenderBits(data,lineCount),
            _=>throw new ArgumentException("不支持的显示格式。")
        };
    }
    public Task<string> WriteBatchAsync(string address,string display,string text)=>Execute(()=>{
        var c=client??throw new InvalidOperationException("请先连接设备。");var t=c.ByteTransform;
        var parts=text.Trim().Trim('[',']').Split([',',' ','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        T[] Parse<T>(Func<string,T> parse)=>parts.Select(parse).ToArray();var ci=CultureInfo.InvariantCulture;
        byte[] bytes=display switch{
            "Hex"=>Convert.FromHexString(string.Concat(parts)),"ASCII"=>SoftBasic.GetFromAsciiStringRender(text),
            "Byte"=>Parse(byte.Parse),"Int16"=>t.TransByte(Parse(x=>short.Parse(x,ci))),"UInt16"=>t.TransByte(Parse(x=>ushort.Parse(x,ci))),
            "Int32"=>t.TransByte(Parse(x=>int.Parse(x,ci))),"UInt32"=>t.TransByte(Parse(x=>uint.Parse(x,ci))),
            "Int64"=>t.TransByte(Parse(x=>long.Parse(x,ci))),"UInt64"=>t.TransByte(Parse(x=>ulong.Parse(x,ci))),
            "Float"=>t.TransByte(Parse(x=>float.Parse(x,ci))),"Double"=>t.TransByte(Parse(x=>double.Parse(x,ci))),
            _=>throw new ArgumentException("Bits 显示不支持反写，与原版一致。")};
        if(bytes.Length==0)throw new ArgumentException("写入内容不能为空。");
        Check(c.Write(address,bytes));return "批量写入成功";
    });
    public Task<IReadOnlyList<BatchParseResult>> ParseBatchAsync(IReadOnlyList<BatchParseDefinition> definitions)=>Execute(()=>{
        var c=client??throw new InvalidOperationException("请先连接设备。");
        if(batchBuffer.Length==0)throw new InvalidOperationException("请先批量读取。");
        var results=new List<BatchParseResult>();
        foreach(var definition in definitions){
            try{results.Add(ParseBatchRow(c.ByteTransform,batchBuffer,definition));}
            catch(Exception ex){results.Add(new(definition.Name,definition.Type,"","",ex.Message));}
        }
        return (IReadOnlyList<BatchParseResult>)results;
    });
    public Task WriteTagAsync(TagDefinition tag,string text)=>Execute(()=>{
        tag.Validate();var c=client??throw new InvalidOperationException("请先连接设备。");
        var culture=CultureInfo.InvariantCulture;var array=tag.Length>0;
        string[] Parts()=>text.Trim().Trim('[',']').Split([',',' ','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        OperateResult result=tag.DataTypeCode switch{
            "bool"=>array?c.Write(tag.Address,Parts().Select(x=>x=="1"||(x!="0"&&bool.Parse(x))).ToArray()):
                c.Write(tag.Address,text=="1"||(text!="0"&&bool.Parse(text))),
            "short"=>array?c.Write(tag.Address,Parts().Select(x=>short.Parse(x,culture)).ToArray()):c.Write(tag.Address,short.Parse(text,culture)),
            "ushort"=>array?c.Write(tag.Address,Parts().Select(x=>ushort.Parse(x,culture)).ToArray()):c.Write(tag.Address,ushort.Parse(text,culture)),
            "int"=>array?c.Write(tag.Address,Parts().Select(x=>int.Parse(x,culture)).ToArray()):c.Write(tag.Address,int.Parse(text,culture)),
            "uint"=>array?c.Write(tag.Address,Parts().Select(x=>uint.Parse(x,culture)).ToArray()):c.Write(tag.Address,uint.Parse(text,culture)),
            "long"=>array?c.Write(tag.Address,Parts().Select(x=>long.Parse(x,culture)).ToArray()):c.Write(tag.Address,long.Parse(text,culture)),
            "ulong"=>array?c.Write(tag.Address,Parts().Select(x=>ulong.Parse(x,culture)).ToArray()):c.Write(tag.Address,ulong.Parse(text,culture)),
            "float"=>array?c.Write(tag.Address,Parts().Select(x=>float.Parse(x,culture)).ToArray()):c.Write(tag.Address,float.Parse(text,culture)),
            "double"=>array?c.Write(tag.Address,Parts().Select(x=>double.Parse(x,culture)).ToArray()):c.Write(tag.Address,double.Parse(text,culture)),
            "string"=>c.Write(tag.Address,text,GetEncoding(tag.StringEncoding)),
            "byte"=>WriteByte(c,tag,text,array),
            _=>throw new ArgumentException($"类型不受支持：{tag.DataTypeCode}")
        };
        Check(result);return "写入成功";
    });
    public async Task<IReadOnlyList<TagSample>> ReadTableAsync(IReadOnlyList<TagDefinition> tags,int sleep=0,CancellationToken token=default){
        if(tags.Count>200||sleep<0)throw new ArgumentException("点表最多200项，间隔不能为负数。");
        await gate.WaitAsync(token);
        try{return await Task.Run(async()=>{
            _=client??throw new InvalidOperationException("请先连接设备。");var results=new List<TagSample>();
            foreach(var tag in tags){
                token.ThrowIfCancellationRequested();
                try {tag.Validate();results.Add(ReadTagCore(tag));}
                catch(Exception ex)when(ex is not OperationCanceledException){results.Add(new(tag,DateTimeOffset.Now,"",null,ex is HslOperationException h?h.ErrorCode:-1,ex.Message));}
                if(sleep>0)await Task.Delay(sleep,token);
            }
            // The original table adds one history timestamp after a complete scan.
            var completedAt=DateTimeOffset.Now;
            return (IReadOnlyList<TagSample>)results.Select(sample=>sample with{Timestamp=completedAt}).ToArray();
        },token);}finally{gate.Release();}
    }
    private TagSample ReadTagCore(TagDefinition tag){
        var c=client!;var len=tag.Length;object result;
        object Read<T>(Func<string,OperateResult<T>> one,Func<string,ushort,OperateResult<T[]>> many){
            var interpreter=new Interpreter();
            object Eval(T x)=>string.IsNullOrWhiteSpace(tag.Expression)?x!:interpreter.Eval(tag.Expression,new Parameter("x",x!));
            return len<=0?Eval(Value(one(tag.Address))):Value(many(tag.Address,(ushort)len)).Select(Eval).ToArray();
        }
        result=tag.DataTypeCode switch{
            "bool"=>Read(c.ReadBool,c.ReadBool),"short"=>Read(c.ReadInt16,c.ReadInt16),"ushort"=>Read(c.ReadUInt16,c.ReadUInt16),
            "int"=>Read(c.ReadInt32,c.ReadInt32),"uint"=>Read(c.ReadUInt32,c.ReadUInt32),"long"=>Read(c.ReadInt64,c.ReadInt64),
            "ulong"=>Read(c.ReadUInt64,c.ReadUInt64),"float"=>Read(c.ReadFloat,c.ReadFloat),"double"=>Read(c.ReadDouble,c.ReadDouble),
            "string"=>Value(c.ReadString(tag.Address,(ushort)Math.Max(0,len),GetEncoding(tag.StringEncoding))),
            "byte"=>len<=0?Value(c.Read(tag.Address,1))[0]:Value(c.Read(tag.Address,(ushort)len)).ToHexString(' '),
            _=>throw new ArgumentException("类型不受支持。")};
        double? numeric=null;
        if(result is bool b)numeric=b?1:0;
        else if(result is IConvertible && result is not string && result is not Array){var n=Convert.ToDouble(result,CultureInfo.InvariantCulture);if(double.IsFinite(n))numeric=n;}
        var text=result is Array array?"["+string.Join(", ",array.Cast<object>().Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)))+"]":Convert.ToString(result,CultureInfo.InvariantCulture)??"";
        return new(tag,DateTimeOffset.Now,text,numeric);
    }
    private static BatchParseResult ParseBatchRow(IByteTransform transform,byte[] data,BatchParseDefinition row){
        if(row.ByteIndex<0||row.ByteIndex>=data.Length)throw new ArgumentOutOfRangeException(nameof(row),"字节索引超出读取范围。");
        var type=row.Type.ToLowerInvariant();
        var (length,lengthCode)=BatchLength(data,row.Length);
        var name=Identifier(string.IsNullOrWhiteSpace(row.Name)?$"value{row.ByteIndex}":row.Name);
        var index=row.ByteIndex;
        object value;
        string code;
        if(type=="string"){
            var parts=(row.Length??"").Split([',',';','|','.',':'],StringSplitOptions.RemoveEmptyEntries);
            var encodingName=parts.Length>1?parts[1]:"ASCII";
            var encoding=GetEncoding(encodingName);
            if(length<0)throw new ArgumentException("字符串长度不能空白。");
            value=transform.TransString(data,index,length,encoding);
            code=$"    string {name} = [变量名].ByteTransform.TransString( read.Content, {index}, {lengthCode}, {EncodingCode(encodingName)} );";
        }
        else if(type=="byte"){
            if(length<=0){value=data[index];code=$"    byte {name} = read.Content[{index}];";}
            else{var bytes=data.Skip(index).Take(length).ToArray();value=bytes.ToHexString(' ');code=$"    byte[] {name} = read.Content.SelectMiddle( {index}, {lengthCode} );";}
        }
        else if(type=="bool"){value=ReadScalar((d,i)=>transform.TransBool(d,i),(d,i,n)=>transform.TransBool(d,i,n),data,index,length);code=Code(name,"bool",index,length,lengthCode,"TransBool");}
        else if(type=="short"){value=ReadScalar((d,i)=>transform.TransInt16(d,i),(d,i,n)=>transform.TransInt16(d,i,n),data,index,length);code=Code(name,"short",index,length,lengthCode,"TransInt16");}
        else if(type=="ushort"){value=ReadScalar((d,i)=>transform.TransUInt16(d,i),(d,i,n)=>transform.TransUInt16(d,i,n),data,index,length);code=Code(name,"ushort",index,length,lengthCode,"TransUInt16");}
        else if(type=="int"){value=ReadScalar((d,i)=>transform.TransInt32(d,i),(d,i,n)=>transform.TransInt32(d,i,n),data,index,length);code=Code(name,"int",index,length,lengthCode,"TransInt32");}
        else if(type=="uint"){value=ReadScalar((d,i)=>transform.TransUInt32(d,i),(d,i,n)=>transform.TransUInt32(d,i,n),data,index,length);code=Code(name,"uint",index,length,lengthCode,"TransUInt32");}
        else if(type=="long"){value=ReadScalar((d,i)=>transform.TransInt64(d,i),(d,i,n)=>transform.TransInt64(d,i,n),data,index,length);code=Code(name,"long",index,length,lengthCode,"TransInt64");}
        else if(type=="ulong"){value=ReadScalar((d,i)=>transform.TransUInt64(d,i),(d,i,n)=>transform.TransUInt64(d,i,n),data,index,length);code=Code(name,"ulong",index,length,lengthCode,"TransUInt64");}
        else if(type=="float"){value=ReadScalar((d,i)=>transform.TransSingle(d,i),(d,i,n)=>transform.TransSingle(d,i,n),data,index,length);code=Code(name,"float",index,length,lengthCode,"TransSingle");}
        else if(type=="double"){value=ReadScalar((d,i)=>transform.TransDouble(d,i),(d,i,n)=>transform.TransDouble(d,i,n),data,index,length);code=Code(name,"double",index,length,lengthCode,"TransDouble");}
        else throw new ArgumentException($"不支持的数据类型：{row.Type}");
        var text=value is Array array?"["+string.Join(", ",array.Cast<object>().Select(item=>Convert.ToString(item,CultureInfo.InvariantCulture)))+"]":
            Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        return new(row.Name,type,text,code);
    }
    private static object ReadScalar<T>(Func<byte[],int,T> readOne,Func<byte[],int,int,T[]> readMany,byte[] data,int index,int length) where T:struct
        =>length<=0?readOne(data,index):readMany(data,index,length);
    private static (int Length,string Code) BatchLength(byte[] data,string text){
        if(string.IsNullOrWhiteSpace(text))return (-1,"-1");
        var value=text.Trim();
        if(value.StartsWith('[')&&value.EndsWith(']')){
            if(!int.TryParse(value[1..^1],out var index)||index<0||index>=data.Length)throw new ArgumentException("长度索引超出读取范围。");
            return (data[index],$"read.Content[{index}]");
        }
        if(!int.TryParse(value.Split([',',';','|','.',':'],StringSplitOptions.RemoveEmptyEntries)[0],out var length))
            throw new ArgumentException("长度格式无效。");
        return (length,length.ToString(CultureInfo.InvariantCulture));
    }
    private static string Code(string name,string type,int index,int length,string lengthCode,string method)
        =>length<=0?$"    {type} {name} = [变量名].ByteTransform.{method}( read.Content, {index} );":
            $"    {type}[] {name} = [变量名].ByteTransform.{method}( read.Content, {index}, {lengthCode} );";
    private static string EncodingCode(string name)=>name.ToUpperInvariant() switch{
        "ASCII"=>"Encoding.ASCII","ANSI"=>"Encoding.Default","UTF8"=>"Encoding.UTF8","UTF16"=>"Encoding.Unicode",
        "UTF16BIG"=>"Encoding.BigEndianUnicode","GB2312"=>"Encoding.GetEncoding( \"gb2312\" )",
        _=>$"Encoding.GetEncoding( \"{name}\" )"};
    private static string Identifier(string value){
        var chars=value.Select(ch=>char.IsLetterOrDigit(ch)||ch=='_'?ch:'_').ToArray();
        var result=new string(chars);
        return string.IsNullOrEmpty(result)||char.IsDigit(result[0])?$"value_{result}":result;
    }
    private static OperateResult WriteByte(ModbusTcpNet client,TagDefinition tag,string text,bool array){
        if(!array)return client.Write(tag.Address,byte.Parse(text,CultureInfo.InvariantCulture));
        var bytes=text.Trim().StartsWith('[')?
            text.Trim().Trim('[',']').Split([',',' ','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(x=>byte.Parse(x,CultureInfo.InvariantCulture)).ToArray():
            Convert.FromHexString(text.Replace(" ",""));
        if(bytes.Length!=tag.Length)throw new ArgumentException($"输入 {bytes.Length} 字节，点位要求 {tag.Length} 字节。");
        return client.Write(tag.Address,bytes);
    }
    public static Encoding GetEncoding(string name){
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return name.ToUpperInvariant() switch{"ASCII"=>Encoding.ASCII,"ANSI"=>Encoding.GetEncoding((int)GetACP()),"UTF8"=>Encoding.UTF8,"UTF16"=>Encoding.Unicode,"UTF16BIG"=>Encoding.BigEndianUnicode,"GB2312"=>Encoding.GetEncoding(936),_=>throw new ArgumentException("不支持的字符串编码。")};
    }
    private static string RenderHex(byte[] data,int lineCount){
        if(lineCount<=0||data.Length<=lineCount)return data.ToHexString(' ');
        return string.Join(Environment.NewLine,data.Chunk(lineCount).Select(row=>row.ToHexString(' ')));
    }
    private static string RenderBits(byte[] data,int lineCount){
        var count=lineCount<=0?8:lineCount;
        return string.Join(Environment.NewLine,data.Chunk(count).Select(row=>string.Join("   ",row.Select(BitString))));
    }
    private static string BitString(byte value)=>string.Concat(Enumerable.Range(0,8).Select(i=>(value&(1<<i))!=0?'1':'0'));
    private static string Join<T>(IEnumerable<T> values,int lineCount=-1){
        var text=values.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)??"").ToArray();
        if(lineCount<=0)return string.Join(", ",text);
        var builder=new StringBuilder();
        for(var index=0;index<text.Length;index++){
            if(index!=0)builder.Append(',');
            if(index!=0&&index%lineCount==0)builder.Append(Environment.NewLine);
            builder.Append(text[index]);
        }
        return builder.ToString();
    }
}
