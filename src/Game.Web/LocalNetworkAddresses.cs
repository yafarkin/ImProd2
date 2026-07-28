using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Game.Web;

/// <summary>
/// Адреса, по которым сервер реально доступен с телефонов в зале (SPEC §1 — локальный Wi-Fi), для
/// страницы подключения (<c>/join</c>). На машине обычно несколько активных интерфейсов (Wi-Fi,
/// Ethernet, VPN и т.п.) — какой из них ведёт в нужную сеть, заранее не известно, поэтому
/// показываются все, с подписью интерфейса, чтобы выбрать рабочий на месте.
/// </summary>
public static class LocalNetworkAddresses
{
    /// <summary>Базовые адреса (без пути) — переиспользуется и страницей подключения (`/login`), и QR конкретного участника (`/auth/login?code=...`, см. `ParticipantQr.razor`).</summary>
    public static IReadOnlyList<(string InterfaceName, string BaseUrl)> DiscoverBaseUrls(int port)
    {
        var result = new List<(string, string)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add((nic.Name, $"http://{address.Address}:{port}"));
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<(string InterfaceName, string Url)> DiscoverJoinUrls(int port) =>
        DiscoverBaseUrls(port).Select(a => (a.InterfaceName, $"{a.BaseUrl}/login")).ToList();
}
