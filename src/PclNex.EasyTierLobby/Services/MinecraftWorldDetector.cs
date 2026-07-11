using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PclNex.EasyTierLobby.Services;

internal static class MinecraftWorldDetector
{
    public static IReadOnlyList<FoundWorld> FindJavaPorts()
    {
        var worlds = new List<FoundWorld>();
        foreach (var processName in new[] { "java", "javaw" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                foreach (var port in GetProcessTcpPorts(process.Id))
                {
                    worlds.Add(new FoundWorld($"Java PID {process.Id} - 端口 {port}", port));
                }
            }
        }

        return worlds
            .GroupBy(world => world.Port)
            .Select(group => group.First())
            .OrderBy(world => world.Port)
            .ToArray();
    }

    private static IEnumerable<int> GetProcessTcpPorts(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            yield break;
        }

        var tcpTable = IntPtr.Zero;
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 3, 0);
        if (result != 0 && result != 122)
        {
            yield break;
        }

        tcpTable = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(tcpTable, ref size, true, 2, 3, 0) != 0)
            {
                yield break;
            }

            var rowCount = Marshal.ReadInt32(tcpTable);
            var rowPtr = IntPtr.Add(tcpTable, 4);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                if (row.OwningPid == processId)
                {
                    yield return IPAddress.NetworkToHostOrder((short)row.LocalPort) & 0xffff;
                }

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTable);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int outBufLen,
        bool order,
        int ipVersion,
        int tableClass,
        int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public int State;
        public int LocalAddr;
        public int LocalPort;
        public int RemoteAddr;
        public int RemotePort;
        public int OwningPid;
    }
}

internal sealed record FoundWorld(string Name, int Port);
