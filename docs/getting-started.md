# Getting started

## Build and reference

Use the .NET 10 SDK and an x64 Windows application targeting `net10.0-windows10.0.19041.0` or a
compatible newer target. Install the matching vendor driver. The library loads its installed native
DLLs; it does not redistribute them.

```powershell
git clone --recurse-submodules https://github.com/KillerPixelCrew/LibGPUDriverInteract.git
cd LibGPUDriverInteract
dotnet build LibGPUDriverInteract.slnx -c Release
```

Initialize the pinned WindowsDeviceControl submodule for standalone builds. Add a source reference:

```xml
<ItemGroup>
  <ProjectReference Include="../LibGPUDriverInteract/src/LibGPUDriverInteract/LibGPUDriverInteract.csproj" />
</ItemGroup>
```

## Acquire a vendor

DetectVendors reads present Windows display adapter vendors without opening sessions. It returns
vendors, not one instance per adapter. Create one GpuDriver per vendor and use its descriptor
instance IDs for adapters and outputs. Subscribe before Start so initial publications are retained.

```csharp
using LibGPUDriverInteract;

var vendors = GpuDriver.DetectVendors();
if (vendors.Count == 0)
{
    return;
}
var vendor = vendors[0]; // Select from this list in a real application.

var directory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApplication", "Graphics", vendor.ToString());
await using var driver = GpuDriver.Create(vendor, directory,
    entry => Console.WriteLine($"{entry.Scope}: {entry.Message}"));
GpuDescriptorSet current = new();
driver.DescriptorsChanged += value => Volatile.Write(ref current, value);
driver.StatusChanged += status => Console.WriteLine(status.Detail);
try
{
    var health = await driver.StartAsync();
    Console.WriteLine($"{vendor}: {health}");
    var descriptor = Volatile.Read(ref current).Descriptors.FirstOrDefault(
        item => item.SupportsWrite && item.ValueKind == GpuValueKind.Boolean);
    if (descriptor is not null)
    {
        // Replace true with an explicit user choice for this exact control.
        var result = await driver.ExecuteAsync(new GpuCommand
        {
            CapabilityId = descriptor.CapabilityId,
            InstanceId = descriptor.InstanceId,
            RequestedValue = GpuValue.Boolean(true)
        });
        Console.WriteLine($"Write: {result.Outcome}; {result.Reason?.Detail}");
    }
}
finally
{
    await driver.StopAsync();
}
```

This example writes driver settings when run. Documentation checks compile it without running it.
Actual UI controls should use the latest descriptor, its bounds/choices and an explicit user action.
Events run on the publishing thread; marshal UI work. Keep owners alive for the application's vendor
integration rather than reopening a session for each slider movement.
