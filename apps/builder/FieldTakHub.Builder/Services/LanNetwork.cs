using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FieldTakHub.Builder.Services;

/// <summary>
/// Wybór adresu IPv4, pod którym telefony w tej samej sieci (Wi-Fi albo hotspot Windows)
/// połączą się z serwerami Buildera. Zastępuje trzy osobne kopie tej logiki, które przy braku
/// sieci zwracały 127.0.0.1 (adres bezużyteczny dla telefonu) i nie odróżniały kart wirtualnych
/// (Hyper-V, WSL, VirtualBox, VPN) od prawdziwej sieci.
/// </summary>
internal static class LanAddressHelper
{
    /// <summary>Adres, który Windows nadaje sobie po włączeniu „Hotspotu mobilnego”.</summary>
    public const string WindowsMobileHotspotAddress = "192.168.137.1";

    private static readonly string[] VirtualAdapterHints =
    {
        "virtual", "hyper-v", "vethernet", "vmware", "wsl", "docker", "vpn", "tap-windows", "wireguard",
        "tailscale", "zerotier", "hamachi", "anyconnect", "fortinet", "pangp", "nordlynx", "npcap", "bluetooth"
    };

    /// <summary>Najlepszy adres LAN albo null, gdy komputer nie ma sieci dostępnej dla telefonów.</summary>
    public static string? GetBestLanIPv4()
    {
        var candidates = new List<(string Ip, int Score)>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch (Exception) { continue; }

                string label = ((nic.Name ?? string.Empty) + " " + (nic.Description ?? string.Empty)).ToLowerInvariant();
                bool looksVirtual = VirtualAdapterHints.Any(h => label.Contains(h));
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                bool isWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                {
                    IPAddress ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                    if (!IsPrivateIPv4(ip)) continue;

                    string text = ip.ToString();
                    int score;
                    if (text == WindowsMobileHotspotAddress) score = 100;
                    else if (looksVirtual) score = 10;
                    else if (hasGateway && isWireless) score = 80;
                    else if (hasGateway) score = 70;
                    else score = 40;
                    candidates.Add((text, score));
                }
            }
        }
        catch (Exception)
        {
            // Brak dostępu do listy kart sieciowych: zachowujemy się jak przy braku sieci.
        }

        if (candidates.Count == 0) return null;
        return candidates.OrderByDescending(c => c.Score).First().Ip;
    }

    /// <summary>Adresy prywatne RFC 1918 (10/8, 172.16/12, 192.168/16). Pomija APIPA 169.254/16.</summary>
    public static bool IsPrivateIPv4(IPAddress ip)
    {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>
    /// Ostrzeżenie do pokazania pod kodem QR, gdy adres serwera jest nieosiągalny dla telefonu
    /// (brak sieci, adres pętli zwrotnej). Zwraca null, gdy adres wygląda poprawnie.
    /// </summary>
    public static string? DescribePhoneReachabilityProblem(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Brak adresu serwera — telefon nie ma skąd pobrać danych.";
        if (url.Contains("//127.", StringComparison.Ordinal) || url.Contains("//localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "⚠ Komputer nie ma aktywnej sieci Wi-Fi/LAN ani hotspotu, więc ten kod wskazuje adres 127.0.0.1, " +
                   "z którym telefon się nie połączy. Włącz „Hotspot mobilny” w Windows albo połącz komputer z siecią Wi-Fi " +
                   "i otwórz to okno ponownie.";
        }

        return null;
    }
}

/// <summary>Wspólne drobiazgi prostych serwerów HTTP Buildera (paczki :8766, OPORD :8769).</summary>
internal static class LanHttp
{
    /// <summary>Krótka odpowiedź tekstowa (np. 404), zamiast zrywania połączenia bez słowa.</summary>
    public static void WritePlainResponse(Stream stream, string status, string message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message ?? string.Empty);
        string headers =
            "HTTP/1.1 " + status + "\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Connection: close\r\n\r\n";
        byte[] head = Encoding.ASCII.GetBytes(headers);
        stream.Write(head, 0, head.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    /// <summary>Ścieżka żądania bez zapytania (?…), odkodowana z %XX.</summary>
    public static string DecodeRequestPath(string requestLine)
    {
        string[] parts = (requestLine ?? string.Empty).Split(' ');
        string raw = parts.Length >= 2 ? parts[1] : "/";
        int queryAt = raw.IndexOf('?');
        if (queryAt >= 0) raw = raw.Substring(0, queryAt);
        return Uri.UnescapeDataString(raw);
    }

    /// <summary>
    /// Nazwa zasobu z adresu „/prefiks/Nazwa.rozszerzenie”, np. „/deploy/BLUFOR.ftak” → „BLUFOR”.
    /// Zwraca null, gdy ścieżka nie zaczyna się od prefiksu albo nie ma jednego z rozszerzeń.
    /// </summary>
    public static string? ResourceName(string path, string prefix, params string[] extensions)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string name = path.Substring(prefix.Length).Trim('/');
        foreach (string ext in extensions)
        {
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - ext.Length);
        }

        return null;
    }
}
