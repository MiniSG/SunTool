using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SunTool.Modules.MemoryWatch.Logic;

/// <summary>실행 중인 프로그램 하나 (목록 표시용).</summary>
public sealed record ProcessEntry(int Id, string Name, long PrivateBytes, string WindowTitle)
{
    public string MemoryText => $"{PrivateBytes / (1024 * 1024):N0} MB";

    public string DisplayText => string.IsNullOrEmpty(WindowTitle) ? Name : $"{Name} — {WindowTitle}";
}

/// <summary>
/// 다른 프로세스의 메모리·핸들·GDI/USER·스레드 수를 읽습니다. 값을 읽기만 하고 디버거로 붙거나 멈추게 하지 않습니다.
/// 메모리·핸들·스레드는 Windows 의 프로세스 목록 정보로 읽어 권한이 없어도 됩니다.
/// GDI/USER 개체 수와 32/64비트 여부는 조회 권한(PROCESS_QUERY_LIMITED_INFORMATION)이 필요해, 관리자·서비스 프로세스는 못 읽을 수 있습니다.
/// </summary>
public sealed class ProcessSampler : IDisposable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;

    private readonly Process _process;
    private readonly IntPtr _queryHandle;

    private ProcessSampler(Process process)
    {
        _process = process;
        Id = process.Id;
        Name = process.ProcessName;
        _queryHandle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)Id);
        CanReadGui = _queryHandle != IntPtr.Zero;
        try
        {
            StartTime = process.StartTime;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            StartTime = null;
        }

        Is32Bit = CanReadGui && Environment.Is64BitOperatingSystem && IsWow64Process(_queryHandle, out var wow64) && wow64;
    }

    public int Id { get; }

    public string Name { get; }

    public DateTime? StartTime { get; }

    /// <summary>GDI/USER 개체 수를 읽을 수 있는지 (조회 권한).</summary>
    public bool CanReadGui { get; }

    /// <summary>32비트 프로그램이면 true (메모리 한계 2 GB, 대용량 주소 사용 시 4 GB).</summary>
    public bool Is32Bit { get; }

    /// <summary>PID 로 감시를 시작합니다. 프로세스가 없으면 null.</summary>
    public static ProcessSampler? Open(int processId)
    {
        try
        {
            return new ProcessSampler(Process.GetProcessById(processId));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>같은 이름으로 실행 중인 프로세스 중 가장 최근에 시작한 것 (다시 켜졌을 때 이어서 감시).</summary>
    public static ProcessSampler? OpenNewestByName(string name, int excludeId)
    {
        var candidates = Process.GetProcessesByName(name).Where(p => p.Id != excludeId).ToList();
        try
        {
            var best = candidates
                .Select(p => (Process: p, Start: SafeStart(p)))
                .OrderByDescending(x => x.Start ?? DateTime.MinValue)
                .FirstOrDefault().Process;
            return best is null ? null : Open(best.Id);
        }
        finally
        {
            foreach (var p in candidates)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>실행 중인 프로그램 목록 (메모리 많이 쓰는 순).</summary>
    public static List<ProcessEntry> ListProcesses()
    {
        var list = new List<ProcessEntry>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id is 0 or 4)
                {
                    continue; // Idle, System
                }

                try
                {
                    string title;
                    try
                    {
                        title = p.MainWindowTitle;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                        title = string.Empty;
                    }

                    list.Add(new ProcessEntry(p.Id, p.ProcessName, p.PrivateMemorySize64, title));
                }
                catch (InvalidOperationException)
                {
                    // 목록을 읽는 사이 끝난 프로세스
                }
            }
        }

        return list.OrderByDescending(e => e.PrivateBytes).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>한 번 측정합니다. 프로세스가 끝났으면 null.</summary>
    public MemorySample? Sample(DateTime time)
    {
        if (HasExited())
        {
            return null;
        }

        try
        {
            _process.Refresh();
            var gdi = CanReadGui ? (int)GetGuiResources(_queryHandle, GrGdiObjects) : -1;
            var user = CanReadGui ? (int)GetGuiResources(_queryHandle, GrUserObjects) : -1;
            var (commit, limit) = SystemCommit();
            return new MemorySample(time, _process.PrivateMemorySize64, _process.HandleCount, gdi, user, _process.Threads.Count)
            {
                PagedPool = _process.PagedSystemMemorySize64,
                NonPagedPool = _process.NonpagedSystemMemorySize64,
                VirtualSize = _process.VirtualMemorySize64,
                SystemCommit = commit,
                SystemCommitLimit = limit,
            };
        }
        catch (InvalidOperationException)
        {
            return null; // 측정 중에 끝남
        }
    }

    private bool HasExited()
    {
        if (CanReadGui)
        {
            // 조회 핸들이 있으면 종료 코드로 정확히 확인 (PID 재사용과 헷갈리지 않음)
            return GetExitCodeProcess(_queryHandle, out var code) && code != StillActive;
        }

        try
        {
            using var current = Process.GetProcessById(Id);
            return !string.Equals(current.ProcessName, Name, StringComparison.OrdinalIgnoreCase)
                   || (StartTime is { } start && SafeStart(current) is { } now && now != start);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_queryHandle != IntPtr.Zero)
        {
            CloseHandle(_queryHandle);
        }

        _process.Dispose();
    }

    private static DateTime? SafeStart(Process p)
    {
        try
        {
            return p.StartTime;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>시스템 전체 커밋과 한도 (바이트). 못 읽으면 -1.</summary>
    public static (long Commit, long Limit) SystemCommit()
    {
        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref info, info.Size))
        {
            return (-1, -1);
        }

        var page = (long)info.PageSize;
        return ((long)info.CommitTotal * page, (long)info.CommitLimit * page);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

    private const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
