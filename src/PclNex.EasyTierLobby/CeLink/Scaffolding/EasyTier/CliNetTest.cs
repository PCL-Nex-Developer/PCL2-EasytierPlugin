using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL.Core.Link.Scaffolding.EasyTier;

public class CliNetTest
{
    public enum NatType
    {
        Unknown,
        OpenInternet,
        NoPat,
        FullCone,
        Restricted,
        PortRestricted,
        SymmetricEasy,
        Symmetric,
        SymmetricFirewall,
        UdpBlocked
    }
    public record NetStatus
    {
        public required NatType UdpNatType;
        public required NatType TcpNatType;
        public required bool SupportIPv6;
    }

    public static async Task<NetStatus?> GetNetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var cliProcess = CreateProcess();
            try
            {
                if (!cliProcess.Start())
                    throw new InvalidOperationException("EasyTier CLI process did not start.");

                var stdoutTask = cliProcess.StandardOutput.ReadToEndAsync(timeoutSource.Token);
                var stderrTask = cliProcess.StandardError.ReadToEndAsync(timeoutSource.Token);
                await cliProcess.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                if (cliProcess.ExitCode != 0)
                {
                    LogWrapper.Warn("Link", $"EasyTier network test attempt {attempt} exited with code {cliProcess.ExitCode}: {stderr.Trim()}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    LogWrapper.Warn("Link", $"EasyTier network test attempt {attempt} returned empty output. stderr: {stderr.Trim()}");
                    continue;
                }

                var stunInfo = JsonSerializer.Deserialize<StunInfo>(stdout, JsonCompat.SerializerOptions);
                if (stunInfo is null)
                    continue;

                var supportIPv6 = stunInfo.Ips.Any(ip => ip.Contains(':'));
                return new NetStatus
                {
                    UdpNatType = GetNatTypeViaCode(stunInfo.UdpNatType),
                    TcpNatType = GetNatTypeViaCode(stunInfo.TcpNatType),
                    SupportIPv6 = supportIPv6
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(cliProcess);
                LogWrapper.Warn("Link", "EasyTier network test timed out after 30 seconds.");
                return null;
            }
            catch (Exception ex)
            {
                TryKill(cliProcess);
                LogWrapper.Warn(ex, "Link", $"EasyTier network test attempt {attempt} failed");
            }
        }

        return null;
    }

    private static Process CreateProcess()
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = $"{EasyTierMetadata.EasyTierFilePath}\\easytier-cli.exe",
                WorkingDirectory = EasyTierMetadata.EasyTierFilePath,
                Arguments = "-o json stun",
                ErrorDialog = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8
            }
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }
    }

    public static NatType GetNatTypeViaCode(int type) => type switch
    {
        0 => NatType.OpenInternet,
        1 => NatType.NoPat,
        2 => NatType.FullCone,
        3 => NatType.Restricted,
        4 => NatType.PortRestricted,
        5 => NatType.SymmetricEasy,
        6 => NatType.Symmetric,
        7 => NatType.SymmetricFirewall,
        8 => NatType.UdpBlocked,
        _ => NatType.Unknown
    };

    public static string GetNatTypeString(NatType type)
    {
        return Lang.Text(type switch
        {
            NatType.OpenInternet or NatType.NoPat => "Link.Nat.Type.Open",
            NatType.FullCone => "Link.Nat.Type.FullCone",
            NatType.PortRestricted => "Link.Nat.Type.PortRestricted",
            NatType.Restricted => "Link.Nat.Type.Restricted",
            NatType.SymmetricEasy => "Link.Nat.Type.SymmetricEasy",
            NatType.Symmetric => "Link.Nat.Type.Symmetric",
            NatType.SymmetricFirewall => "Link.Nat.Type.SymmetricFirewall",
            NatType.UdpBlocked => "Link.Nat.Type.UdpBlocked",
            _ => "Link.Nat.Type.Unknown"
        });
    }
}