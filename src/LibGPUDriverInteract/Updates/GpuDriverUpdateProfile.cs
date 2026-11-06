using System.Globalization;
using System.Text.RegularExpressions;

namespace LibGPUDriverInteract;

/// <summary>The source comparison and supported installer contract of one vendor.</summary>
internal sealed partial record GpuDriverUpdateProfile(GpuVendor Vendor, string InstallerName, string Arguments,
    string[] SignerNames)
{
    private static readonly GpuDriverUpdateProfile Intel = new(GpuVendor.Intel, "Installer.exe", "-s", ["Intel Corporation"]);
    private static readonly GpuDriverUpdateProfile Amd = new(GpuVendor.Amd, "setup.exe", "-INSTALL",
        ["Advanced Micro Devices", "Advanced Micro Devices, Inc.", "Advanced Micro Devices Inc."]);
    private static readonly GpuDriverUpdateProfile Nvidia = new(GpuVendor.Nvidia, "setup.exe", "-s -n Display.Driver",
        ["NVIDIA Corporation"]);

    internal static GpuDriverUpdateProfile For(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Intel => Intel,
        GpuVendor.Amd => Amd,
        GpuVendor.Nvidia => Nvidia,
        _ => throw new NotSupportedException("Unknown graphics driver vendor.")
    };

    internal bool? IsNewer(GpuDriverPackage package)
    {
        var installed = package.Adapter.Version;
        if (Vendor == GpuVendor.Nvidia && Version.TryParse(installed, out var windows))
        {
            var digits = (windows.Build % 10 * 10000 + windows.Revision).ToString("D5", CultureInfo.InvariantCulture);
            installed = digits.Insert(digits.Length - 2, ".");
        }

        if (!Version.TryParse(installed, out var current))
        {
            throw new InvalidDataException("The installed driver version could not be compared.");
        }

        if (Vendor == GpuVendor.Amd)
        {
            var versions = StoreVersion().Matches(package.ReleaseNotes)
                .Select(match => Version.Parse(match.Groups[1].Value)).Order().ToArray();
            if (versions.Length == 0)
            {
                throw new InvalidDataException("AMD did not publish its Windows driver-store versions.");
            }

            if (versions.Contains(current) || current > versions[^1])
            {
                return false;
            }

            if (current < versions[0])
            {
                return true;
            }

            // An absent date cannot select between architecture-specific branches. Report unavailable,
            // rather than a failed check or an invented up-to-date/available answer.
            return package.Adapter.Date is { } date ? date.Date < package.Date.Date : null;
        }

        return Version.TryParse(package.Version, out var available)
            ? available > current
            : throw new InvalidDataException("The published driver version could not be compared.");
    }

    internal string InstallArguments(string resultLog) => Vendor == GpuVendor.Amd
        ? Arguments + " -LOG \"" + resultLog + "\"" : Arguments;

    internal async Task<GpuDriverInstallResult> ReadResultAsync(int code, string resultLog)
    {
        if (Vendor == GpuVendor.Amd && code is 0 or 3010)
        {
            return File.Exists(resultLog)
                ? ReadAmdResult(code, await File.ReadAllTextAsync(resultLog).ConfigureAwait(false))
                : new GpuDriverInstallResult(false, code == 3010, code,
                    "AMD did not publish its installation result. Check the installed version before another explicit attempt.");
        }

        return ReadExitCode(code);
    }

    internal GpuDriverInstallResult ReadExitCode(int code)
    {
        var reboot = code == 3010 || (Vendor == GpuVendor.Nvidia && code == 1)
            || (Vendor == GpuVendor.Intel && code is 2 or 14 or 17 or 18);
        var success = code == 0 || reboot;
        var detail = Vendor == GpuVendor.Intel && code is 17 or 18
            ? "Intel driver installed. Shut Windows down, then power the device on to finish."
            : success ? reboot ? "Driver installed. Restart Windows to finish." : "The vendor installer completed successfully."
            : $"The vendor installer returned {code}. Check the installed driver before another explicit attempt."
                + (Vendor == GpuVendor.Intel && code == 13 ? " Restart Windows before trying again." : "");
        return new GpuDriverInstallResult(success, reboot, code, detail);
    }

    internal static GpuDriverInstallResult ReadAmdResult(int exitCode, string log)
    {
        var result = ResultCode().Matches(log);
        var packages = PackageCode().Matches(log)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        var overall = result.Count == 1 ? int.Parse(result[0].Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        var succeeded = overall == 0 && packages.All(code => code is 0 or 3);
        var reboot = exitCode == 3010 || overall == 2 || packages.Any(code => code is 2 or 3);
        return new GpuDriverInstallResult(succeeded, reboot, exitCode, succeeded
            ? reboot ? "AMD driver installed. Restart Windows to finish." : "AMD driver installation completed."
            : "AMD reported a failed or unconfirmed installation. Check the installed version before another explicit attempt."
                + (reboot ? " Restart Windows before trying again." : ""));
    }

    [GeneratedRegex(@"Windows Driver Store Version\s+(\d+\.\d+\.\d+\.\d+)")]
    private static partial Regex StoreVersion();
    [GeneratedRegex(@"(?im)^\s*ResultCode\s*=\s*(\d+)\s*$")]
    private static partial Regex ResultCode();
    [GeneratedRegex(@"(?im)^\s*ErrorCode\s*=\s*(\d+)\s*$")]
    private static partial Regex PackageCode();
}
