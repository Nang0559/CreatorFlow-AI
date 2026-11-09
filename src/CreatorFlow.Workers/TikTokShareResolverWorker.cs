using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace CreatorFlow.Workers;

/// <summary>
/// Resolves only creator-submitted public TikTok pages. It does not authenticate,
/// bypass access controls, or follow redirects outside explicitly allowed hosts.
/// </summary>
internal sealed class TikTokShareResolverWorker(
    IConfiguration config,
    ILogger<TikTokShareResolverWorker> logger) : BackgroundService
{
    private const long MaxBytes = 500L * 1024 * 1024;
    private readonly string connectionString = config.GetConnectionString("CreatorFlow")
        ?? throw new InvalidOperationException("Missing SQL connection.");
    private readonly string root = Path.GetFullPath(config["VideoRendering:StorageRoot"]
        ?? Path.Combine(AppContext.BaseDirectory, "data", "video"));
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(3) };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("TikTok public-share resolver started.");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var item = await ClaimAsync(ct);
                if (item is null) { await Task.Delay(TimeSpan.FromSeconds(8), ct); continue; }
                await ResolveAsync(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "TikTok share resolver loop failed."); await Task.Delay(5000, ct); }
        }
    }

    private async Task<ShareItem?> ClaimAsync(CancellationToken ct)
    {
        await using var c = new SqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
          UPDATE dbo.SharedLinkInboxItems
             SET ResolverStatus=CASE WHEN ResolverAttemptCount>=2 THEN N'Failed' ELSE N'Pending' END,
                 ResolverError=CASE WHEN ResolverAttemptCount>=2 THEN N'Worker lease expired after the final attempt.' ELSE ResolverError END,
                 ResolverUpdatedAt=SYSUTCDATETIME()
           WHERE ResolverStatus=N'Resolving'
             AND ResolverUpdatedAt < DATEADD(minute,-10,SYSUTCDATETIME());

          ;WITH q AS (
            SELECT TOP(1) * FROM dbo.SharedLinkInboxItems WITH(UPDLOCK,READPAST,ROWLOCK)
            WHERE Platform=N'TikTok' AND ResolverStatus=N'Pending' AND ResolverAttemptCount<2
            ORDER BY SharedAt,Id)
          UPDATE q SET ResolverStatus=N'Resolving',ResolverAttemptCount=ResolverAttemptCount+1,
            ResolverUpdatedAt=SYSUTCDATETIME(),ResolverError=NULL
          OUTPUT inserted.Id,inserted.TenantId,inserted.SharedUrl;
          """;
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new ShareItem(r.GetGuid(0),r.GetGuid(1),r.GetString(2)) : null;
    }

    private async Task ResolveAsync(ShareItem item,CancellationToken ct)
    {
        string? temp=null;
        string? finalPath=null;
        var assetPersisted=false;
        try
        {
            var pageUri=await GetPublicPageAsync(new Uri(item.Url),ct);
            var html=await GetTextAsync(pageUri,ct);
            var title=ExtractMeta(html,"og:title") ?? "Video TikTok được chia sẻ";
            var tenantRoot=Path.Combine(root,"sources",item.TenantId.ToString("N"));
            Directory.CreateDirectory(tenantRoot);
            var assetId=Guid.NewGuid(); var fileName=assetId.ToString("N")+".mp4";
            finalPath=Path.Combine(tenantRoot,fileName);
            temp=finalPath+".work";
            Directory.CreateDirectory(temp);
            var downloaded=await TikTokYtDlpDownloader.DownloadAsync(pageUri.ToString(),title,temp,config,ct);
            title=downloaded.Title;
            File.Move(downloaded.FilePath,finalPath,true);
            Directory.Delete(temp,recursive:true); temp=null;
            string hash; await using(var hashStream=File.OpenRead(finalPath)){hash=Convert.ToHexString(await SHA256.HashDataAsync(hashStream,ct));}
            var saved=await SaveAssetAndJobsAsync(item,assetId,title,finalPath,fileName,new FileInfo(finalPath).Length,hash,ct);
            assetId=saved.AssetId;
            assetPersisted=true;
            if(saved.Reused && finalPath is not null)
            {
                try{File.Delete(finalPath);}catch{}
                finalPath=null;
            }
            logger.LogInformation("Resolved public TikTok share {ShareId} into asset {AssetId}; jobs await rights review.",item.Id,assetId);
        }
        catch(Exception ex)
        {
            logger.LogWarning(ex,"Could not resolve public TikTok share {ShareId}",item.Id);
            await SetStateAsync(item.TenantId,item.Id,"Failed",ex.Message,null,null,ct);
        }
        finally
        {
            if(temp is not null && Directory.Exists(temp)) try{Directory.Delete(temp,recursive:true);}catch{}
            if(!assetPersisted && finalPath is not null && File.Exists(finalPath)) try{File.Delete(finalPath);}catch{}
        }
    }

    private async Task<Uri> GetPublicPageAsync(Uri uri,CancellationToken ct)
    {
        for(var i=0;i<6;i++)
        {
            if(!AllowedPageHost(uri)) throw new InvalidOperationException("Chuyển hướng ra ngoài miền TikTok được hỗ trợ; đã chặn.");
            using var response=await SendSafeAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
            if((int)response.StatusCode is >=300 and <400)
            {
                if(response.Headers.Location is null) throw new InvalidOperationException("Redirect không có Location.");
                uri=response.Headers.Location.IsAbsoluteUri?response.Headers.Location:new Uri(uri,response.Headers.Location);
                continue;
            }
            response.EnsureSuccessStatusCode(); return uri;
        }
        throw new InvalidOperationException("Quá nhiều lần chuyển hướng.");
    }

    private async Task<string> GetTextAsync(Uri uri,CancellationToken ct)
    {
        const int maxPageBytes=5_000_000;
        using var response=await SendSafeAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is > maxPageBytes) throw new InvalidOperationException("Trang nguồn vượt giới hạn đọc 5 MB.");
        var contentType=response.Content.Headers.ContentType?.MediaType;
        if(contentType is not null && contentType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
            throw new InvalidOperationException("Nguồn TikTok không trả về trang HTML công khai.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);
        using var buffer=new MemoryStream();
        var chunk=new byte[81920];long total=0;int read;
        while((read=await input.ReadAsync(chunk,ct))>0)
        {
            total+=read;
            if(total>maxPageBytes) throw new InvalidOperationException("Trang nguồn vượt giới hạn đọc 5 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0,read),ct);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task<HttpResponseMessage> SendSafeAsync(Uri uri,HttpCompletionOption option,CancellationToken ct)
    {
        if(uri.Scheme!="https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !(AllowedPageHost(uri)||AllowedMediaHost(uri)))
            throw new InvalidOperationException("URL nguồn không thuộc host HTTPS được cho phép.");
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CreatorFlowPublicResolver/1.0)");
        return await http.SendAsync(request,option,ct);
    }

    private static bool AllowedPageHost(Uri u) => u.Scheme=="https" && (HostIs(u.Host,"tiktok.com")||HostIs(u.Host,"tiktokv.com"));
    private static bool AllowedMediaHost(Uri u) => u.Scheme=="https" &&
        (HostIs(u.Host,"tiktokcdn.com")||HostIs(u.Host,"tiktokcdn-us.com")||
         HostIs(u.Host,"tiktokcdn-eu.com")||HostIs(u.Host,"tiktokv.com")||
         HostIs(u.Host,"byteoversea.com")||HostIs(u.Host,"ibytedtos.com")||HostIs(u.Host,"muscdn.com"));
    private static bool HostIs(string host,string suffix) => host.Equals(suffix,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+suffix,StringComparison.OrdinalIgnoreCase);
    private static string? ExtractMeta(string html,string key)
    {
        var escaped=Regex.Escape(key);
        var patterns=new[]{ $@"<meta[^>]+(?:property|name)=[""']{escaped}[""'][^>]+content=[""']([^""']+)[""']", $@"<meta[^>]+content=[""']([^""']+)[""'][^>]+(?:property|name)=[""']{escaped}[""']" };
        foreach(var p in patterns){var m=Regex.Match(html,p,RegexOptions.IgnoreCase|RegexOptions.Singleline);if(m.Success)return m.Groups[1].Value;}
        return null;
    }

    private async Task<(Guid AssetId,string StoragePath,bool Reused)> SaveAssetAndJobsAsync(ShareItem item,Guid assetId,string title,string path,string name,long size,string hash,CancellationToken ct)
    {
        await using var c=new SqlConnection(connectionString);await c.OpenAsync(ct);await using SqlTransaction tx=(SqlTransaction)await c.BeginTransactionAsync(ct);
        var effectiveAssetId=assetId;
        var effectivePath=path;
        var reused=false;
        await using (var find=c.CreateCommand())
        {
            find.Transaction=tx;
            find.CommandText="SELECT TOP(1) Id,StoragePath FROM dbo.VideoAssets WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND Sha256=@hash";
            Add(find,"@tenant",item.TenantId);Add(find,"@hash",hash);
            await using var reader=await find.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                effectiveAssetId=reader.GetGuid(0);
                effectivePath=reader.GetString(1);
                reused=true;
            }
        }
        if(reused && !File.Exists(effectivePath))
            throw new InvalidOperationException("Tệp media trùng SHA-256 đã có bản ghi nhưng tệp lưu trữ không còn tồn tại; cần kiểm tra kho video.");

        if(!reused)
        {
            await using var asset=c.CreateCommand();
            asset.Transaction=tx;asset.CommandText="INSERT dbo.VideoAssets(Id,TenantId,OriginalFileName,StoredFileName,StoragePath,ContentType,FileSizeBytes,Sha256,RightsNote,CreatedAt) VALUES(@id,@tenant,@name,@stored,@path,N'video/mp4',@size,@hash,N'Nguồn TikTok công khai; quyền tái sử dụng CHƯA được xác minh.',SYSUTCDATETIME())";
            Add(asset,"@id",effectiveAssetId);Add(asset,"@tenant",item.TenantId);Add(asset,"@name",title.Length>260?title[..260]:title);Add(asset,"@stored",name);Add(asset,"@path",effectivePath);Add(asset,"@size",size);Add(asset,"@hash",hash);
            await asset.ExecuteNonQueryAsync(ct);
        }
        var styles=new[]{"Tự nhiên","Kể chuyện","Đánh giá sản phẩm"};
        foreach(var style in styles)
        {
            var id=Guid.NewGuid();var output=Path.Combine(root,"renders",item.TenantId.ToString("N"),id.ToString("N")+".mp4");
            await using var job=c.CreateCommand();job.Transaction=tx;
            job.CommandText="INSERT dbo.VideoRenderJobs(Id,TenantId,AssetId,SharedLinkInboxItemId,StyleKey,Title,Hook,CallToAction,Status,ProgressPercent,SourcePath,OutputPath,CreatedAt) VALUES(@id,@tenant,@asset,@share,@style,@title,@hook,NULL,N'AwaitingRightsReview',0,@source,@output,SYSUTCDATETIME())";
            Add(job,"@id",id);Add(job,"@tenant",item.TenantId);Add(job,"@asset",effectiveAssetId);Add(job,"@share",item.Id);Add(job,"@style",style);
            Add(job,"@title",title.Length>180?title[..180]:title);Add(job,"@hook","Video nguồn được chia sẻ; vui lòng biên tập lại nội dung sau khi xác nhận quyền.");Add(job,"@source",effectivePath);Add(job,"@output",output);
            await job.ExecuteNonQueryAsync(ct);
        }
        await using (var inbox=c.CreateCommand())
        {
            inbox.Transaction=tx;
            inbox.CommandText="UPDATE dbo.SharedLinkInboxItems SET ResolverStatus=N'NeedsReview',ResolverError=NULL,ResolvedAssetId=@asset,ResolvedTitle=@title,ResolverUpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND TenantId=@tenant AND ResolverStatus=N'Resolving'";
            Add(inbox,"@asset",effectiveAssetId);Add(inbox,"@title",title.Length>180?title[..180]:title);Add(inbox,"@id",item.Id);Add(inbox,"@tenant",item.TenantId);
            if(await inbox.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Share Inbox item is no longer in the expected Resolving state.");
        }
        await tx.CommitAsync(ct);
        return (effectiveAssetId,effectivePath,reused);
    }

    private async Task SetStateAsync(Guid tenantId,Guid id,string status,string? error,Guid? asset,string? title,CancellationToken ct)
    {
        await using var c=new SqlConnection(connectionString);await c.OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="UPDATE dbo.SharedLinkInboxItems SET ResolverStatus=@status,ResolverError=@error,ResolvedAssetId=COALESCE(@asset,ResolvedAssetId),ResolvedTitle=COALESCE(@title,ResolvedTitle),ResolverUpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND TenantId=@tenant";
        Add(cmd,"@status",status);Add(cmd,"@error",error is null?DBNull.Value:error.Length>950?error[..950]:error);Add(cmd,"@asset",asset is null?DBNull.Value:asset.Value);Add(cmd,"@title",title is null?DBNull.Value:title);Add(cmd,"@id",id);Add(cmd,"@tenant",tenantId);await cmd.ExecuteNonQueryAsync(ct);
    }
    private static void Add(SqlCommand c,string name,object value){c.Parameters.AddWithValue(name,value);}
    private sealed record ShareItem(Guid Id,Guid TenantId,string Url);
}
