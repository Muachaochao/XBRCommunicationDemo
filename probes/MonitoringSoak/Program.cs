using System.Diagnostics;
using System.Text.Json;
using Xbr.Communication;

var durationSeconds=args.Length>0&&int.TryParse(args[0],out var seconds)?seconds:10;
var connectionCount=args.Length>1&&int.TryParse(args[1],out var connections)?connections:10;
var tagCount=args.Length>2&&int.TryParse(args[2],out var tags)?tags:20;
var interval=args.Length>3&&int.TryParse(args[3],out var delay)?delay:500;
if(durationSeconds<=0||connectionCount<=0||tagCount<=0||interval<50)
    throw new ArgumentException("Usage: MonitoringSoak [seconds] [connections] [tags] [interval>=50]");

var process=Process.GetCurrentProcess();
var startedAt=DateTimeOffset.Now;
var fixtures=Enumerable.Range(0,connectionCount).Select(_=>new Fixture("table")).ToArray();
var latencies=new System.Collections.Concurrent.ConcurrentBag<double>();
var failures=new System.Collections.Concurrent.ConcurrentBag<string>();
var rounds=new int[connectionCount];
var tagsPerConnection=Enumerable.Range(0,tagCount)
    .Select(index=>new TagDefinition($"tag{index}",(100+index%100).ToString(),"ushort")).ToArray();

try{
    await Task.WhenAll(fixtures.Select((fixture,index)=>RunConnection(fixture,index)));
}
finally{
    foreach(var fixture in fixtures)await fixture.DisposeAsync();
}

var totalRounds=rounds.Sum();
var totalRequests=fixtures.Sum(fixture=>fixture.Frames.Count);
var ordered=latencies.OrderBy(value=>value).ToArray();
var result=new{
    passed=failures.IsEmpty&&totalRounds>0,
    startedAt,
    endedAt=DateTimeOffset.Now,
    durationSeconds,
    connectionCount,
    tagCount,
    intervalMs=interval,
    rounds=rounds,
    totalRounds,
    totalRequests,
    failures=failures.ToArray(),
    latencyMs=new{min=ordered.FirstOrDefault(),p50=Percentile(ordered,0.50),p95=Percentile(ordered,0.95),max=ordered.LastOrDefault()},
    process=new{
        privateMemoryMb=process.PrivateMemorySize64/1024d/1024d,
        workingSetMb=process.WorkingSet64/1024d/1024d,
        managedMemoryMb=GC.GetTotalMemory(false)/1024d/1024d,
        threadCount=process.Threads.Count,
        cpuMs=process.TotalProcessorTime.TotalMilliseconds
    }
};
Directory.CreateDirectory("docs/baseline");
await File.WriteAllTextAsync("docs/baseline/monitoring-soak.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Monitoring soak: {connectionCount} connections x {tagCount} tags, {totalRounds} rounds, {totalRequests} requests, failures={failures.Count}, passed={result.passed}");
return result.passed?0:1;

async Task RunConnection(Fixture fixture,int index){
    var service=new ModbusService();var stopwatch=new Stopwatch();
    try{
        await service.ConnectAsync(new(){Port=fixture.Port,ReceiveTimeout=1000});
        var deadline=DateTime.UtcNow.AddSeconds(durationSeconds);
        while(DateTime.UtcNow<deadline){
            stopwatch.Restart();
            var rows=await service.ReadTableAsync(tagsPerConnection);
            stopwatch.Stop();latencies.Add(stopwatch.Elapsed.TotalMilliseconds);rounds[index]++;
            foreach(var failure in rows.Where(row=>!row.Success))failures.Add($"connection {index} {failure.Tag.Name}: {failure.Error}");
            var remaining=interval-(int)stopwatch.ElapsedMilliseconds;
            if(remaining>0)await Task.Delay(remaining);
        }
    }
    catch(Exception ex){failures.Add($"connection {index}: {ex.Message}");}
    finally{await service.DisconnectAsync();}
}

static double Percentile(double[] values,double percentile){
    if(values.Length==0)return 0;
    var index=Math.Clamp((int)Math.Ceiling(percentile*values.Length)-1,0,values.Length-1);
    return values[index];
}
