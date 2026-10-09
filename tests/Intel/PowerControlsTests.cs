using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Display;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Intel.Tests.Fakes;
using LibGPUDriverInteract.Runtime;
using Xunit;

namespace LibGPUDriverInteract.Intel.Tests;

public sealed class PowerControlsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationAroundTheFirstPowerSetterDistinguishesRefusalFromPartialChange(bool afterWrite)
    {
        using var cancellation = new CancellationTokenSource();
        var driver = new FakeIgclDriver();
        using var session = driver.OpenSession();
        var output = new IgclOutput(3, Assert.Single(session.Adapters), 0, default, true);
        var control = Assert.Single(PowerSavingControls.Build(session, output, PowerSavingControls.FeatureLrr,
                "display-fixture", new Placement("display", "power", 0), IntelLog.None),
            control => control.CapabilityId == "display.lrr.plugged-in");
        if (afterWrite)
        {
            driver.AfterPowerWrite = cancellation.Cancel;
        }
        else
        {
            cancellation.Cancel();
        }

        var failure = Assert.Throws<DriverFailure>(() => control.Write(GpuValue.Choice("lrr-1"),
            new WriteAdmission(cancellation.Token, DriverDeadline.Never, () => true)));
        Assert.Equal(afterWrite, failure.Attempted);
        Assert.Equal(afterWrite ? new[] { PowerSavingControls.FeaturePsr } : [], driver.PowerWrites);
        session.Dispose();
        GC.KeepAlive(driver);
    }
}
