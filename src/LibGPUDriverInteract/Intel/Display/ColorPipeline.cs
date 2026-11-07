using System.Runtime.InteropServices;
using LibGPUDriverInteract.Intel.Controls;
using LibGPUDriverInteract.Intel.Igcl;
using LibGPUDriverInteract.Runtime;

namespace LibGPUDriverInteract.Intel.Display;

/// <summary>The colour values the consumer last wrote per display, kept so a restart can recognise them.</summary>
/// <remarks>
///     The driver hands back the LUT and matrix it holds, not the settings behind them, and no inverse
///     is exact. So the plugin records what it wrote and, at start, accepts that record only when the
///     driver's current LUT and matrix match what the record produces. Anything else is unknown. A record
///     that cannot be read is never overwritten; colour then reads as unknown and writes still apply.
/// </remarks>
internal sealed class ColorStore
{
    private const string Scope = "color";
    private const string What = "colour record";
    private readonly IDriverSink _log;
    private readonly string? _path;
    private readonly Dictionary<string, ColorSettings> _settings;

    private ColorStore(string? path, Dictionary<string, ColorSettings> settings, IDriverSink log)
    {
        _path = path;
        _settings = settings;
        _log = log;
    }

    public static ColorStore Load(string? directory, IDriverSink log)
    {
        var path = directory is null ? null : Path.Combine(directory, "color.v1.json");
        var settings = new Dictionary<string, ColorSettings>(StringComparer.Ordinal);
        if (path is null)
        {
            return new ColorStore(path, settings, log);
        }

        try
        {
            foreach (var (display, recorded) in DriverStateFile.Read(path, new Dictionary<string, ColorSettings>()))
            {
                settings[display] = recorded;
            }
        }
        catch (DriverFailure failure)
        {
            log.Warn(Scope, $"{failure.Message} The {What} is left as it is and not written this session.");
            path = null;
        }

        return new ColorStore(path, settings, log);
    }

    public ColorSettings? Get(string display)
    {
        return _settings.TryGetValue(display, out var settings) ? settings : null;
    }

    /// <summary>Records what a write applied; a failed save is logged and costs only recognition later.</summary>
    /// <param name="display">Stable output instance identity used by the persisted color record.</param>
    /// <param name="settings">Values successfully written to that display; retained in memory even if persistence fails.</param>
    public void Set(string display, ColorSettings settings)
    {
        _settings[display] = settings;
        if (_path is null)
        {
            return;
        }

        try
        {
            DriverStateFile.Write(_path, _settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Error(Scope, $"The {What} could not be saved: {DriverLog.Describe(error)}");
        }
    }
}

/// <summary>One colour value a control publishes, and where it sits in <see cref="ColorSettings" />.</summary>
internal sealed class ColorField
{
    public static readonly ColorField Brightness = new("display.color-brightness", "Brightness",
        ColorSettings.BrightnessMinimum, ColorSettings.BrightnessMaximum, GpuUnit.None, false,
        static settings => settings.Brightness, static (settings, value) => settings with { Brightness = value });

    public static readonly ColorField Contrast = new("display.color-contrast", "Contrast",
        ColorSettings.ContrastMinimum, ColorSettings.ContrastMaximum, GpuUnit.Percent, false,
        static settings => settings.Contrast, static (settings, value) => settings with { Contrast = value });

    public static readonly ColorField Gamma = new("display.color-gamma", "Gamma",
        ColorSettings.GammaMinimum, ColorSettings.GammaMaximum, GpuUnit.None, false,
        static settings => settings.Gamma, static (settings, value) => settings with { Gamma = value });

    public static readonly ColorField Hue = new("display.color-hue", "Hue",
        ColorSettings.HueMinimum, ColorSettings.HueMaximum, GpuUnit.None, true,
        static settings => settings.Hue, static (settings, value) => settings with { Hue = value });

    public static readonly ColorField Saturation = new("display.color-saturation", "Saturation",
        ColorSettings.SaturationMinimum, ColorSettings.SaturationMaximum, GpuUnit.Percent, true,
        static settings => settings.Saturation, static (settings, value) => settings with { Saturation = value });

    private ColorField(
        string id,
        string label,
        int minimum,
        int maximum,
        GpuUnit unit,
        bool inMatrix,
        Func<ColorSettings, int> get,
        Func<ColorSettings, int, ColorSettings> set)
    {
        Id = id;
        Label = label;
        Range = IntegerRange.Linear(minimum, maximum);
        Unit = unit;
        InMatrix = inMatrix;
        Get = get;
        Set = set;
    }

    /// <summary>Every field, in row order; the matrix fields last.</summary>
    public static IReadOnlyList<ColorField> All { get; } = [Brightness, Contrast, Gamma, Hue, Saturation];

    public string Id { get; }

    public string Label { get; }

    public IntegerRange Range { get; }

    public GpuUnit Unit { get; }

    /// <summary>Whether the value lives in the CSC matrix (hue, saturation) rather than the 1D LUT.</summary>
    public bool InMatrix { get; }

    public Func<ColorSettings, int> Get { get; }

    public Func<ColorSettings, int, ColorSettings> Set { get; }
}

/// <summary>
///     One display's colour pipe: the last 1D LUT for brightness, contrast and gamma, and the first CSC
///     matrix for hue and saturation, driven through <c>ctlPixelTransformation*</c>.
/// </summary>
/// <remarks>
///     Not offered on an HDR output. In HDR the driver reports only a non-uniformly sampled gamma LUT
///     and its 3D LUT is unsupported, so the SDR algorithms here would be wrong. Every write carries
///     <c>CTL_PIXTX_PIPE_SET_CONFIG_FLAG_PERSIST_ACROSS_POWER_EVENTS</c> so the result survives sleep.
///     <para>
///         <c>CTL_PIXTX_CONFIG_QUERY_TYPE_CURRENT</c> returns only "the configuration set through last set
///         call", and only for a LUT. Measured on 2026-11-07 on an Intel laptop panel with five blocks (3D LUT,
///         degamma LUT, CSC with offsets, gamma LUT, CSC) and seen in an Arc B370 log: before any set the gamma
///         LUT answers <c>CTL_RESULT_ERROR_KMD_CALL</c>; after a set it returns the samples written; both CSC
///         blocks answer <c>CTL_RESULT_ERROR_INVALID_PIXTX_BLOCK_TYPE</c> even right after a set that carried
///         them. The LUT also answers that code while another writer's configuration is current, as during
///         display power-off. So the curve is read, and the matrix is known only through it: every write sets
///         the curve and the matrix in one call, as Intel's <c>ApplyLinearCSC</c> sample sets several blocks,
///         and a curve that matches the record therefore carries the record's matrix.
///     </para>
///     <para>
///         The pipe is read once per pass and serves all five rows. The sample buffer, the neutral curve
///         and the curve of the recorded settings are built once and reused, so a pass allocates nothing.
///     </para>
/// </remarks>
internal sealed unsafe class ColorPipeline
{
    private const int BlockType1dLut = 1;
    private const int BlockType3x3Matrix = 3;
    private const int QueryCapability = 0;
    private const int QueryCurrent = 1;
    private const int OperationSetCustom = 2;
    private const uint FlagPersist = 1;
    private const int SamplingUniform = 0;
    private const int EncodingSt2084 = 2;
    private const int EncodingHlg = 3;
    private const uint FeatureHdr = 1 << 5;
    private const int MatrixSize = 9;

    private readonly string _curveKey;
    private readonly string _display;
    private readonly IDriverSink _log;
    private readonly CtlPixTxBlockConfig _lutBlock;
    private readonly CtlPixTxBlockConfig? _matrixBlock;

    /// <summary>Whether the matrix precedes the curve in the pipe, the order a set call lists them in.</summary>
    private readonly bool _matrixFirst;

    private readonly double[] _neutralCurve;
    private readonly IgclOutput _output;
    private readonly double[] _recordedCurve;
    private readonly double[] _samples;
    private readonly IgclSession _session;
    private readonly ColorStore _store;
    private ColorSettings? _curve;
    private ColorSettings? _matrix;
    private string _observation = string.Empty;
    private long _observedPass;
    private ColorSettings? _recorded;
    private int _result;

    private ColorPipeline(
        IgclSession session,
        IgclOutput output,
        string display,
        CtlPixTxBlockConfig lutBlock,
        CtlPixTxBlockConfig? matrixBlock,
        bool matrixFirst,
        ColorStore store,
        IDriverSink log)
    {
        _session = session;
        _output = output;
        _display = display;
        _store = store;
        _log = log;
        _matrixFirst = matrixFirst;
        _curveKey = $"{display}.curve";
        var perChannel = (int)lutBlock.Config.OneDLut.SamplesPerChannel;

        // Pinned for the pipeline's lifetime, so every query and write hands the driver the same buffer.
        _samples = GC.AllocateArray<double>(perChannel * (int)lutBlock.Config.OneDLut.Channels, pinned: true);
        lutBlock.Size = (uint)sizeof(CtlPixTxBlockConfig);
        lutBlock.Config.OneDLut.SampleValues = Marshal.UnsafeAddrOfPinnedArrayElement(_samples, 0);
        lutBlock.Config.OneDLut.SamplePositions = 0;
        _lutBlock = lutBlock;
        if (matrixBlock is { } matrixConfig)
        {
            matrixConfig.Size = (uint)sizeof(CtlPixTxBlockConfig);
            _matrixBlock = matrixConfig;
        }

        _neutralCurve = new double[perChannel];
        _recordedCurve = new double[perChannel];
        ColorMath.FillCurve(ColorSettings.Neutral, _neutralCurve);
    }

    /// <summary>Whether hue and saturation can be offered.</summary>
    public bool HasMatrix => _matrixBlock is not null;

    /// <summary>Queries the pipe and builds the pipeline when an SDR 1D LUT is available.</summary>
    /// <param name="session">The session.</param>
    /// <param name="output">The output.</param>
    /// <param name="display">The display's instance id, the key of the colour record.</param>
    /// <param name="store">The colour record.</param>
    /// <param name="log">Receives the decisions.</param>
    /// <returns>The pipeline, or null when colour cannot be offered.</returns>
    public static ColorPipeline? TryCreate(
        IgclSession session,
        IgclOutput output,
        string display,
        ColorStore store,
        IDriverSink log)
    {
        var api = session.Api;
        if (api.PixTxGetConfig is null || api.PixTxSetConfig is null)
        {
            return null;
        }

        if ((output.Properties.FeatureEnabledFlags & FeatureHdr) != 0)
        {
            log.Info("color", $"{display} is in HDR; colour controls are not offered.");
            return null;
        }

        CtlPixTxPipeGetConfig caps = default;
        caps.QueryType = QueryCapability;
        var result = session.Call(api.PixTxGetConfig, output.Handle, ref caps);
        if (result != IgclResult.Success || caps.NumBlocks == 0)
        {
            log.Info("color", $"{display} reports no colour pipe ({IgclResult.Describe(result)}).");
            return null;
        }

        if (caps.OutputFormat.EncodingType is EncodingSt2084 or EncodingHlg)
        {
            log.Info("color", $"{display} outputs an HDR encoding; colour controls are not offered.");
            return null;
        }

        var blocks = new CtlPixTxBlockConfig[caps.NumBlocks];
        fixed (CtlPixTxBlockConfig* buffer = blocks)
        {
            caps.BlockConfigs = (nint)buffer;
            result = session.Call(api.PixTxGetConfig, output.Handle, ref caps);
        }

        if (result != IgclResult.Success)
        {
            log.Info("color", $"{display} colour blocks refused ({IgclResult.Describe(result)}).");
            return null;
        }

        CtlPixTxBlockConfig? curve = null;
        CtlPixTxBlockConfig? matrix = null;
        var curveIndex = -1;
        var matrixIndex = -1;
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            // Intel's sample takes the last 1D LUT for desktop gamma: in HDR only that one is reported.
            if (block.BlockType == BlockType1dLut)
            {
                curve = block;
                curveIndex = index;
            }
            else if (block.BlockType == BlockType3x3Matrix && matrix is null)
            {
                matrix = block;
                matrixIndex = index;
            }
        }

        if (curve is not { } lut
            || lut.Config.OneDLut.SamplingType != SamplingUniform
            || lut.Config.OneDLut.SamplesPerChannel < 2
            || lut.Config.OneDLut.Channels is not (1 or 3))
        {
            log.Info("color", $"{display} has no uniformly sampled 1D LUT; colour controls are not offered.");
            return null;
        }

        return new ColorPipeline(session, output, display, lut, matrix, matrix is not null && matrixIndex < curveIndex,
            store, log);
    }

    /// <summary>Reads one field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The value, or unknown when the driver holds something the consumer cannot name.</returns>
    public ControlRead Read(ColorField field)
    {
        Observe(false);
        if ((field.InMatrix ? _matrix : _curve) is { } known)
        {
            return ControlRead.Of(GpuValue.Integer(field.Get(known)));
        }

        return _result == IgclResult.Success ? ControlRead.Failed(_observation) : ControlRead.Driver(_result);
    }

    /// <summary>Writes one field, carrying the other colour values.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="admission">Rechecked after preparatory reads and before the setter.</param>
    /// <returns>How the driver answered.</returns>
    /// <remarks>
    ///     The curve and the matrix go out together in one set call, so the curve read afterwards also
    ///     vouches for the matrix. Values the driver holds but the consumer cannot name are unknown and
    ///     have been published as such, so they cannot be carried: the write starts them from neutral and
    ///     says so in the log.
    /// </remarks>
    public ControlWrite Write(ColorField field, int value, WriteAdmission admission)
    {
        Observe(true);
        var neutral = ColorSettings.Neutral;
        if (_curve is null || (HasMatrix && _matrix is null))
        {
            _log.Info(
                "color",
                $"{_display}: the driver holds colour the consumer did not write; writing "
                + $"{field.Label.ToLowerInvariant()} replaces it and starts the unknown colour values from neutral.");
        }

        var curve = _curve ?? neutral;
        var matrix = _matrix ?? neutral;
        var settings = field.Set(
            new ColorSettings(curve.Brightness, curve.Contrast, curve.Gamma, matrix.Hue, matrix.Saturation),
            value);
        var result = Apply(settings, admission);
        _observedPass = 0;
        var write = ControlWrite.From(result, $"the {field.Label.ToLowerInvariant()} of {_display}");
        if (write.Status == WriteStatus.Applied)
        {
            _store.Set(_display, settings);
        }

        return write;
    }

    /// <summary>What a <c>CTL_PIXTX_CONFIG_QUERY_TYPE_CURRENT</c> query of the curve answered.</summary>
    /// <param name="result">The query's result.</param>
    /// <returns>How to read it.</returns>
    internal static CurrentQuery ClassifyCurrent(int result)
    {
        return result switch
        {
            IgclResult.Success => CurrentQuery.Held,
            // Nothing set since the driver started: older drivers say so with DATA_NOT_FOUND, current ones
            // with KMD_CALL. The pipe then applies the driver's own, neutral configuration.
            IgclResult.DataNotFound or IgclResult.KmdCall => CurrentQuery.NeverSet,
            // Another writer's configuration is the current one; what this block holds is not reported.
            IgclResult.InvalidPixTxBlockType => CurrentQuery.NotReported,
            _ => CurrentQuery.Failed
        };
    }

    /// <summary>Reads the pipe once per pass and recognises what it holds.</summary>
    /// <param name="fresh">Whether to read even when this pass already did, before a write.</param>
    private void Observe(bool fresh)
    {
        if (!fresh && _observedPass == _session.Pass)
        {
            return;
        }

        _observedPass = _session.Pass;
        _result = IgclResult.Success;
        _curve = null;
        _matrix = null;
        var neutral = ColorSettings.Neutral;
        var stored = Recorded();
        var block = _lutBlock;
        var result = Query(&block);
        string message;
        switch (ClassifyCurrent(result))
        {
            case CurrentQuery.Held:
                var channel = _samples.AsSpan(0, _neutralCurve.Length);
                if (stored is { } record && ColorMath.Matches(_recordedCurve, channel))
                {
                    // The consumer's own curve, set together with its matrix.
                    _curve = record;
                    _matrix = HasMatrix ? record : null;
                    message = "The display's colour is the consumer's own.";
                }
                else if (ColorMath.Matches(_neutralCurve, channel))
                {
                    _curve = neutral;
                    message = "The display holds a neutral tone curve the consumer did not write; "
                              + "hue and saturation are unknown.";
                }
                else
                {
                    message = "The display holds a tone curve the consumer did not write; its colour is unknown.";
                }

                break;
            case CurrentQuery.NeverSet:
                _curve = neutral;
                _matrix = HasMatrix ? neutral : null;
                message = "No colour has been set on the display since the driver started; it is neutral.";
                break;
            case CurrentQuery.NotReported:
                message = "Another writer set the display's colour last and the driver does not report it; "
                          + $"its colour is unknown ({IgclResult.Describe(result)}).";
                break;
            default:
                _result = result;
                message = $"The display's colour could not be read ({IgclResult.Describe(result)}).";
                break;
        }

        _observation = message;
        _log.LogChange(GpuLogLevel.Info, "color", _curveKey, $"{_display}: {message}");
    }

    /// <summary>The recorded settings, with their curve rebuilt only when the record changed.</summary>
    private ColorSettings? Recorded()
    {
        var stored = _store.Get(_display);
        if (stored is { } record && stored != _recorded)
        {
            ColorMath.FillCurve(record, _recordedCurve);
        }

        _recorded = stored;
        return stored;
    }

    private int Query(CtlPixTxBlockConfig* block)
    {
        CtlPixTxPipeGetConfig request = default;
        request.QueryType = QueryCurrent;
        request.NumBlocks = 1;
        request.BlockConfigs = (nint)block;
        return _session.Call(_session.Api.PixTxGetConfig, _output.Handle, ref request);
    }

    /// <summary>Sets the curve and, when the pipe has one, the matrix in one call, in pipe order.</summary>
    private int Apply(ColorSettings settings, WriteAdmission admission)
    {
        var lut = _lutBlock.Config.OneDLut;
        var perChannel = (int)lut.SamplesPerChannel;
        for (var channel = 0; channel < lut.Channels; channel++)
        {
            ColorMath.FillCurve(settings, _samples.AsSpan(channel * perChannel, perChannel));
        }

        var blocks = stackalloc CtlPixTxBlockConfig[2];
        uint count = 1;
        blocks[0] = _lutBlock;
        if (_matrixBlock is { } matrixBlock)
        {
            var coefficients = ColorMath.HueSaturationMatrix(settings);
            for (var index = 0; index < 3; index++)
            {
                matrixBlock.Config.Matrix.PreOffsets[index] = 0;
                matrixBlock.Config.Matrix.PostOffsets[index] = 0;
            }

            for (var index = 0; index < MatrixSize; index++)
            {
                matrixBlock.Config.Matrix.Matrix[index] = coefficients[index];
            }

            blocks[_matrixFirst ? 0 : 1] = matrixBlock;
            blocks[_matrixFirst ? 1 : 0] = _lutBlock;
            count = 2;
        }

        CtlPixTxPipeSetConfig request = default;
        request.OperationType = OperationSetCustom;
        request.Flags = FlagPersist;
        request.NumBlocks = count;
        request.BlockConfigs = (nint)blocks;
        admission.Check();
        return _session.Call(_session.Api.PixTxSetConfig, _output.Handle, ref request);
    }
}

/// <summary>How a current-configuration query of the curve answered.</summary>
internal enum CurrentQuery
{
    /// <summary>The driver returned the curve it holds.</summary>
    Held,

    /// <summary>Nothing has been set since the driver started; the pipe is neutral.</summary>
    NeverSet,

    /// <summary>Another writer's configuration is current and the driver does not report this block.</summary>
    NotReported,

    /// <summary>The query failed.</summary>
    Failed
}

/// <summary>One colour value of one display.</summary>
internal sealed class ColorControl : IntelControl
{
    private readonly ColorField _field;
    private readonly ColorPipeline _pipeline;

    private ColorControl(ColorPipeline pipeline, ColorField field, GpuDescriptor descriptor)
        : base(descriptor)
    {
        _pipeline = pipeline;
        _field = field;
    }

    /// <summary>Builds the colour controls of one display.</summary>
    /// <param name="pipeline">The pipe.</param>
    /// <param name="instance">The display's instance id.</param>
    /// <param name="placement">Where they sit.</param>
    /// <returns>The controls.</returns>
    public static IReadOnlyList<ColorControl> Build(ColorPipeline pipeline, string instance, Placement placement)
    {
        List<ColorControl> controls = [];
        foreach (var field in ColorField.All)
        {
            if (field.InMatrix && !pipeline.HasMatrix)
            {
                continue;
            }

            controls.Add(new ColorControl(pipeline, field,
                DriverDescriptors.Range(field.Id, instance, field.Label, field.Range, field.Unit,
                    placement.Plus(controls.Count))));
        }

        return controls;
    }

    /// <inheritdoc />
    public override ControlRead Read()
    {
        return _pipeline.Read(_field);
    }

    /// <inheritdoc />
    protected override ControlWrite WriteValidated(GpuValue value, WriteAdmission admission)
    {
        return _pipeline.Write(_field, value.IntegerValue!.Value, admission);
    }
}
