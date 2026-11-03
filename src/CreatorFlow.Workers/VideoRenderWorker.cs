using Microsoft.Data.SqlClient;

namespace CreatorFlow.Workers;

internal sealed class VideoRenderWorker(
    IConfiguration config,
    IVideoRenderEngine renderEngine,
    ILogger<VideoRenderWorker> logger) : BackgroundService
{
    private readonly string connectionString = config.GetConnectionString("CreatorFlow")
        ?? throw new InvalidOperationException("Missing SQL connection.");
    private readonly string root = Path.GetFullPath(config["VideoRendering:StorageRoot"]
        ?? Path.Combine(AppContext.BaseDirectory, "data", "video"));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Dịch vụ dựng video đã khởi động bằng engine {Engine}.", renderEngine.Name);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await ClaimAsync(ct);
                if (job is null) { await Task.Delay(3000, ct); continue; }
                await RenderAsync(job, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Lỗi hàng đợi dựng video."); await Task.Delay(5000, ct); }
        }
    }

    private async Task<Job?> ClaimAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.VideoRenderJobs SET Status=CASE WHEN AttemptCount<3 THEN N'Queued' ELSE N'Failed' END,
              ProgressPercent=0,ErrorMessage=N'Worker bị dừng quá thời hạn xử lý; tác vụ được đưa lại hàng đợi hoặc đánh dấu lỗi.'
            WHERE Status IN(N'Rendering',N'Encoding') AND StartedAt<DATEADD(hour,-3,SYSUTCDATETIME());
            ;WITH q AS (SELECT TOP(1) * FROM dbo.VideoRenderJobs WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE Status=N'Queued' AND AttemptCount<3 ORDER BY CreatedAt,Id)
            UPDATE q SET Status=N'Rendering',ProgressPercent=5,StartedAt=SYSUTCDATETIME(),
              AttemptCount=AttemptCount+1,ErrorMessage=NULL
            OUTPUT inserted.Id,inserted.TenantId,inserted.SourcePath,inserted.OutputPath,inserted.Title,
              inserted.Hook,inserted.CallToAction,inserted.StyleKey;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new Job(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7));
    }

    private async Task RenderAsync(Job job, CancellationToken ct)
    {
        var work = Path.Combine(root, "work", job.Id.ToString("N"));
        var output = Path.GetFullPath(job.Output);
        try
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(root, "sources", job.TenantId.ToString("N"))) + Path.DirectorySeparatorChar;
            var tenantOutputRoot = Path.GetFullPath(Path.Combine(root, "renders", job.TenantId.ToString("N"))) + Path.DirectorySeparatorChar;
            var sourcePath = Path.GetFullPath(job.Source);
            if (!sourcePath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                !output.StartsWith(tenantOutputRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Đường dẫn video không thuộc vùng lưu trữ của workspace.");

            Directory.CreateDirectory(work);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await SetStateAsync(job.Id, "Rendering", 12, null, ct);
            var result = await renderEngine.RenderAsync(new VideoRenderRequest(
                sourcePath, output, work, job.Title, job.Hook, job.Cta, job.StyleKey), ct);
            await SetStateAsync(job.Id, "Encoding", 90, null, ct);
            await CompleteAsync(job.Id, result, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dựng video thất bại {JobId} bằng {Engine}", job.Id, renderEngine.Name);
            await FailAsync(job.Id, ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); }
            catch (Exception ex) { logger.LogWarning(ex, "Không dọn được thư mục tạm."); }
        }
    }

    private async Task SetStateAsync(Guid id, string state, int progress, string? error, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.VideoRenderJobs SET Status=@s,ProgressPercent=@p,ErrorMessage=@e WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')";
        command.Parameters.AddWithValue("@s", state);
        command.Parameters.AddWithValue("@p", progress);
        command.Parameters.AddWithValue("@e", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task CompleteAsync(Guid id, VideoRenderResult result, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.VideoRenderJobs SET Status=N'Completed',ProgressPercent=100,CompletedAt=SYSUTCDATETIME(),
              Sha256=@sha,DurationSeconds=@duration,Width=@width,Height=@height
            WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')
            """;
        command.Parameters.AddWithValue("@sha", result.Sha256);
        command.Parameters.AddWithValue("@duration", result.DurationSeconds);
        command.Parameters.AddWithValue("@width", result.Width);
        command.Parameters.AddWithValue("@height", result.Height);
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Không thể xác nhận hoàn tất tác vụ.");
    }

    private async Task FailAsync(Guid id, string error)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.VideoRenderJobs SET Status=CASE WHEN AttemptCount<3 THEN N'Queued' ELSE N'Failed' END,
              ProgressPercent=CASE WHEN AttemptCount<3 THEN 0 ELSE ProgressPercent END,
              ErrorMessage=@error,CompletedAt=CASE WHEN AttemptCount<3 THEN NULL ELSE SYSUTCDATETIME() END
            WHERE Id=@id AND Status IN(N'Rendering',N'Encoding')
            """;
        command.Parameters.AddWithValue("@error", error[..Math.Min(error.Length, 1800)]);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Job(Guid Id, Guid TenantId, string Source, string Output,
        string Title, string Hook, string? Cta, string StyleKey);
}
