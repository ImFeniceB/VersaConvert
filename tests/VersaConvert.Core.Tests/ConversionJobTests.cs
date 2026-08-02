using VersaConvert.Core.Models;

namespace VersaConvert.Core.Tests;

public sealed class ConversionJobTests : IDisposable
{
    private readonly string _inputPath;

    public ConversionJobTests()
    {
        _inputPath = Path.Combine(Path.GetTempPath(), $"versaconvert-job-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_inputPath, "test");
    }

    [Fact]
    public void FailedJob_CanBeRetried()
    {
        var job = new ConversionJob(_inputPath)
        {
            Status = JobStatus.Failed
        };

        Assert.True(job.CanRetry);
        Assert.False(job.HasOutput);
    }

    [Fact]
    public void CompletedJob_WithOutput_ExposesOutputAction()
    {
        var job = new ConversionJob(_inputPath)
        {
            OutputPath = Path.ChangeExtension(_inputPath, ".md"),
            Status = JobStatus.Completed
        };

        Assert.True(job.HasOutput);
        Assert.False(job.CanRetry);
    }

    [Fact]
    public void StatusChange_NotifiesDerivedActionProperties()
    {
        var job = new ConversionJob(_inputPath);
        var changedProperties = new List<string?>();
        job.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        job.Status = JobStatus.Cancelled;

        Assert.Contains(nameof(ConversionJob.CanRetry), changedProperties);
        Assert.Contains(nameof(ConversionJob.HasOutput), changedProperties);
    }

    public void Dispose()
    {
        if (File.Exists(_inputPath)) File.Delete(_inputPath);
    }
}
