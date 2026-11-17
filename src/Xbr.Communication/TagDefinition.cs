using System.Globalization;
using System.Xml.Linq;
namespace Xbr.Communication;

public sealed record TagDefinition(string Name,string Address,string DataTypeCode,int Length=-1,
    string StringEncoding="ASCII",string Expression="",string Unit="",string Description="")
{
    public static readonly string[] DataTypes=["bool","byte","short","ushort","int","uint","long","ulong","float","double","string"];
    public void Validate(){
        if(string.IsNullOrWhiteSpace(Address)||!DataTypes.Contains(DataTypeCode)||Length>65535)
            throw new ArgumentException($"点位 {Name}: 地址、类型或长度无效。");
    }
    public XElement ToXml(XElement? original=null){
        var e=original==null?new XElement("DataTableItem"):new XElement(original);
        foreach(var (key,value) in new[]{("Name",Name),("Address",Address),("DataTypeCode",DataTypeCode),("Length",Length==-1?null:Length.ToString(CultureInfo.InvariantCulture)),
            ("StringEncoding",DataTypeCode=="string"?StringEncoding:null),("Expression",string.IsNullOrEmpty(Expression)?null:Expression),
            ("Unit",string.IsNullOrEmpty(Unit)?null:Unit),("Description",string.IsNullOrEmpty(Description)?null:Description)})e.SetAttributeValue(key,value);
        return e;
    }
    public static TagDefinition FromXml(XElement e){
        string Get(string name,string fallback="")=>e.Attribute(name)?.Value??fallback;
        int len=int.TryParse(Get("Length"),out var length)?length:-1;
        var result=new TagDefinition(Get("Name"),Get("Address"),Get("DataTypeCode").ToLowerInvariant(),len,Get("StringEncoding","ASCII"),Get("Expression"),Get("Unit"),Get("Description"));result.Validate();return result;
    }
}
public sealed record TagSample(TagDefinition Tag,DateTimeOffset Timestamp,string Value,double? NumericValue,int ErrorCode=0,string Error="")
{
    public bool Success=>string.IsNullOrEmpty(Error);
}
public sealed record TableDefinition(IReadOnlyList<TagDefinition> Tags,int Interval=1000,int SleepTime=0)
{
    public static TableDefinition Load(XElement e){
        int Read(string key,int fallback)=>int.TryParse(e.Attribute(key)?.Value,out var n)?n:fallback;
        var table=new TableDefinition(e.Elements("DataTableItem").Select(TagDefinition.FromXml).ToArray(),Read("Interval",1000),Read("SleepTime",0));
        if(table.Interval<0||table.SleepTime<0||table.Tags.Count>200)throw new ArgumentException("点表最多200项，间隔不能为负数。");
        return table;
    }
    public XElement Save(XElement? source=null){
        var e=source==null?new XElement("DataTable"):new XElement(source);
        var old=e.Elements("DataTableItem").ToArray();e.Elements("DataTableItem").Remove();
        e.SetAttributeValue("Interval",Interval);e.SetAttributeValue("SleepTime",SleepTime);
        var remaining=old.ToList();
        foreach(var tag in Tags){
            var match=remaining.FirstOrDefault(x=>(string?)x.Attribute("Address")==tag.Address && (string?)x.Attribute("Name")==tag.Name && string.Equals((string?)x.Attribute("DataTypeCode"),tag.DataTypeCode,StringComparison.OrdinalIgnoreCase));
            if(match!=null)remaining.Remove(match);
            e.Add(tag.ToXml(match));
        }
        return e;
    }
}

// Explicit bounded retention, including failed samples. Failure is never replaced by zero.
public sealed record TrendFrame(DateTimeOffset Timestamp,IReadOnlyDictionary<string,double?> Values);
public sealed record BatchParseDefinition(string Name,string Type,string Length,int ByteIndex);
public sealed record BatchParseResult(string Name,string Type,string Value,string Code,string Error="")
{
    public bool Success=>string.IsNullOrEmpty(Error);
}

public sealed class TrendBuffer(int capacity=4000)
{
    private readonly Dictionary<string,Queue<TagSample>> samples=new();
    public int Capacity {get;}=capacity>0?capacity:throw new ArgumentOutOfRangeException(nameof(capacity));
    public void Add(string id,TagSample sample){if(!samples.TryGetValue(id,out var q))samples[id]=q=new();q.Enqueue(sample);while(q.Count>Capacity)q.Dequeue();}
    public IReadOnlyList<TagSample> Get(string id)=>samples.TryGetValue(id,out var q)?q.ToArray():[];
    public IReadOnlyList<TrendFrame> GetFrames(IEnumerable<string> ids){
        var selected=ids.Distinct().ToArray();
        var byId=selected.ToDictionary(id=>id,Get);
        var timestamps=byId.Values.SelectMany(x=>x).Select(x=>x.Timestamp).Distinct().OrderBy(x=>x).ToArray();
        return timestamps.Select(timestamp=>{
            var values=new Dictionary<string,double?>();
            foreach(var id in selected){
                var sample=byId[id].LastOrDefault(x=>x.Timestamp==timestamp);
                values[id]=sample is not null&&sample.Success?sample.NumericValue:null;
            }
            return new TrendFrame(timestamp,values);
        }).ToArray();
    }
    public IReadOnlyList<TrendFrame> GetCompleteFrames(IEnumerable<string> ids,TimeSpan? window=null){
        var selected=ids.Distinct().ToArray();
        var frames=GetFrames(selected).Where(frame=>selected.All(id=>frame.Values[id].HasValue)).ToArray();
        if(!window.HasValue||frames.Length==0)return frames;
        var cutoff=frames[^1].Timestamp-window.Value;
        return frames.Where(frame=>frame.Timestamp>=cutoff).ToArray();
    }
    public void Clear()=>samples.Clear();
    public string ToCsv(){
        static string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var lines=new List<string>{"Timestamp,Name,Address,Type,Value,Unit,ErrorCode,Error"};
        foreach(var s in samples.Values.SelectMany(q=>q).OrderBy(s=>s.Timestamp))lines.Add(string.Join(",",new[]{s.Timestamp.ToString("O"),s.Tag.Name,s.Tag.Address,s.Tag.DataTypeCode,s.Value,s.Tag.Unit,s.ErrorCode.ToString(CultureInfo.InvariantCulture),s.Error}.Select(Quote)));
        return string.Join(Environment.NewLine,lines);
    }
}
