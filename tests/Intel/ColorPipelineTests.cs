using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using LibGPUDriverInteract.Testing;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

public sealed class ColorPipelineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void AnUntouchedPipeReadsAsNeutralWithoutAWrite(int blocks)
    {
        var driver = new FakeIgclDriver { ColorBlockCount = (uint)blocks };
        using var session = driver.OpenSession();
        var output = new IgclOutput(3, Assert.Single(session.Adapters), 0, default, true);
        var pipeline = ColorPipeline.TryCreate(session, output, "display-fixture",
            ColorStore.Load(null, DriverLog.None),
            DriverLog.None);
        Assert.NotNull(pipeline);
        Assert.Equal(GpuValue.Integer(ColorSettings.Neutral.Brightness),
            pipeline.Read(ColorField.Brightness).Value);
        Assert.Equal(0, driver.ColorWrites);
        session.Dispose();
        GC.KeepAlive(driver);
    }

    [Theory]
    [InlineData(0x00000000, nameof(CurrentQuery.Held))]
    [InlineData(0x40000014, nameof(CurrentQuery.NeverSet))]
    [InlineData(0x40000017, nameof(CurrentQuery.NeverSet))]
    [InlineData(0x4800000b, nameof(CurrentQuery.NotReported))]
    [InlineData(0x40000003, nameof(CurrentQuery.Failed))]
    [InlineData(0x4000000b, nameof(CurrentQuery.Failed))]
    public void CurrentQueryResultsMapToWhatTheDriverMeans(int result, string expected)
    {
        Assert.Equal(expected, ColorPipeline.ClassifyCurrent(result).ToString());
    }

    [Theory]
    [InlineData(0x40000017, "0x40000017 (CTL_RESULT_ERROR_KMD_CALL)")]
    [InlineData(0x4800000b, "0x4800000b (CTL_RESULT_ERROR_INVALID_PIXTX_BLOCK_TYPE)")]
    [InlineData(0x4000000b, "0x4000000b (CTL_RESULT_ERROR_INVALID_ARGUMENT)")]
    [InlineData(0x40000099, "0x40000099")]
    public void ResultCodesAreNamedFromTheHeader(int result, string expected)
    {
        Assert.Equal(expected, IgclResult.Describe(result));
    }

    [Theory]
    [InlineData(0x40000014)]
    [InlineData(0x40000017)]
    public void ANeverSetPipeReadsEveryFieldAsNeutral(int unset)
    {
        var (driver, session, pipeline) = Open(unset);
        foreach (var field in ColorField.All)
        {
            var read = pipeline.Read(field);
            Assert.Null(read.Failure);
            Assert.Equal(GpuValue.Integer(field.Get(ColorSettings.Neutral)), read.Value);
        }

        Assert.Equal(0, driver.ColorWrites);
        session.Dispose();
    }

    [Fact]
    public void AWriteSetsTheCurveAndMatrixTogetherSoBothReadBack()
    {
        var (driver, session, pipeline) = Open(IgclResult.KmdCall);
        var write = pipeline.Write(ColorField.Hue, 20, Admission);
        Assert.Equal(WriteStatus.Applied, write.Status);
        Assert.Equal([1, 3], driver.LastColorSetTypes);
        session.BeginPass();
        Assert.Equal(GpuValue.Integer(20), pipeline.Read(ColorField.Hue).Value);
        Assert.Equal(GpuValue.Integer(ColorSettings.Neutral.Brightness), pipeline.Read(ColorField.Brightness).Value);

        Assert.Equal(WriteStatus.Applied, pipeline.Write(ColorField.Brightness, 10, Admission).Status);
        session.BeginPass();
        Assert.Equal(GpuValue.Integer(10), pipeline.Read(ColorField.Brightness).Value);
        Assert.Equal(GpuValue.Integer(20), pipeline.Read(ColorField.Hue).Value);
        session.Dispose();
    }

    [Fact]
    public void AnotherWritersConfigurationReadsAsUnknownNotAsADriverFailure()
    {
        var (driver, session, pipeline) = Open(IgclResult.KmdCall);
        driver.ColorSetElsewhere = true;
        foreach (var field in ColorField.All)
        {
            var read = pipeline.Read(field);
            Assert.Null(read.Value);
            Assert.StartsWith("Another writer set the display's colour last", read.Failure);
        }

        session.Dispose();
    }

    private static WriteAdmission Admission => new(CancellationToken.None, static () => true);

    private static (FakeIgclDriver Driver, IgclSession Session, ColorPipeline Pipeline) Open(int unset)
    {
        var driver = new FakeIgclDriver { ColorBlockCount = 4, ColorMatrix = true, ColorUnsetResult = unset };
        var session = driver.OpenSession();
        var output = new IgclOutput(3, Assert.Single(session.Adapters), 0, default, true);
        var pipeline = ColorPipeline.TryCreate(session, output, "display-fixture",
            ColorStore.Load(null, DriverLog.None), DriverLog.None);
        Assert.NotNull(pipeline);
        Assert.True(pipeline.HasMatrix);
        return (driver, session, pipeline);
    }

    [Fact]
    public void UnreadableColorRecordIsPreservedWhileTheWrittenValueRemainsAvailable()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Root, "color.v1.json");
        File.WriteAllText(path, "corrupt color record");
        var store = ColorStore.Load(directory.Root, DriverLog.None);
        var written = ColorSettings.Neutral with { Brightness = 5 };
        store.Set("display-fixture", written);
        Assert.Equal(written, store.Get("display-fixture"));
        Assert.Equal("corrupt color record", File.ReadAllText(path));
    }

    [Fact]
    public void FailedColorSaveDoesNotUndoTheWrittenValue()
    {
        using var directory = new TemporaryDirectory();
        var store = ColorStore.Load(directory.Root, DriverLog.None);
        Directory.CreateDirectory(Path.Combine(directory.Root, "color.v1.json"));
        var written = ColorSettings.Neutral with { Brightness = 5 };
        store.Set("display-fixture", written);
        Assert.Equal(written, store.Get("display-fixture"));
    }
}
