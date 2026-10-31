using System.Collections.Concurrent;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Testing;

internal sealed class RecordingDriverSink : IDriverSink
{
    internal GpuDescriptorSet? Descriptors { get; private set; }
    internal ConcurrentQueue<GpuState> States { get; } = new();
    internal ConcurrentQueue<string> Logs { get; } = new();
    internal int DescriptorPublications { get; private set; }

    public void PublishDescriptors(GpuDescriptorSet descriptors)
    {
        Descriptors = descriptors;
        DescriptorPublications++;
    }

    public void PublishCapabilityState(GpuState state)
    {
        States.Enqueue(state);
    }

    public void ReportHealth(GpuHealth health, string detail)
    {
    }

    public void Log(GpuLogLevel level, string scope, string message)
    {
        Logs.Enqueue(message);
    }

    public void LogChange(GpuLogLevel level, string scope, string key, string message)
    {
        Logs.Enqueue(message);
    }
}

internal sealed class DriverTestSession(params DriverControl[] controls) : IDriverSession
{
    internal int Discoveries { get; private set; }
    internal int Passes { get; private set; }
    internal int Disposals { get; private set; }
    internal int Initializations { get; private set; }
    internal Action<WriteAdmission, CancellationToken>? BeforeInitialize { get; set; }
    internal Action? BeforeDispose { get; set; }
    internal Action? BeforeDiscover { get; set; }
    internal DriverModel Model { get; set; } = new([DriverDescriptors.Section("graphics", "Graphics")], controls);

    public void Initialize(WriteAdmission admission, CancellationToken token)
    {
        Initializations++;
        BeforeInitialize?.Invoke(admission, token);
    }

    public void BeginPass()
    {
        Passes++;
    }

    public DriverModel Discover()
    {
        Discoveries++;
        BeforeDiscover?.Invoke();
        return Model;
    }

    public GpuProfileSyncResult Sync(GpuProfileSync sync, WriteAdmission admission, CancellationToken token)
    {
        return new GpuProfileSyncResult(0, 0, []);
    }

    public void Dispose()
    {
        BeforeDispose?.Invoke();
        Disposals++;
    }
}

internal sealed class DriverTestControl(GpuDescriptor descriptor) : DriverControl(descriptor)
{
    internal DriverTestControl(string id = "enabled")
        : this(DriverDescriptors.Toggle(id, "driver", id, "graphics", GpuProfileScope.GlobalOnly))
    {
    }

    internal Func<GpuValue>? Reading { get; set; }
    internal Action? BeforeWrite { get; set; }
    internal Action? AfterWrite { get; set; }
    internal bool IgnoreWrites { get; set; }
    internal bool RefuseWrites { get; set; }

    /// <summary>A failure the setter reports after it has already run, as a driver result code would.</summary>
    internal string? FailAfterWrite { get; set; }

    /// <summary>How many reads after a write still answer the previous value before the new one settles.</summary>
    internal int SettleReads { get; set; }

    internal int Reads { get; private set; }
    internal int Writes { get; private set; }
    internal bool Value { get; set; }
    private int _settling;
    private bool? _settled;

    internal override GpuValue Read()
    {
        Reads++;
        if (_settling > 0)
        {
            _settling--;
        }
        else if (_settled is { } settled)
        {
            Value = settled;
            _settled = null;
        }

        return Reading?.Invoke() ?? GpuValue.Boolean(Value);
    }

    internal override void Write(GpuValue value, WriteAdmission admission)
    {
        BeforeWrite?.Invoke();
        admission.Check();
        if (RefuseWrites)
        {
            throw new DriverFailure("Fixture setter unsupported.");
        }

        Writes++;
        if (!IgnoreWrites)
        {
            if (SettleReads > 0)
            {
                _settling = SettleReads;
                _settled = value.BooleanValue!.Value;
            }
            else
            {
                Value = value.BooleanValue!.Value;
            }
        }

        AfterWrite?.Invoke();
        if (FailAfterWrite is { } failure)
        {
            throw new DriverFailure(failure, true);
        }
    }

    /// <summary>Turns the default fixture control on.</summary>
    internal static GpuCommand EnableCommand()
    {
        return new GpuCommand
        {
            CapabilityId = "enabled",
            InstanceId = "driver",
            RequestedValue = GpuValue.Boolean(value: true)
        };
    }
}
