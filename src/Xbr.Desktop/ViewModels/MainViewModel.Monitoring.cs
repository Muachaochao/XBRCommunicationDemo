using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbr.Communication;
using Xbr.Desktop.Models;
using TKCode.Arc;
namespace Xbr.Desktop.ViewModels;
public partial class MainViewModel
{
    private readonly TrendBuffer trends=new(4000);
    private readonly Dictionary<string,ChartSeries> curveSeriesByTag=new();
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;
    private XElement? importedTable;
    public ObservableCollection<TagRow> Tags {get;}=new();
    public ObservableCollection<BatchParseRow> BatchParseRows {get;}=new();
    public string[] BatchDisplays {get;}=["Hex","ASCII","Byte","Int16","UInt16","Int32","UInt32","Float","Double","Int64","UInt64","Bits"];
    [ObservableProperty] private IReadOnlyList<ChartSeries> curveSeries=[];
    [ObservableProperty] private string batchAddress="100";
    [ObservableProperty] private string batchCount="10";
    [ObservableProperty] private string batchDisplay="Hex";
    [ObservableProperty] private string batchLineCount="";
    [ObservableProperty] private bool batchReverse;
    [ObservableProperty] private string batchContent="";
    [ObservableProperty] private string batchInfo="未读取";
    [ObservableProperty] private bool batchParseEnabled;
    [ObservableProperty] private BatchParseRow? selectedBatchParseRow;
    [ObservableProperty] private string batchSearch="";
    [ObservableProperty] private bool batchSearchRegex;
    [ObservableProperty] private string batchSearchInfo="";
    [ObservableProperty] private int batchSelectionStart;
    [ObservableProperty] private int batchSelectionEnd;
    [ObservableProperty] private string batchCode="";
    [ObservableProperty] private string tableInterval="1000";
    [ObservableProperty] private string tableSleep="0";
    [ObservableProperty] private string tablePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XbrCommunicationDemo","point-table.xml");
    [ObservableProperty] private string historyPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XbrCommunicationDemo","trend.csv");
    [ObservableProperty] private TagRow? selectedTag;
    [ObservableProperty] private bool isMonitoring;
    [ObservableProperty] private string tableStatus="0 个点位";
    [ObservableProperty] private IReadOnlyList<DateTime> curveCategories=[];
    [ObservableProperty] private string curveTitle="实时曲线";
    [ObservableProperty] private string curveSummary="在数据表中勾选“曲线”列；最多保留 4000 轮采样。";
    [ObservableProperty] private bool curveSmooth=true;
    [ObservableProperty] private double curveStrokeWidth=2;
    [ObservableProperty] private string curveWindowMinutes="5";
    public bool CanEditTable=>!IsMonitoring&&!IsBusy;
    partial void OnIsMonitoringChanged(bool value){Refresh();OnPropertyChanged(nameof(CanEditTable));}
    [RelayCommand] private Task ReadBatch()=>Run(async()=>{
        var start=System.Diagnostics.Stopwatch.StartNew();BatchContent=await service.ReadBatchAsync(BatchAddress,ushort.Parse(BatchCount),BatchDisplay,BatchReverse,ParseBatchLineCount());
        BatchInfo=$"{service.BatchByteCount} 字节 · {start.Elapsed.TotalMilliseconds:F0} ms";Log("批量读取完成");
        await RefreshBatchAnalysisAsync();
    });
    [RelayCommand] private Task RenderBatch()=>Run(async()=>{
        BatchContent=await service.RenderBatchAsync(BatchDisplay,BatchReverse,ParseBatchLineCount());
        await RefreshBatchAnalysisAsync();
    });
    [RelayCommand] private Task WriteBatch()=>Run(async()=>{
        Result=await service.WriteBatchAsync(BatchAddress,BatchDisplay,BatchContent);Log(Result);
        BatchCode=$"OperateResult write = modbus.Write( \"{BatchAddress}\", \"{BatchContent}\".ToHexBytes( ) );";
    });
    [RelayCommand] private void AddBatchParseRow(){
        var row=new BatchParseRow{Name=$"value{BatchParseRows.Count+1}",ByteIndex="0"};BatchParseRows.Add(row);SelectedBatchParseRow=row;
    }
    [RelayCommand] private void RemoveBatchParseRow(){if(SelectedBatchParseRow is not null)BatchParseRows.Remove(SelectedBatchParseRow);}
    [RelayCommand] private void FindBatchText(){
        if(string.IsNullOrEmpty(BatchSearch)){batchSearchIndex=0;BatchSelectionStart=BatchSelectionEnd=0;BatchSearchInfo="请输入查找内容";return;}
        try{
            int index;
            if(BatchSearchRegex){
                var matches=Regex.Matches(BatchContent,BatchSearch);
                if(matches.Count==0){batchSearchIndex=0;BatchSearchInfo="没有匹配";return;}
                if(batchSearchIndex>=matches.Count){batchSearchIndex=0;BatchSearchInfo="已到末尾，再次查找从开头开始";return;}
                var match=matches[batchSearchIndex++];index=match.Index;BatchSelectionStart=index;BatchSelectionEnd=index+match.Length;
            }
            else{
                index=BatchContent.IndexOf(BatchSearch,batchSearchIndex,StringComparison.Ordinal);
                if(index<0){batchSearchIndex=0;BatchSearchInfo="没有更多匹配";return;}
                batchSearchIndex=index+BatchSearch.Length;BatchSelectionStart=index;BatchSelectionEnd=index+BatchSearch.Length;
            }
            BatchSearchInfo=$"匹配位置 {index} · 字节偏移 {BatchByteOffset(index)}";
        }catch(Exception ex){BatchSearchInfo=ex.Message;batchSearchIndex=0;}
    }
    partial void OnBatchSearchChanged(string value){batchSearchIndex=0;BatchSearchInfo="";}
    partial void OnBatchSearchRegexChanged(bool value){batchSearchIndex=0;BatchSearchInfo="";}
    partial void OnCurveWindowMinutesChanged(string value)=>RefreshCurve();
    [RelayCommand] private void AddTag(){
        if(!CanEditTable)return;if(Tags.Count>=200){Log("点表最多200项。");return;}
        var row=new TagRow(){Name=$"点位{Tags.Count+1}",Address=Address};TrackRow(row);Tags.Add(row);SelectedTag=row;TableStatus=$"{Tags.Count} 个点位";
    }
    [RelayCommand] private void RemoveTag(){
        if(!CanEditTable||SelectedTag==null)return;
        UntrackRow(SelectedTag);Tags.Remove(SelectedTag);SelectedTag=Tags.FirstOrDefault();
        trends.Clear();RefreshCurve();TableStatus=$"{Tags.Count} 个点位；历史已清空";
    }
    private TableDefinition TableSnapshot()=>new(Tags.Select(t=>t.Definition()).ToArray(),int.Parse(TableInterval),int.Parse(TableSleep));
    private async Task SampleTableAsync(CancellationToken token){
        var rows=Tags.ToArray();var snapshot=TableSnapshot();
        var readings=await service.ReadTableAsync(snapshot.Tags,snapshot.SleepTime,token);
        for(int i=0;i<rows.Length;i++){
            var row=rows[i];var sample=readings[i];row.Value=sample.Success?sample.Value:"";row.Error=sample.Error;row.LastUpdated=sample.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            trends.Add(row.Id,sample);
        }
        TableStatus=$"{readings.Count} 项 · {readings.Count(s=>s.Success)} 成功 · {readings.Count(s=>!s.Success)} 失败";RefreshCurve();
    }
    [RelayCommand] private Task ReadTable()=>Run(async()=>await SampleTableAsync(CancellationToken.None));
    [RelayCommand] private void StartTable(){
        if(!CanOperate)return;
        try{
            var snapshot=TableSnapshot();if(snapshot.Tags.Count==0)throw new ArgumentException("请先添加点位。");
            if(snapshot.Interval<50||snapshot.SleepTime<0)throw new ArgumentException("采集周期至少50ms，点间休眠不能为负数。");
            monitorCancellation?.Dispose();monitorCancellation=new();IsMonitoring=true;monitorTask=MonitorAsync(snapshot.Interval,monitorCancellation.Token);
        }catch(Exception ex){Log(ex.Message);}
    }
    private async Task MonitorAsync(int interval,CancellationToken token){
        try{while(!token.IsCancellationRequested){await SampleTableAsync(token);await Task.Delay(interval,token);}}
        catch(OperationCanceledException){}catch(Exception ex){Log("点表采集停止："+ex.Message);}finally{IsMonitoring=false;}
    }
    [RelayCommand] private async Task StopTable()=>await StopTableAsync();
    private async Task StopTableAsync(){monitorCancellation?.Cancel();if(monitorTask!=null)await monitorTask;monitorCancellation?.Dispose();monitorCancellation=null;monitorTask=null;IsMonitoring=false;}
    private void RefreshCurve(){
        var plotted=Tags.Where(row=>row.IsPlotted).ToArray();
        var plottable=plotted.Where(IsPlottable).ToArray();
        var selectedIds=plottable.Select(row=>row.Id).ToHashSet();
        foreach(var stale in curveSeriesByTag.Keys.Where(id=>!selectedIds.Contains(id)).ToArray()){
            curveSeriesByTag.Remove(stale);
        }
        if(plottable.Length==0){
            CurveSeries=[];CurveCategories=[];CurveTitle="实时曲线";
            CurveSummary=plotted.Length==0?"在数据表中勾选“曲线”列；最多保留 4000 轮采样。":
                $"{plotted.Length} 个点位不可绘制；仅标量数值或 Bool 点位可进入曲线。";return;
        }
        var allFrames=trends.GetFrames(plotted.Select(row=>row.Id));
        // Arc maps NaN to zero. Drop any round where a selected scalar failed so the
        // chart never fabricates zero; the table and CSV retain that failed sample.
        var window=ParseCurveWindow();
        IReadOnlyList<TrendFrame> frames=trends.GetCompleteFrames(plottable.Select(row=>row.Id),window);
        CurveCategories=frames.Select(frame=>frame.Timestamp.LocalDateTime).ToArray();
        var failures=0;var plottedSamples=0;
        foreach(var row in plottable){
            var values=frames.Select(frame=>frame.Values[row.Id]!.Value).ToArray();
            if(curveSeriesByTag.TryGetValue(row.Id,out var series)){
                series.Name=CurveLabel(row);series.Values=values;
            }
            else{
                series=new ChartSeries{Key=row.Id,Name=CurveLabel(row),Values=values};
                curveSeriesByTag[row.Id]=series;
            }
            series.IsDashed=row.CurveDashed;
            series.Color=Avalonia.Media.Color.TryParse(row.CurveColor,out var color)?color:null;
            var samples=trends.Get(row.Id);failures+=samples.Count(sample=>!sample.Success);
            plottedSamples+=samples.Count(sample=>sample.Success&&sample.NumericValue.HasValue);
        }
        CurveTitle=$"实时曲线 · {plottable.Length} 个点位";
        CurveSummary=$"保留 {allFrames.Count}/4000 轮 · 时间窗 {(window.HasValue?$"{window.Value.TotalMinutes:0.#} 分钟":"全部")} · 显示 {frames.Count} 轮 · 可绘制 {plottedSamples} 次 · 失败 {failures} 次 · 不可绘制 {plotted.Length-plottable.Length} 个；失败轮次从曲线窗口剔除，表格/CSV 保留。";
        CurveSeries=curveSeriesByTag.Values.ToArray();
    }
    [RelayCommand] private void ClearHistory(){if(IsMonitoring)return;trends.Clear();RefreshCurve();}
    public async Task WriteTagValueAsync(TagRow row,string value){
        if(IsBusy)return;IsBusy=true;
        try{
            var definition=row.Definition();await service.WriteTagAsync(definition,value);
            var sample=(await service.ReadTableAsync([definition])).Single();
            row.Value=sample.Success?sample.Value:"";row.Error=sample.Error;row.LastUpdated=sample.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            trends.Add(row.Id,sample);RefreshCurve();Result=sample.Success?$"{row.Name} 写入并回读成功":sample.Error;Log(Result);
        }catch(Exception ex){Result=ex.Message;Log("表格写入失败："+ex.Message);}
        finally{IsBusy=false;}
    }
    private static void BackupWrite(string path,Action<string> write){
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if(File.Exists(full))File.Copy(full,full+"."+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".bak");write(full);
    }
    [RelayCommand] private void SaveTable(){try{var table=TableSnapshot();if(table.Interval<0||table.SleepTime<0)throw new ArgumentException("间隔不能为负数。");BackupWrite(TablePath,p=>table.Save(importedTable).Save(p));Log("点表已保存，使用原版 DataTableItem 格式");}catch(Exception ex){Log(ex.Message);}}
    [RelayCommand] private void LoadTable(){if(!CanEditTable)return;try{var e=XElement.Load(TablePath);var table=TableDefinition.Load(e);ApplyTable(table);importedTable=e;Log("点表已加载");}catch(Exception ex){Log(ex.Message);}}
    private void ApplyTable(TableDefinition table){
        foreach(var row in Tags)UntrackRow(row);
        Tags.Clear();foreach(var tag in table.Tags){var row=TagRow.From(tag);TrackRow(row);Tags.Add(row);}
        TableInterval=table.Interval.ToString();TableSleep=table.SleepTime.ToString();SelectedTag=Tags.FirstOrDefault();
        trends.Clear();RefreshCurve();TableStatus=$"{Tags.Count} 个点位";
    }
    [RelayCommand] private void ExportHistory(){try{BackupWrite(HistoryPath,p=>File.WriteAllText(p,trends.ToCsv(),new System.Text.UTF8Encoding(true)));Log("历史CSV已导出（含失败记录和时间戳）");}catch(Exception ex){Log(ex.Message);}}
    private int ParseBatchLineCount()=>string.IsNullOrWhiteSpace(BatchLineCount)?-1:
        int.TryParse(BatchLineCount,out var value)&&value>0?value:throw new ArgumentException("每行数量须为正整数或留空。");
    private void TrackRow(TagRow row)=>row.PropertyChanged+=TagRow_PropertyChanged;
    private void UntrackRow(TagRow row)=>row.PropertyChanged-=TagRow_PropertyChanged;
    private void TagRow_PropertyChanged(object? sender,PropertyChangedEventArgs e){
        if(e.PropertyName is nameof(TagRow.IsPlotted) or nameof(TagRow.Name) or nameof(TagRow.Address) or nameof(TagRow.CurveColor) or nameof(TagRow.CurveDashed))RefreshCurve();
    }
    private string CurveLabel(TagRow row){
        var name=string.IsNullOrWhiteSpace(row.Name)?row.Address:row.Name;
        return Tags.Count(other=>other.Id!=row.Id&&string.Equals(string.IsNullOrWhiteSpace(other.Name)?other.Address:other.Name,name,StringComparison.Ordinal))==0
            ?name:$"{name} [{row.Id[..4]}]";
    }
    private static bool IsPlottable(TagRow row){
        if(!string.IsNullOrWhiteSpace(row.Length)&&int.TryParse(row.Length,out var length)&&length>0)return false;
        return row.DataTypeCode is "bool" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double";
    }
    private TimeSpan? ParseCurveWindow(){
        if(string.IsNullOrWhiteSpace(CurveWindowMinutes)||CurveWindowMinutes=="0")return null;
        if(!double.TryParse(CurveWindowMinutes,out var minutes)||minutes<0)return null;
        return TimeSpan.FromMinutes(minutes);
    }
    private async Task RefreshBatchAnalysisAsync(){
        BatchCode=$"OperateResult<byte[]> read = modbus.Read( \"{BatchAddress}\", {BatchCount} );";
        if(!BatchParseEnabled||BatchParseRows.Count==0)return;
        var valid=new List<(BatchParseRow Row,BatchParseDefinition Definition)>();
        foreach(var row in BatchParseRows){
            try{valid.Add((row,row.Definition()));row.Error="";}
            catch(Exception ex){row.Value="";row.Error=ex.Message;}
        }
        if(valid.Count==0)return;
        var results=await service.ParseBatchAsync(valid.Select(item=>item.Definition).ToArray());
        var codeLines=new List<string>{BatchCode};
        for(var index=0;index<valid.Count;index++){
            var row=valid[index].Row;var parsed=results[index];row.Value=parsed.Value;row.Error=parsed.Error;
            if(parsed.Success)codeLines.Add(parsed.Code);
        }
        BatchCode=string.Join(Environment.NewLine,codeLines);
    }
    private int BatchByteOffset(int textIndex){
        if(BatchDisplay!="Hex")return textIndex;
        try{return Convert.FromHexString(BatchContent[..textIndex].Replace(" ","")).Length;}catch{return textIndex/3;}
    }
    private int batchSearchIndex;
}
