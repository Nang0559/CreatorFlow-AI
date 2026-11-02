using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CreatorFlow.Workers;

internal sealed class VideoRenderWorker(IConfiguration config, ILogger<VideoRenderWorker> logger) : BackgroundService
{
    private readonly string connectionString = config.GetConnectionString("CreatorFlow") ?? throw new InvalidOperationException("Missing SQL connection.");
    private readonly string root = Path.GetFullPath(config["VideoRendering:StorageRoot"] ?? Path.Combine(AppContext.BaseDirectory,"data","video"));
    private readonly string afterFx = config["VideoRendering:AfterEffectsExe"] ?? "";
    private readonly string aerender = config["VideoRendering:AerenderExe"] ?? "";
    private readonly string ffmpeg = config["VideoRendering:FFmpegExe"] ?? "";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Dịch vụ dựng video đã khởi động.");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await ClaimAsync(ct);
                if (job is null) { await Task.Delay(3000,ct); continue; }
                await RenderAsync(job,ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex,"Lỗi hàng đợi dựng video."); await Task.Delay(5000,ct); }
        }
    }

    private async Task<Job?> ClaimAsync(CancellationToken ct)
    {
        await using var c = new SqlConnection(connectionString);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE dbo.VideoRenderJobs SET Status=CASE WHEN AttemptCount<3 THEN N'Queued' ELSE N'Failed' END,
              ProgressPercent=0,ErrorMessage=N'Worker bị dừng quá thời hạn xử lý; tác vụ được đưa lại hàng đợi hoặc đánh dấu lỗi.'
            WHERE Status IN(N'Rendering',N'Encoding') AND StartedAt<DATEADD(hour,-3,SYSUTCDATETIME());
            ;WITH q AS (SELECT TOP(1) * FROM dbo.VideoRenderJobs WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE Status=N'Queued' AND AttemptCount<3 ORDER BY CreatedAt,Id)
            UPDATE q SET Status=N'Rendering',ProgressPercent=5,StartedAt=SYSUTCDATETIME(),
              AttemptCount=AttemptCount+1,ErrorMessage=NULL
            OUTPUT inserted.Id,inserted.SourcePath,inserted.OutputPath,inserted.Title,
              inserted.Hook,inserted.CallToAction,inserted.StyleKey;
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetString(6));
    }

    private async Task RenderAsync(Job j,CancellationToken ct)
    {
        var work=Path.Combine(root,"work",j.Id.ToString("N")); Directory.CreateDirectory(work);
        var project=Path.Combine(work,"creatorflow.aep"); var jsx=Path.Combine(work,"build.jsx");
        var aeSequence=Path.Combine(work,"frame_[#####].png");
        var ffmpegSequence=Path.Combine(work,"frame_%05d.png"); var output=Path.GetFullPath(j.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            Require(afterFx); Require(aerender); Require(ffmpeg);
            if(!File.Exists(j.Source)) throw new FileNotFoundException("Không tìm thấy video nguồn.",j.Source);
            await SetStateAsync(j.Id,"Rendering",12,null,ct);
            var payload=JsonSerializer.Serialize(new { source=j.Source,project,title=j.Title,hook=j.Hook,cta=j.Cta,style=j.StyleKey });
            await File.WriteAllTextAsync(jsx,BuildJsx(payload),new System.Text.UTF8Encoding(false),ct);
            await RunAsync(afterFx,["-r",jsx],work,TimeSpan.FromMinutes(10),ct);
            if(!File.Exists(project)) throw new InvalidOperationException("After Effects không tạo được dự án dựng.");
            await SetStateAsync(j.Id,"Rendering",30,null,ct);
            await RunAsync(aerender,["-project",project,"-comp","CreatorFlow 9x16","-OMtemplate","PNG Sequence","-output",aeSequence],work,TimeSpan.FromMinutes(90),ct);
            await SetStateAsync(j.Id,"Encoding",80,null,ct);
            await RunAsync(ffmpeg,["-y","-hide_banner","-loglevel","error","-framerate","30","-i",ffmpegSequence,
              "-i",j.Source,"-map","0:v:0","-map","1:a?","-c:v","libx264","-preset","medium","-crf","20",
              "-pix_fmt","yuv420p","-c:a","aac","-b:a","192k","-shortest","-movflags","+faststart",output],
              work,TimeSpan.FromMinutes(45),ct);
            if(!File.Exists(output)||new FileInfo(output).Length<50000) throw new InvalidOperationException("MP4 đầu ra không hợp lệ.");
            var sha=await HashAsync(output,ct);
            var info=await ProbeAsync(output,ct);
            if(info.Width!=1080||info.Height!=1920)throw new InvalidOperationException("Kích thước MP4 không đúng chuẩn 1080×1920.");
            await CompleteAsync(j.Id,sha,info.Duration,info.Width,info.Height,ct);
        }
        catch(Exception ex) { logger.LogError(ex,"Dựng video thất bại {JobId}",j.Id); await FailAsync(j.Id,ex.Message); }
        finally { try { if(Directory.Exists(work)) Directory.Delete(work,true); } catch(Exception ex) { logger.LogWarning(ex,"Không dọn được thư mục tạm."); } }
    }

    private static string BuildJsx(string payload) => """
      var d=__PAYLOAD__;
      app.beginSuppressDialogs();
      var p=app.newProject();
      var footage=p.importFile(new ImportOptions(new File(d.source)));
      var w=1080,h=1920,fps=30,duration=Math.max(footage.duration,1);
      var comp=p.items.addComp("CreatorFlow 9x16",w,h,1,duration,fps);
      var video=comp.layers.add(footage);
      var scale=Math.max(w/footage.width,h/footage.height)*100;
      video.property("Scale").setValue([scale,scale]);
      video.property("Position").setValue([w/2,h/2]);
      function caption(value,y,size,color){
        if(!value)return;
        var layer=comp.layers.addText(value),doc=layer.property("Source Text").value;
        var fontSize=Math.max(38,size-Math.max(0,value.length-24)*0.6);doc.font="ArialMT";doc.fontSize=fontSize;doc.fillColor=color;
        doc.applyStroke=true;doc.strokeColor=[0.12,0.08,0.2];doc.strokeWidth=3;
        doc.justification=ParagraphJustification.CENTER_JUSTIFY;
        layer.property("Source Text").setValue(doc);
        var fit=Math.min(100,(w*0.86)/(Math.max(1,value.length)*fontSize*0.58)*100);
        layer.property("Scale").setValue([fit,100]);
        layer.property("Position").setValueAtTime(0,[w/2,y+48]);
        layer.property("Position").setValueAtTime(0.42,[w/2,y]);
        layer.property("Opacity").setValueAtTime(0,0);
        layer.property("Opacity").setValueAtTime(0.42,100);
      }
      if(d.style==="Kể chuyện"){
        caption(d.title,h*0.20,72,[0.82,0.66,1]);caption(d.hook,h*0.36,60,[1,1,1]);caption(d.cta,h*0.90,42,[0.82,0.66,1]);
      }else if(d.style==="Đánh giá sản phẩm"){
        caption(d.title,h*0.16,64,[1,0.84,0.42]);caption(d.hook,h*0.73,58,[1,1,1]);caption(d.cta,h*0.90,44,[1,0.84,0.42]);
      }else{
        caption(d.title,h*0.70,66,[1,1,1]);caption(d.hook,h*0.80,52,[1,1,1]);caption(d.cta,h*0.91,40,[0.82,0.66,1]);
      }
      p.save(new File(d.project));app.endSuppressDialogs(false);app.quit();
      """.Replace("__PAYLOAD__",payload);

    private async Task RunAsync(string exe,IEnumerable<string> args,string cwd,TimeSpan timeout,CancellationToken ct)
    {
        using var p=new Process { StartInfo=new ProcessStartInfo(exe) { WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true } };
        foreach(var a in args)p.StartInfo.ArgumentList.Add(a);
        p.Start();var stdout=p.StandardOutput.ReadToEndAsync(ct);var stderr=p.StandardError.ReadToEndAsync(ct);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);linked.CancelAfter(timeout);
        try{await p.WaitForExitAsync(linked.Token);}catch{try{p.Kill(true);}catch{}throw;}
        _=await stdout;var err=await stderr;
        if(p.ExitCode!=0)throw new InvalidOperationException($"{Path.GetFileName(exe)} mã {p.ExitCode}: {err[..Math.Min(err.Length,1200)]}");
    }

    private async Task SetStateAsync(Guid id,string state,int progress,string? error,CancellationToken ct)
    {
        await using var c=new SqlConnection(connectionString);await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="UPDATE dbo.VideoRenderJobs SET Status=@s,ProgressPercent=@p,ErrorMessage=@e WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')";
        cmd.Parameters.AddWithValue("@s",state);cmd.Parameters.AddWithValue("@p",progress);cmd.Parameters.AddWithValue("@e",(object?)error??DBNull.Value);cmd.Parameters.AddWithValue("@id",id);await cmd.ExecuteNonQueryAsync(ct);
    }
    private async Task CompleteAsync(Guid id,string sha,double duration,int width,int height,CancellationToken ct)
    {
        await using var c=new SqlConnection(connectionString);await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="UPDATE dbo.VideoRenderJobs SET Status=N'Completed',ProgressPercent=100,CompletedAt=SYSUTCDATETIME(),Sha256=@sha,DurationSeconds=@d,Width=@w,Height=@h WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')";
        cmd.Parameters.AddWithValue("@sha",sha);cmd.Parameters.AddWithValue("@d",duration);cmd.Parameters.AddWithValue("@w",width);cmd.Parameters.AddWithValue("@h",height);cmd.Parameters.AddWithValue("@id",id);
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("Không thể xác nhận hoàn tất tác vụ.");
    }
    private async Task FailAsync(Guid id,string error)
    {
        await using var c=new SqlConnection(connectionString);await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="UPDATE dbo.VideoRenderJobs SET Status=CASE WHEN AttemptCount<3 THEN N'Queued' ELSE N'Failed' END,ProgressPercent=CASE WHEN AttemptCount<3 THEN 0 ELSE ProgressPercent END,ErrorMessage=@e,CompletedAt=CASE WHEN AttemptCount<3 THEN NULL ELSE SYSUTCDATETIME() END WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')";
        cmd.Parameters.AddWithValue("@e",error[..Math.Min(error.Length,1800)]);cmd.Parameters.AddWithValue("@id",id);await cmd.ExecuteNonQueryAsync();
    }
    private async Task<(double Duration,int Width,int Height)> ProbeAsync(string path,CancellationToken ct)
    {
        var probe=config["VideoRendering:FFprobeExe"]??Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe");
        using var p=new Process{StartInfo=new ProcessStartInfo(probe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
        foreach(var a in new[]{"-v","error","-select_streams","v:0","-show_entries","stream=width,height:format=duration","-of","json",path})p.StartInfo.ArgumentList.Add(a);
        p.Start();var json=await p.StandardOutput.ReadToEndAsync(ct);await p.WaitForExitAsync(ct);
        if(p.ExitCode!=0)throw new InvalidOperationException("FFprobe không đọc được MP4.");
        using var doc=JsonDocument.Parse(json);var stream=doc.RootElement.GetProperty("streams")[0];var format=doc.RootElement.GetProperty("format");
        return(double.Parse(format.GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture),stream.GetProperty("width").GetInt32(),stream.GetProperty("height").GetInt32());
    }
    private static async Task<string> HashAsync(string path,CancellationToken ct){await using var s=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(s,ct)).ToLowerInvariant();}
    private static void Require(string path){if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new FileNotFoundException("Chưa cấu hình công cụ dựng video.",path);}
    private sealed record Job(Guid Id,string Source,string Output,string Title,string Hook,string? Cta,string StyleKey);
}
