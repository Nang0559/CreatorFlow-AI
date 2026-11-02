using System.Data;
using System.Security.Cryptography;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Api.Video;

public static class VideoRenderEndpoints
{
    private const long MaxBytes=524288000;
    public static IEndpointRouteBuilder MapVideoRenderEndpoints(this IEndpointRouteBuilder app)
    {
        var g=app.MapGroup("/api/v1/video").RequireAuthorization();
        g.MapPost("/assets",UploadAsync).DisableAntiforgery();
        g.MapGet("/assets",ListAssetsAsync);
        g.MapPost("/render-jobs",CreateJobAsync);
        g.MapGet("/render-jobs",ListJobsAsync);
        g.MapGet("/render-jobs/{id:guid}",GetJobAsync);
        g.MapGet("/render-jobs/{id:guid}/preview",(Guid id,CreatorFlowDbContext? db,ITenantContext tenant,IConfiguration cfg,CancellationToken ct)=>OutputAsync(id,db,tenant,cfg,ct,false));
        g.MapGet("/render-jobs/{id:guid}/download",(Guid id,CreatorFlowDbContext? db,ITenantContext tenant,IConfiguration cfg,CancellationToken ct)=>OutputAsync(id,db,tenant,cfg,ct,true));
        return app;
    }

    private static async Task<IResult> UploadAsync(HttpRequest req,CreatorFlowDbContext? db,ITenantContext tenant,IConfiguration cfg,CancellationToken ct)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);
        if(!tenant.TenantId.HasValue)return Results.Forbid();
        if(!req.HasFormContentType)return Results.BadRequest(new{error="Vui lòng chọn tệp video."});
        var form=await req.ReadFormAsync(ct);
        if(!bool.TryParse(form["confirmRights"],out var rightsConfirmed)||!rightsConfirmed)return Results.BadRequest(new{error="Bạn cần xác nhận quyền sử dụng video trước khi tải lên."});
        var file=form.Files.GetFile("file");
        if(file is null||file.Length==0)return Results.BadRequest(new{error="Tệp video đang trống."});
        if(file.Length>MaxBytes)return Results.BadRequest(new{error="Video vượt quá giới hạn 500 MB."});
        var ext=Path.GetExtension(file.FileName);
        if(!new[]{".mp4",".mov",".m4v"}.Contains(ext,StringComparer.OrdinalIgnoreCase))return Results.BadRequest(new{error="Chỉ nhận video MP4, MOV hoặc M4V."});
        var root=StorageRoot(cfg);var dir=Path.Combine(root,"sources");Directory.CreateDirectory(dir);
        var id=Guid.NewGuid();var stored=id.ToString("N")+ext.ToLowerInvariant();var path=Path.Combine(dir,stored);
        try
        {
            await using var input=file.OpenReadStream(MaxBytes);
            var header=new byte[12];var n=await input.ReadAsync(header.AsMemory(),ct);
            if(n<8||System.Text.Encoding.ASCII.GetString(header,4,4)!="ftyp")return Results.BadRequest(new{error="Tệp không có cấu trúc MP4/MOV hợp lệ."});
            input.Position=0;await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true);
            await input.CopyToAsync(output,ct);await output.FlushAsync(ct);
        }
        catch { if(File.Exists(path))File.Delete(path); throw; }
        string hash;await using(var stream=File.OpenRead(path))hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
        try
        {
            await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
            await using var cmd=c.CreateCommand();
            var contentType=ext.ToLowerInvariant() switch { ".mov"=>"video/quicktime", ".m4v"=>"video/x-m4v", _=>"video/mp4" };
            cmd.CommandText="INSERT dbo.VideoAssets(Id,TenantId,OriginalFileName,StoredFileName,StoragePath,ContentType,FileSizeBytes,Sha256,RightsNote,CreatedAt) VALUES(@id,@t,@name,@stored,@path,@contentType,@size,@hash,@rights,SYSUTCDATETIME())";
            Add(cmd,"@id",id);Add(cmd,"@t",tenant.TenantId.Value);Add(cmd,"@name",Path.GetFileName(file.FileName));Add(cmd,"@stored",stored);Add(cmd,"@contentType",contentType);
            Add(cmd,"@path",path);Add(cmd,"@size",file.Length);Add(cmd,"@hash",hash);Add(cmd,"@rights",TrimTo(form["rightsNote"].ToString(),1000));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch{if(File.Exists(path))File.Delete(path);throw;}
        return Results.Created($"/api/v1/video/assets/{id}",new{id,fileName=Path.GetFileName(file.FileName),sizeBytes=file.Length,sha256=hash});
    }

    private static async Task<IResult> ListAssetsAsync(CreatorFlowDbContext? db,ITenantContext tenant,CancellationToken ct)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);
        if(!tenant.TenantId.HasValue)return Results.Forbid();
        await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT TOP(100) Id,OriginalFileName,FileSizeBytes,CreatedAt,RightsNote FROM dbo.VideoAssets WHERE TenantId=@t ORDER BY CreatedAt DESC";Add(cmd,"@t",tenant.TenantId.Value);
        await using var r=await cmd.ExecuteReaderAsync(ct);var rows=new List<object>();
        while(await r.ReadAsync(ct))rows.Add(new{id=r.GetGuid(0),fileName=r.GetString(1),sizeBytes=r.GetInt64(2),createdAt=(DateTimeOffset)r.GetValue(3),rightsNote=r.IsDBNull(4)?null:r.GetString(4)});
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateJobAsync(CreateRenderJobRequest req,CreatorFlowDbContext? db,ITenantContext tenant,IConfiguration cfg,CancellationToken ct)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);
        if(!tenant.TenantId.HasValue)return Results.Forbid();
        var allowedStyles=new[]{"Tự nhiên","Kể chuyện","Đánh giá sản phẩm"};
        if(req.AssetId==Guid.Empty||string.IsNullOrWhiteSpace(req.Title)||req.Title.Trim().Length>180||string.IsNullOrWhiteSpace(req.Hook)||req.Hook.Trim().Length>500||req.CallToAction?.Trim().Length>300||(!string.IsNullOrWhiteSpace(req.StyleKey)&&!allowedStyles.Contains(req.StyleKey,StringComparer.Ordinal)))
            return Results.ValidationProblem(new Dictionary<string,string[]>{{"video",new[]{"Vui lòng kiểm tra video nguồn, tiêu đề, câu mở đầu và phong cách dựng."}}});
        await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
        await using var find=c.CreateCommand();find.CommandText="SELECT StoragePath FROM dbo.VideoAssets WHERE Id=@id AND TenantId=@t";Add(find,"@id",req.AssetId);Add(find,"@t",tenant.TenantId.Value);
        var source=(string?)await find.ExecuteScalarAsync(ct);if(string.IsNullOrWhiteSpace(source)||!File.Exists(source))return Results.NotFound(new{error="Không tìm thấy video nguồn trong thư viện."});
        var outputDir=Path.Combine(StorageRoot(cfg),"renders",tenant.TenantId.Value.ToString("N"));Directory.CreateDirectory(outputDir);
        var id=Guid.NewGuid();var output=Path.Combine(outputDir,id.ToString("N")+".mp4");
        await using var cmd=c.CreateCommand();cmd.CommandText="INSERT dbo.VideoRenderJobs(Id,TenantId,AssetId,StyleKey,Title,Hook,CallToAction,Status,ProgressPercent,SourcePath,OutputPath,CreatedAt) VALUES(@id,@t,@a,@style,@title,@hook,@cta,N'Queued',0,@src,@out,SYSUTCDATETIME())";
        Add(cmd,"@id",id);Add(cmd,"@t",tenant.TenantId.Value);Add(cmd,"@a",req.AssetId);Add(cmd,"@style",TrimTo(req.StyleKey??"Tự nhiên",80));
        Add(cmd,"@title",req.Title.Trim());Add(cmd,"@hook",req.Hook.Trim());Add(cmd,"@cta",TrimTo(req.CallToAction,300));Add(cmd,"@src",source);Add(cmd,"@out",output);
        await cmd.ExecuteNonQueryAsync(ct);return Results.Accepted($"/api/v1/video/render-jobs/{id}",new{id,status="Queued",progressPercent=0});
    }

    private static async Task<IResult> ListJobsAsync(CreatorFlowDbContext? db,ITenantContext tenant,CancellationToken ct)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);if(!tenant.TenantId.HasValue)return Results.Forbid();
        await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT TOP(100) Id,AssetId,StyleKey,Title,Status,ProgressPercent,ErrorMessage,CreatedAt,CompletedAt,DurationSeconds,Width,Height,Sha256 FROM dbo.VideoRenderJobs WHERE TenantId=@t ORDER BY CreatedAt DESC";Add(cmd,"@t",tenant.TenantId.Value);
        await using var r=await cmd.ExecuteReaderAsync(ct);var rows=new List<object>();while(await r.ReadAsync(ct))rows.Add(JobDto(r));return Results.Ok(rows);
    }
    private static async Task<IResult> GetJobAsync(Guid id,CreatorFlowDbContext? db,ITenantContext tenant,CancellationToken ct)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);if(!tenant.TenantId.HasValue)return Results.Forbid();
        await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT Id,AssetId,StyleKey,Title,Status,ProgressPercent,ErrorMessage,CreatedAt,CompletedAt,DurationSeconds,Width,Height,Sha256 FROM dbo.VideoRenderJobs WHERE Id=@id AND TenantId=@t";Add(cmd,"@id",id);Add(cmd,"@t",tenant.TenantId.Value);
        await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?Results.Ok(JobDto(r)):Results.NotFound();
    }
    private static async Task<IResult> OutputAsync(Guid id,CreatorFlowDbContext? db,ITenantContext tenant,IConfiguration cfg,CancellationToken ct,bool download)
    {
        if(db is null)return Results.Problem("Cơ sở dữ liệu chưa được cấu hình.",statusCode:503);if(!tenant.TenantId.HasValue)return Results.Forbid();
        await using var c=db.Database.GetDbConnection();if(c.State!=ConnectionState.Open)await c.OpenAsync(ct);
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT OutputPath,Status FROM dbo.VideoRenderJobs WHERE Id=@id AND TenantId=@t";Add(cmd,"@id",id);Add(cmd,"@t",tenant.TenantId.Value);
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return Results.NotFound();
        var path=Path.GetFullPath(r.GetString(0));var status=r.GetString(1);
        var root=Path.GetFullPath(Path.Combine(StorageRoot(cfg),"renders",tenant.TenantId.Value.ToString("N")))+Path.DirectorySeparatorChar;
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))return Results.Problem("Đường dẫn video không hợp lệ.",statusCode:500);
        if(status!="Completed"||!File.Exists(path))return Results.Conflict(new{error="Video chưa dựng xong."});
        return Results.File(path,"video/mp4",download?$"{id:N}.mp4":null,enableRangeProcessing:true);
    }
    private static object JobDto(IDataRecord r)=>new{id=r.GetGuid(0),assetId=r.GetGuid(1),styleKey=r.GetString(2),title=r.GetString(3),status=r.GetString(4),progressPercent=r.GetByte(5),errorMessage=r.IsDBNull(6)?null:r.GetString(6),createdAt=(DateTimeOffset)r.GetValue(7),completedAt=r.IsDBNull(8)?(DateTimeOffset?)null:(DateTimeOffset)r.GetValue(8),durationSeconds=r.IsDBNull(9)?(decimal?)null:r.GetDecimal(9),width=r.IsDBNull(10)?(int?)null:r.GetInt32(10),height=r.IsDBNull(11)?(int?)null:r.GetInt32(11),sha256=r.IsDBNull(12)?null:r.GetString(12)};
    private static string StorageRoot(IConfiguration c)=>Path.GetFullPath(c["VideoRendering:StorageRoot"]??Path.Combine(AppContext.BaseDirectory,"data","video"));
    private static string? TrimTo(string? s,int n)=>string.IsNullOrWhiteSpace(s)?null:s.Trim()[..Math.Min(s.Trim().Length,n)];
    private static void Add(IDbCommand c,string key,object? value){var p=c.CreateParameter();p.ParameterName=key;p.Value=value??DBNull.Value;c.Parameters.Add(p);}
}
public sealed record CreateRenderJobRequest(Guid AssetId,string Title,string Hook,string? CallToAction=null,string? StyleKey=null);
