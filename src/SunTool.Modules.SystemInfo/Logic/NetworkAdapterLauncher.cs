using System.Diagnostics;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>
/// Windows 의 랜카드 속성 창을 엽니다 (제어판 &gt; 네트워크 연결 &gt; 우클릭 &gt; 속성 과 같은 창).
/// IP 변경은 Windows 가 관리자 확인(UAC)을 요구하므로, 창을 열 때 확인 창이 뜰 수 있습니다.
/// </summary>
public static class NetworkAdapterLauncher
{
    /// <summary>"네트워크 연결" 셸 폴더 (ncpa.cpl).</summary>
    private const string NetworkConnectionsFolder = "shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}";

    /// <summary>연결 이름(예: "이더넷 2")의 속성 창을 엽니다. 찾지 못하면 false (호출 쪽에서 <see cref="OpenConnectionsFolder"/>).</summary>
    /// <remarks>Shell.Application COM 을 쓰므로 STA 스레드(UI 스레드)에서 불러야 합니다.</remarks>
    public static bool TryOpenProperties(string connectionName)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return false;
        }

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic? folder = shell!.NameSpace(NetworkConnectionsFolder);
            if (folder is null)
            {
                return false;
            }

            foreach (dynamic item in folder.Items())
            {
                if (string.Equals((string)item.Name, connectionName, StringComparison.CurrentCultureIgnoreCase))
                {
                    item.InvokeVerb("properties");
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shell is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    /// <summary>네트워크 연결 창 전체를 엽니다.</summary>
    public static void OpenConnectionsFolder() =>
        Process.Start(new ProcessStartInfo("control.exe", "ncpa.cpl") { UseShellExecute = true });
}
