using Xunit;

namespace LibGPUDriverInteract.Nvidia.Tests;

public sealed class NvSettingLabelTests
{
    private static NvSettingDefinition Setting(uint id)
    {
        return NvSettings.All.Single(setting => setting.Id == id);
    }

    [Theory]
    // Shader Cache Size counts megabytes: a size another tool set reads as its size.
    [InlineData(0x00ac8497u, 0x00004000u, "16 GB")]
    [InlineData(0x00ac8497u, 0x00000400u, "1 GB")]
    [InlineData(0x00ac8497u, 0x00000200u, "512 MB")]
    [InlineData(0x00ac8497u, 0xffffffffu, "Unlimited")]
    // The Resizable BAR size limit counts bytes.
    [InlineData(0x000f00ffu, 0x50000000u, "1.3 GB")]
    [InlineData(0x000f00ffu, 0x06400000u, "100 MB")]
    // An enumeration value outside the table has no name to read.
    [InlineData(0x1057eb71u, 0x00000009u, "Custom")]
    public void AValueIsNeverLabelledWithItsRawNumber(uint setting, uint value, string expected)
    {
        var label = NvSettingControl.LabelOf(Setting(setting), value);
        Assert.Equal(expected, label);
        Assert.DoesNotContain("0x", label, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralUnnamedValuesStayDistinct()
    {
        var labels = NvSettingControl.Labelled(Setting(0x1057eb71), [0, 9, 10]).Select(pair => pair.Label);
        Assert.Equal(["Adaptive", "Custom", "Custom 2"], labels);
    }
}
