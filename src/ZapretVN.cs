// Zapret VN — программа в трее для управления zapret (winws) во Вьетнаме.
// Сборка: src\build.cmd (нужен только .NET Framework 4.8, он есть в Windows 10/11).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Zapret VN")]
[assembly: System.Reflection.AssemblyProduct("Zapret VN")]
[assembly: System.Reflection.AssemblyDescription("Обход DPI-блокировок (zapret / winws)")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace ZapretVN
{
    static class App
    {
        public const string Name = "Zapret VN";
        public const string Version = "1.0.0";
        public const string Root = @"C:\zapret-vietnam";
        public const string ServiceName = "zapret-vn";
        public const string TaskName = "ZapretVN";
        public const string GitHubUrl = "https://github.com/mrme0ws";
        public const string TelegramUrl = "https://t.me/mrmeows";

        public static void OpenUrl(string url)
        {
            // через explorer: программа запущена от админа, а браузер должен открыться обычным
            try { Process.Start("explorer.exe", "\"" + url + "\""); }
            catch { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
        }
        public static string Bin { get { return Path.Combine(Root, "bin"); } }
        public static string Fake { get { return Path.Combine(Root, "fake"); } }
        public static string Lists { get { return Path.Combine(Root, "lists"); } }
        public static string Config { get { return Path.Combine(Root, "config"); } }
        public static string Exe { get { return Path.Combine(Root, "ZapretVN.exe"); } }
        public static string Winws { get { return Path.Combine(Bin, "winws.exe"); } }
        public static string ListMain { get { return Path.Combine(Lists, "vietnam.txt"); } }
        public static string ListExclude { get { return Path.Combine(Lists, "exclude.txt"); } }
        public static string ListAuto { get { return Path.Combine(Lists, "autohostlist.txt"); } }
        public static string LogFile { get { return Path.Combine(Root, "zapret-vn.log"); } }
    }

    // ===================================================================== данные

    static class Defaults
    {
        public const string Main =
@"# Домены, к которым всегда применяется обход (поддомены включаются автоматически).
steampowered.com
steamcommunity.com
steamstatic.com
steam-chat.com
steamgames.com
steamusercontent.com
steamserver.net
s.team
valvesoftware.com
roblox.com
rbxcdn.com
rbx.com
robloxlabs.com
telegram.org
telegram.me
t.me
telesco.pe
tdesktop.com
";
        public const string Exclude =
@"# Домены, к которым обход не применяется никогда.
vietcombank.com.vn
techcombank.com.vn
bidv.com.vn
vietinbank.vn
mbbank.com.vn
momo.vn
zalopay.vn
gov.vn
";
    }

    class Strategy
    {
        public string Id, Title, Args;
        public Strategy(string id, string title, string args) { Id = id; Title = title; Args = args; }
        public override string ToString() { return Title; }
    }

    static class Strategies
    {
        const string FakeTls = "{FAKE}\\tls_clienthello_www_google_com.bin";
        // Порядок = приоритет при автоподборе
        public static readonly Strategy[] All =
        {
            new Strategy("multidisorder", "Multidisorder (лучшая для FPT)", "--dpi-desync=multidisorder --dpi-desync-split-pos=1,midsld"),
            new Strategy("fake_multidisorder", "Fake + multidisorder (badseq)", "--dpi-desync=fake,multidisorder --dpi-desync-split-pos=1,midsld --dpi-desync-fooling=badseq --dpi-desync-fake-tls=" + FakeTls),
            new Strategy("fake_multisplit", "Fake + multisplit (badseq)", "--dpi-desync=fake,multisplit --dpi-desync-split-pos=1,midsld --dpi-desync-fooling=badseq --dpi-desync-fake-tls=" + FakeTls),
            new Strategy("multisplit", "Multisplit", "--dpi-desync=multisplit --dpi-desync-split-pos=1,midsld"),
            new Strategy("multisplit_sni", "Multisplit по SNI", "--dpi-desync=multisplit --dpi-desync-split-pos=1,sniext+1,host+1,midsld"),
            new Strategy("fakedsplit", "Fakedsplit (badseq)", "--dpi-desync=fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq"),
            new Strategy("fake_fakedsplit", "Fake + fakedsplit (autottl)", "--dpi-desync=fake,fakedsplit --dpi-desync-autottl=5 --dpi-desync-split-pos=1 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com"),
            new Strategy("fake_autottl", "Fake (autottl)", "--dpi-desync=fake --dpi-desync-autottl=2 --dpi-desync-repeats=6 --dpi-desync-fake-tls=" + FakeTls),
            new Strategy("fake_md5sig", "Fake (md5sig)", "--dpi-desync=fake --dpi-desync-fooling=md5sig --dpi-desync-fake-tls=" + FakeTls),
            new Strategy("seqovl", "Seqovl 681", "--dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=" + FakeTls),
        };

        public static Strategy Get(string id)
        {
            return All.FirstOrDefault(s => s.Id == id) ?? All[0];
        }
    }

    static class Checks
    {
        // что показывать во вкладке «Проверка»: подпись | хост
        public static readonly string[][] Sites =
        {
            new[] { "Steam — магазин", "store.steampowered.com" },
            new[] { "Steam — сообщество", "steamcommunity.com" },
            new[] { "Steam — поддержка", "help.steampowered.com" },
            new[] { "Steam — оплата", "checkout.steampowered.com" },
            new[] { "Steam — картинки", "store.fastly.steamstatic.com" },
            new[] { "Steam — чат", "steam-chat.com" },
            new[] { "Roblox — API", "apis.roblox.com" },
            new[] { "Roblox — вход в игру", "gamejoin.roblox.com" },
            new[] { "Roblox — друзья и чат", "realtime-signalr.roblox.com" },
            new[] { "Telegram Web", "web.telegram.org" },
            new[] { "Discord", "discord.com" },
        };

        // для автоподбора стратегии
        public static readonly string[] PickHosts =
        {
            "store.steampowered.com", "steamcommunity.com", "help.steampowered.com", "checkout.steampowered.com",
            "store.fastly.steamstatic.com", "steam-chat.com", "apis.roblox.com", "gamejoin.roblox.com", "web.telegram.org",
        };

        // поиск блокировок: категория | хост для проверки | домен для списка
        public static readonly string[] Candidates =
        {
            "Steam|store.steampowered.com|steampowered.com", "Steam|steamcommunity.com|steamcommunity.com",
            "Steam|store.fastly.steamstatic.com|steamstatic.com", "Steam|steam-chat.com|steam-chat.com",
            "Roblox|apis.roblox.com|roblox.com",
            "Мессенджеры|telegram.org|telegram.org", "Мессенджеры|t.me|t.me", "Мессенджеры|discord.com|discord.com",
            "Мессенджеры|gateway.discord.gg|discord.gg", "Мессенджеры|signal.org|signal.org",
            "Игры|store.epicgames.com|epicgames.com", "Игры|www.ea.com|ea.com", "Игры|battle.net|battle.net",
            "Торренты|1337x.to|1337x.to", "Торренты|thepiratebay.org|thepiratebay.org", "Торренты|nyaa.si|nyaa.si",
            "Торренты|rutracker.org|rutracker.org", "Торренты|yts.mx|yts.mx", "Торренты|torrentgalaxy.to|torrentgalaxy.to",
            "Книги и наука|z-library.sk|z-library.sk", "Книги и наука|libgen.is|libgen.is", "Книги и наука|annas-archive.org|annas-archive.org",
            "Книги и наука|sci-hub.se|sci-hub.se", "Книги и наука|archive.org|archive.org",
            "Аниме и манга|mangadex.org|mangadex.org", "Аниме и манга|nhentai.net|nhentai.net", "Аниме и манга|e-hentai.org|e-hentai.org",
            "Аниме и манга|www.pixiv.net|pixiv.net",
            "Видео и музыка|vimeo.com|vimeo.com", "Видео и музыка|www.twitch.tv|twitch.tv", "Видео и музыка|soundcloud.com|soundcloud.com",
            "18+|www.pornhub.com|pornhub.com", "18+|www.xvideos.com|xvideos.com", "18+|www.xnxx.com|xnxx.com",
            "18+|xhamster.com|xhamster.com", "18+|onlyfans.com|onlyfans.com", "18+|chaturbate.com|chaturbate.com",
            "Ставки|www.bet365.com|bet365.com", "Ставки|1xbet.com|1xbet.com", "Ставки|www.188bet.com|188bet.com",
            "Ставки|www.fun88.com|fun88.com", "Ставки|www.dafabet.com|dafabet.com", "Ставки|stake.com|stake.com",
            "Ставки|www.pinnacle.com|pinnacle.com",
            "Криптобиржи|www.okx.com|okx.com", "Криптобиржи|www.bybit.com|bybit.com", "Криптобиржи|www.kucoin.com|kucoin.com",
            "Криптобиржи|www.binance.com|binance.com",
            "Новости|www.rfa.org|rfa.org", "Новости|www.voatiengviet.com|voatiengviet.com", "Новости|www.bbc.com|bbc.com",
            "Новости|www.nguoi-viet.com|nguoi-viet.com", "Новости|www.rfi.fr|rfi.fr", "Новости|www.dw.com|dw.com",
            "Новости|thoibao.de|thoibao.de", "Новости|www.viettan.org|viettan.org", "Новости|medium.com|medium.com",
            "VPN и приватность|www.torproject.org|torproject.org", "VPN и приватность|protonvpn.com|protonvpn.com",
            "VPN и приватность|nordvpn.com|nordvpn.com", "VPN и приватность|mullvad.net|mullvad.net",
            "VPN и приватность|www.expressvpn.com|expressvpn.com",
            "Другое|www.quora.com|quora.com", "Другое|www.scribd.com|scribd.com", "Другое|www.patreon.com|patreon.com",
            "Другое|archiveofourown.org|archiveofourown.org", "Другое|wikileaks.org|wikileaks.org",
            "Другое|www.reddit.com|reddit.com", "Другое|x.com|x.com",
        };
    }

    // ===================================================================== настройки

    static class Settings
    {
        static Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string FilePath { get { return Path.Combine(App.Config, "settings.ini"); } }

        public static void Load()
        {
            d.Clear();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = l.IndexOf('=');
                        if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
            }
            catch { }
        }

        public static bool Has(string k) { return d.ContainsKey(k); }

        public static string Get(string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) ? v : def;
        }

        public static void Set(string k, string v)
        {
            d[k] = v;
            Directory.CreateDirectory(App.Config);
            File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
        }
    }

    // ===================================================================== запуск процессов

    static class Shell
    {
        public class Result { public int Code; public string Out = ""; public string Err = ""; }

        public static Encoding Oem { get { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } }

        public static string Sys(string exe) { return Path.Combine(Environment.SystemDirectory, exe); }

        public static Result Run(string file, string args, int timeoutMs, Encoding enc = null)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = enc ?? Encoding.UTF8,
                StandardErrorEncoding = enc ?? Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                p.WaitForExit(3000);
                var r = new Result { Code = p.HasExited ? p.ExitCode : -1 };
                if (so.Wait(3000)) r.Out = so.Result;
                if (se.Wait(1000)) r.Err = se.Result;
                return r;
            }
        }

        public static Result PowerShell(string script, int timeoutMs)
        {
            string full = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, timeoutMs);
        }
    }

    // ===================================================================== служба winws

    static class Zapret
    {
        public static string TcpArgs()
        {
            return Strategies.Get(Settings.Get("strategy", "multidisorder")).Args;
        }

        public static string BuildArgs(string tcp)
        {
            string L = App.Lists, F = App.Fake;
            tcp = tcp.Replace("{FAKE}", F);
            return "--wf-tcp=80,443 --wf-udp=443"
                + " --filter-tcp=80,443 --hostlist=" + L + "\\vietnam.txt --hostlist-exclude=" + L + "\\exclude.txt"
                + " --hostlist-auto=" + L + "\\autohostlist.txt --hostlist-auto-fail-threshold=3 " + tcp
                + " --new --filter-udp=443 --hostlist=" + L + "\\vietnam.txt --hostlist-exclude=" + L + "\\exclude.txt"
                + " --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=" + F + "\\quic_initial_www_google_com.bin";
        }

        static ServiceController Svc()
        {
            return ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(App.ServiceName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool ServiceExists()
        {
            using (var s = Svc()) return s != null;
        }

        public static bool IsRunning()
        {
            using (var s = Svc()) return s != null && s.Status == ServiceControllerStatus.Running;
        }

        public static bool ServiceAutoStart()
        {
            using (var s = Svc()) return s != null && s.StartType == ServiceStartMode.Automatic;
        }

        static Shell.Result Sc(string args)
        {
            return Shell.Run(Shell.Sys("sc.exe"), args, 20000, Shell.Oem);
        }

        // создать/обновить службу с текущей стратегией
        public static void Configure(bool autostart)
        {
            string args = BuildArgs(TcpArgs());
            // --dry-run отказывается работать, пока запущен winws с тем же фильтром — поэтому сначала Stop()
            if (IsRunning()) throw new Exception("Configure: служба должна быть остановлена");
            KillStray();
            var dry = Shell.Run(App.Winws, "--dry-run " + args, 20000);
            if (dry.Code != 0)
            {
                string msg = (dry.Err + dry.Out).Trim();
                int nl = msg.IndexOf('\n');
                throw new Exception("winws не принял параметры (код " + dry.Code + "): " + (nl > 0 ? msg.Substring(0, nl) : msg));
            }
            string bin = App.Winws + " " + args;
            string start = autostart ? "auto" : "demand";
            Shell.Result r;
            if (!ServiceExists())
            {
                r = Sc("create " + App.ServiceName + " binPath= \"" + bin + "\" start= " + start + " DisplayName= \"Zapret VN\"");
                if (r.Code != 0) throw new Exception("Не удалось создать службу: " + (r.Out + r.Err).Trim());
                Sc("failure " + App.ServiceName + " reset= 0 actions= restart/5000/restart/5000/restart/30000");
            }
            else
            {
                r = Sc("config " + App.ServiceName + " binPath= \"" + bin + "\" start= " + start + " DisplayName= \"Zapret VN\"");
                if (r.Code != 0) throw new Exception("Не удалось обновить службу: " + (r.Out + r.Err).Trim());
            }
            Sc("description " + App.ServiceName + " \"Обход DPI-блокировок (Steam, Roblox и др.). Управление: программа Zapret VN.\"");
        }

        // параметры службы отстают от текущей версии программы (после обновления)?
        public static bool IsOutdated()
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + App.ServiceName))
            {
                if (k == null) return false;
                string cur = (k.GetValue("ImagePath", "", Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").Replace("\"", "").Trim();
                return !string.Equals(cur, App.Winws + " " + BuildArgs(TcpArgs()), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetStartMode(bool autostart)
        {
            if (ServiceExists()) Sc("config " + App.ServiceName + " start= " + (autostart ? "auto" : "demand"));
        }

        public static void Start()
        {
            using (var s = Svc())
            {
                if (s == null) throw new Exception("Служба не установлена");
                if (s.Status != ServiceControllerStatus.Running)
                {
                    KillStray();
                    s.Start();
                    s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            }
            Thread.Sleep(1200);
            if (!IsRunning()) throw new Exception("Служба запустилась и сразу остановилась. Попробуй другую стратегию.");
        }

        public static void Stop()
        {
            using (var s = Svc())
            {
                if (s != null && s.Status != ServiceControllerStatus.Stopped)
                {
                    if (s.Status != ServiceControllerStatus.StopPending) s.Stop();
                    s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
            }
            KillStray();
        }

        public static void KillStray()
        {
            foreach (var p in Process.GetProcessesByName("winws"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        public static void StopDriver() { Sc("stop WinDivert"); }

        public static void RemoveService()
        {
            if (ServiceExists()) { Stop(); Sc("delete " + App.ServiceName); }
            KillStray();
            StopDriver();
        }
    }

    // ===================================================================== автозапуск значка

    static class AutoStart
    {
        static string Esc(string s) { return System.Security.SecurityElement.Escape(s); }

        public static bool TaskExists()
        {
            return Shell.Run(Shell.Sys("schtasks.exe"), "/Query /TN " + App.TaskName, 15000, Shell.Oem).Code == 0;
        }

        public static void Enable()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
                + "<RegistrationInfo><Description>Zapret VN: значок в трее при входе в Windows</Description></RegistrationInfo>"
                + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Esc(user) + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>"
                + "<Principals><Principal id=\"Author\"><UserId>" + Esc(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>"
                + "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate>"
                + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>"
                + "<Actions Context=\"Author\"><Exec><Command>" + Esc(App.Exe) + "</Command><Arguments>--tray</Arguments>"
                + "<WorkingDirectory>" + Esc(App.Root) + "</WorkingDirectory></Exec></Actions></Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "zapretvn_task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            var r = Shell.Run(Shell.Sys("schtasks.exe"), "/Create /TN " + App.TaskName + " /XML \"" + tmp + "\" /F", 20000, Shell.Oem);
            try { File.Delete(tmp); } catch { }
            if (r.Code != 0) throw new Exception("Не удалось включить автозапуск: " + (r.Err + r.Out).Trim());
        }

        public static void Disable()
        {
            if (TaskExists()) Shell.Run(Shell.Sys("schtasks.exe"), "/Delete /TN " + App.TaskName + " /F", 15000, Shell.Oem);
        }
    }

    // ===================================================================== DNS через HTTPS

    static class SecureDns
    {
        const string Servers = "'8.8.8.8','8.8.4.4','1.1.1.1','1.0.0.1'";

        public static bool IsEnabled()
        {
            var r = Shell.PowerShell(
                "$ok=$true; $ad=@(Get-NetAdapter -Physical | ? Status -eq 'Up'); if ($ad.Count -eq 0) { $ok=$false };" +
                "foreach ($a in $ad) { $s=(Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4).ServerAddresses;" +
                " if ($s -notcontains '8.8.8.8' -and $s -notcontains '1.1.1.1') { $ok=$false } };" +
                "$d=Get-DnsClientDohServerAddress -ServerAddress 8.8.8.8 -EA SilentlyContinue; if (-not $d -or -not $d.AutoUpgrade) { $ok=$false };" +
                "if ($ok) {'YES'} else {'NO'}", 30000);
            return r.Out.Contains("YES");
        }

        public static string Enable()
        {
            var r = Shell.PowerShell(
                "foreach ($s in " + Servers + ") { $t = if ($s -like '8.*') {'https://dns.google/dns-query'} else {'https://cloudflare-dns.com/dns-query'};" +
                " if (Get-DnsClientDohServerAddress -ServerAddress $s -EA SilentlyContinue) { Set-DnsClientDohServerAddress -ServerAddress $s -AutoUpgrade $true -AllowFallbackToUdp $false | Out-Null }" +
                " else { Add-DnsClientDohServerAddress -ServerAddress $s -DohTemplate $t -AutoUpgrade $true -AllowFallbackToUdp $false | Out-Null } };" +
                "Get-NetAdapter -Physical | ? Status -eq 'Up' | % { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '8.8.8.8','1.1.1.1'; 'ADAPTER ' + $_.Name };" +
                "Clear-DnsClientCache", 60000);
            if (r.Code != 0) throw new Exception("Не удалось включить защищённый DNS: " + r.Err.Trim());
            return string.Join(", ", r.Out.Split('\n').Where(l => l.StartsWith("ADAPTER ")).Select(l => l.Substring(8).Trim()));
        }

        public static void Disable()
        {
            var r = Shell.PowerShell(
                "Get-NetAdapter -Physical | ? Status -eq 'Up' | % { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses };" +
                "foreach ($s in " + Servers + ") { Set-DnsClientDohServerAddress -ServerAddress $s -AutoUpgrade $false -AllowFallbackToUdp $true -EA SilentlyContinue | Out-Null };" +
                "Clear-DnsClientCache", 60000);
            if (r.Code != 0) throw new Exception("Не удалось выключить защищённый DNS: " + r.Err.Trim());
        }
    }

    // ===================================================================== сетевые проверки

    static class NetTest
    {
        static readonly Regex AnswerA = new Regex("\"type\"\\s*:\\s*1\\s*,[^}]*?\"data\"\\s*:\\s*\"(\\d+\\.\\d+\\.\\d+\\.\\d+)\"");

        // настоящий IP через DNS-over-HTTPS (провайдер не может подменить)
        public static string Doh(string host)
        {
            string[] urls = { "https://dns.google/resolve?type=A&name=", "https://cloudflare-dns.com/dns-query?type=A&name=" };
            foreach (var u in urls)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(u + Uri.EscapeDataString(host));
                    req.Timeout = 6000; req.ReadWriteTimeout = 6000; req.Accept = "application/dns-json"; req.Proxy = null;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        var m = AnswerA.Match(sr.ReadToEnd());
                        return m.Success ? m.Groups[1].Value : null;
                    }
                }
                catch { }
            }
            return null;
        }

        public static Dictionary<string, string> DohMany(IEnumerable<string> hosts)
        {
            var r = new ConcurrentDictionary<string, string>();
            Parallel.ForEach(hosts.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = 12 }, h => { r[h] = Doh(h); });
            return r.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static string CurlPath
        {
            get
            {
                string p = Shell.Sys("curl.exe");
                if (!File.Exists(p)) throw new Exception("Не найден curl.exe (нужна Windows 10 1803 или новее).");
                return p;
            }
        }

        // HTTPS-проверка хостов curl'ом. resolve = null -> системный DNS. Возвращает коды curl по раундам.
        public static Dictionary<string, List<int>> Curl(IList<string> hosts, Dictionary<string, string> resolve, int rounds, int timeoutSec)
        {
            var res = hosts.Distinct().ToDictionary(h => h, h => new List<int>());
            string curl = CurlPath;
            for (int round = 0; round < rounds; round++)
            {
                var sb = new StringBuilder("-Z --parallel-max 40 -s --ssl-no-revoke --max-time " + timeoutSec + " -w \"%{url} %{exitcode}\\n\"");
                foreach (var h in res.Keys)
                {
                    string ip;
                    if (resolve != null && resolve.TryGetValue(h, out ip) && ip != null) sb.Append(" --resolve " + h + ":443:" + ip);
                }
                foreach (var h in res.Keys) sb.Append(" -o NUL https://" + h + "/");
                var r = Shell.Run(curl, sb.ToString(), (timeoutSec + 15) * 1000);
                var seen = new HashSet<string>();
                foreach (var line in r.Out.Split('\n'))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length < 2) continue;
                    Uri uri; int code;
                    if (!Uri.TryCreate(p[0], UriKind.Absolute, out uri) || !int.TryParse(p[1], out code)) continue;
                    string h = uri.Host;
                    if (res.ContainsKey(h) && seen.Add(h)) res[h].Add(code);
                }
                foreach (var h in res.Keys) if (!seen.Contains(h)) res[h].Add(-1);
            }
            return res;
        }

        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "открывается";
                case 6: return "адрес не найден (DNS)";
                case 7: return "нет соединения";
                case 28: return "не отвечает (таймаут)";
                case 35: return "сброс при подключении (DPI)";
                case 56: return "соединение сброшено";
                case 60: return "ошибка сертификата";
                case -1: return "нет ответа";
                default: return "ошибка " + code;
            }
        }

        public static IPAddress Gateway()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var g in ni.GetIPProperties().GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) return g.Address;
            }
            return null;
        }

        // обычный DNS-запрос (UDP 53) — чтобы увидеть, что отвечает DNS провайдера
        // null = нет ответа, "" = нет A-записи
        public static string DnsQuery(IPAddress server, string host, int timeoutMs)
        {
            try
            {
                var q = new List<byte>();
                int id = new Random(Guid.NewGuid().GetHashCode()).Next(1, 65535);
                q.AddRange(new byte[] { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
                foreach (var label in host.TrimEnd('.').Split('.'))
                {
                    var b = Encoding.ASCII.GetBytes(label);
                    q.Add((byte)b.Length); q.AddRange(b);
                }
                q.AddRange(new byte[] { 0, 0, 1, 0, 1 });
                using (var u = new UdpClient())
                {
                    u.Client.ReceiveTimeout = timeoutMs;
                    u.Connect(server, 53);
                    u.Send(q.ToArray(), q.Count);
                    IPEndPoint ep = null;
                    byte[] r = u.Receive(ref ep);
                    if (r.Length < 12) return null;
                    int qd = (r[4] << 8) | r[5], an = (r[6] << 8) | r[7], pos = 12;
                    for (int i = 0; i < qd; i++) pos = SkipName(r, pos) + 4;
                    for (int i = 0; i < an && pos + 10 <= r.Length; i++)
                    {
                        pos = SkipName(r, pos);
                        int type = (r[pos] << 8) | r[pos + 1], len = (r[pos + 8] << 8) | r[pos + 9];
                        pos += 10;
                        if (type == 1 && len == 4) return r[pos] + "." + r[pos + 1] + "." + r[pos + 2] + "." + r[pos + 3];
                        pos += len;
                    }
                    return "";
                }
            }
            catch { return null; }
        }

        static int SkipName(byte[] r, int pos)
        {
            while (pos < r.Length)
            {
                int l = r[pos];
                if (l == 0) return pos + 1;
                if ((l & 0xC0) == 0xC0) return pos + 2;
                pos += l + 1;
            }
            return pos;
        }

        public static bool IsBogus(string ip)
        {
            return ip.StartsWith("127.") || ip == "0.0.0.0" || ip.StartsWith("10.") || ip.StartsWith("192.168.")
                || Regex.IsMatch(ip, "^172\\.(1[6-9]|2\\d|3[01])\\.");
        }
    }

    // ===================================================================== установка в C:\zapret-vietnam

    static class Installer
    {
        public static string SourceDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }

        public static bool RunningFromRoot
        {
            get { return string.Equals(Path.GetFullPath(SourceDir).TrimEnd('\\'), App.Root, StringComparison.OrdinalIgnoreCase); }
        }

        // Файлы zapret вшиты в exe как ресурсы "payload/<папка>/<файл>" (см. build.ps1).
        // В dev-сборке без ресурсов берутся из папок bin/fake/lists рядом с exe.
        const string PayloadPrefix = "payload/";

        static List<string> PayloadNames()
        {
            return typeof(Installer).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(PayloadPrefix)).ToList();
        }

        static byte[] ReadPayload(string name)
        {
            using (var s = typeof(Installer).Assembly.GetManifestResourceStream(name))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // [относительный путь "bin/winws.exe"] -> содержимое
        static Dictionary<string, Func<byte[]>> PayloadFiles()
        {
            var r = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in PayloadNames())
            {
                string name = n;
                r[n.Substring(PayloadPrefix.Length)] = () => ReadPayload(name);
            }
            if (r.Count == 0)
                foreach (var sub in new[] { "bin", "fake", "lists" })
                {
                    var dir = Path.Combine(SourceDir, sub);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string path = f;
                        r[sub + "/" + Path.GetFileName(f)] = () => File.ReadAllBytes(path);
                    }
                }
            return r;
        }

        public static bool NeedsInstall()
        {
            if (RunningFromRoot) return false;
            if (!PayloadFiles().ContainsKey("bin/winws.exe"))
                throw new Exception("В программе нет файлов zapret, и рядом нет папки bin. Скачай ZapretVN.exe из релиза на GitHub.");
            return true;
        }

        // Распаковать файлы zapret в dir. Списки не перезаписываются (там правки пользователя).
        // Возвращает true, если пришлось остановить службу из-за занятого файла.
        static bool Extract(string dir, ref bool wasRunning)
        {
            bool stopped = false;
            foreach (var kv in PayloadFiles())
            {
                string dst = Path.Combine(dir, kv.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                bool isList = kv.Key.StartsWith("lists/", StringComparison.OrdinalIgnoreCase);
                if (isList && File.Exists(dst)) continue;
                byte[] data = kv.Value();
                if (SameContent(data, dst)) continue;
                try { File.WriteAllBytes(dst, data); }
                catch (Exception)
                {
                    if (stopped) throw;
                    // файл занят (драйвер WinDivert загружен) — останавливаем и пробуем снова
                    wasRunning = Zapret.IsRunning();
                    Zapret.Stop(); Zapret.StopDriver(); stopped = true;
                    Thread.Sleep(500);
                    File.WriteAllBytes(dst, data);
                }
            }
            return stopped;
        }

        // для проверки сборки: ZapretVN.exe --extract <папка>
        public static void ExtractTo(string dir)
        {
            foreach (var kv in PayloadFiles())
            {
                string dst = Path.Combine(dir, kv.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, kv.Value());
            }
        }

        public static void Install()
        {
            bool firstInstall = !File.Exists(App.Exe);
            foreach (var d in new[] { App.Bin, App.Fake, App.Lists, App.Config }) Directory.CreateDirectory(d);
            bool wasRunning = false;
            bool stopped = Extract(App.Root, ref wasRunning);
            byte[] self = File.ReadAllBytes(Application.ExecutablePath);
            if (!SameContent(self, App.Exe)) File.WriteAllBytes(App.Exe, self);
            EnsureData();
            if (stopped && wasRunning) { try { Zapret.Start(); } catch { } }
            try
            {
                MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Zapret VN.lnk"));
                if (firstInstall) MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Zapret VN.lnk"));
            }
            catch { }
        }

        public static void EnsureData()
        {
            foreach (var d in new[] { App.Bin, App.Fake, App.Lists, App.Config }) Directory.CreateDirectory(d);
            if (!File.Exists(App.Winws)) { bool w = false; Extract(App.Root, ref w); }
            var utf8 = new UTF8Encoding(false);
            if (!File.Exists(App.ListMain)) File.WriteAllText(App.ListMain, Defaults.Main, utf8);
            if (!File.Exists(App.ListExclude)) File.WriteAllText(App.ListExclude, Defaults.Exclude, utf8);
            if (!File.Exists(App.ListAuto)) File.WriteAllText(App.ListAuto, "", utf8);
        }

        public static IEnumerable<string> ShortcutPaths()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Zapret VN.lnk");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Zapret VN.lnk");
        }

        static void MakeShortcut(string lnk)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            dynamic sc = sh.CreateShortcut(lnk);
            sc.TargetPath = App.Exe;
            sc.WorkingDirectory = App.Root;
            sc.Description = "Zapret VN — обход блокировок";
            sc.IconLocation = App.Exe + ",0";
            sc.Save();
        }

        static bool SameContent(byte[] data, string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != data.Length) return false;
                using (var h = SHA256.Create())
                using (var s = File.OpenRead(path))
                    return Convert.ToBase64String(h.ComputeHash(data)) == Convert.ToBase64String(h.ComputeHash(s));
            }
            catch { return false; }
        }
    }

    // ===================================================================== редактор списка доменов

    class ListEditor : UserControl
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public event EventHandler Changed;
        readonly string file;
        readonly ListBox lb;
        readonly TextBox tb;
        readonly FlowLayoutPanel side;
        readonly Label count;
        readonly List<string> header = new List<string>();

        public ListEditor(string file, string hint)
        {
            this.file = file;
            Dock = DockStyle.Fill;
            Padding = new Padding(8);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var h = new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 8) };
            t.SizeChanged += (s, e) => { h.MaximumSize = new Size(Math.Max(100, t.ClientSize.Width - 10), 0); };
            t.Controls.Add(h, 0, 0);
            t.SetColumnSpan(h, 2);
            lb = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false, Sorted = true };
            lb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            t.Controls.Add(lb, 0, 1);
            side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
            AddButton("Удалить выбранные", (s, e) => RemoveSelected());
            count = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(2, 4, 0, 0) };
            t.Controls.Add(side, 1, 1);
            tb = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddFromBox(); } };
            tb.HandleCreated += (s, e) => SendMessage(tb.Handle, 0x1501, (IntPtr)1, "домен, например example.com (можно несколько через пробел)");
            var bAdd = new Button { Text = "Добавить", AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
            bAdd.Click += (s, e) => AddFromBox();
            t.Controls.Add(tb, 0, 2);
            t.Controls.Add(bAdd, 1, 2);
            Controls.Add(t);
        }

        public Button AddButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(170, 0), Margin = new Padding(0, 0, 0, 6) };
            b.Click += click;
            side.Controls.Add(b);
            side.Controls.Remove(count);
            if (count != null) side.Controls.Add(count);
            return b;
        }

        public List<string> Items { get { return lb.Items.Cast<string>().ToList(); } }
        public string[] Selected { get { return lb.SelectedItems.Cast<string>().ToArray(); } }

        public static List<string> ReadDomains(string path)
        {
            var r = new List<string>();
            try
            {
                if (File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var l = raw.Trim();
                        if (l.Length == 0 || l[0] == '#' || l[0] == ';' || l[0] == '/') continue;
                        r.Add(l.ToLowerInvariant());
                    }
            }
            catch { }
            return r;
        }

        public void Reload()
        {
            header.Clear();
            var items = new List<string>();
            try
            {
                if (File.Exists(file))
                    foreach (var raw in File.ReadAllLines(file))
                    {
                        var l = raw.Trim();
                        if (l.Length == 0) continue;
                        if (l[0] == '#' || l[0] == ';') { if (items.Count == 0) header.Add(raw); continue; }
                        items.Add(l.ToLowerInvariant());
                    }
            }
            catch { }
            lb.BeginUpdate();
            lb.Items.Clear();
            foreach (var i in items.Distinct()) lb.Items.Add(i);
            lb.EndUpdate();
            UpdateCount();
        }

        void UpdateCount() { if (count != null) count.Text = "Всего: " + lb.Items.Count; }

        void AddFromBox()
        {
            var parts = tb.Text.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bad = parts.Where(p => Normalize(p) == null).ToList();
            AddDomains(parts);
            tb.Text = bad.Count > 0 ? string.Join(" ", bad) : "";
            if (bad.Count > 0) MessageBox.Show(FindForm(), "Не похоже на домен: " + string.Join(", ", bad), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public int AddDomains(IEnumerable<string> domains)
        {
            int n = 0;
            foreach (var d in domains)
            {
                var x = Normalize(d);
                if (x != null && !lb.Items.Contains(x)) { lb.Items.Add(x); n++; }
            }
            if (n > 0) Save();
            return n;
        }

        public void RemoveDomains(IEnumerable<string> domains)
        {
            bool any = false;
            foreach (var d in domains.ToList()) if (lb.Items.Contains(d)) { lb.Items.Remove(d); any = true; }
            if (any) Save();
        }

        void RemoveSelected() { RemoveDomains(Selected); }

        public void Clear() { lb.Items.Clear(); Save(); }

        void Save()
        {
            var lines = new List<string>(header);
            lines.AddRange(Items);
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            UpdateCount();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public static string Normalize(string s)
        {
            if (s == null) return null;
            s = s.Trim().ToLowerInvariant();
            int i = s.IndexOf("://");
            if (i >= 0) s = s.Substring(i + 3);
            foreach (var c in new[] { '/', '?', '#', ':' }) { i = s.IndexOf(c); if (i >= 0) s = s.Substring(0, i); }
            s = s.Trim('.');
            if (s.StartsWith("*.")) s = s.Substring(2);
            if (s.StartsWith("www.")) s = s.Substring(4);
            try { s = new IdnMapping().GetAscii(s); } catch { return null; }
            return Regex.IsMatch(s, "^([a-z0-9-]+\\.)+[a-z0-9-]{2,}$") && s.Length <= 253 ? s : null;
        }
    }

    // ===================================================================== главное окно

    class ScanResult
    {
        public string Category, Host, Domain, Isp, Https, Verdict;
        public bool Blocked, Maybe, InList, NotFound;
    }

    class MainForm : Form
    {
        readonly bool screenshotMode;
        bool allowVisible, exiting, busy, loadingUi, balloonShown;
        DateTime autoMtime;
        float scale = 1f;

        NotifyIcon tray;
        ToolStripMenuItem miOpen, miToggle, miCheck, miExit;
        PictureBox pic;
        Label lblState, lblSub, lblBusy;
        Button btnToggle, btnPick, btnScan, btnScanAdd, btnCheck;
        CheckBox chkAuto, chkDns;
        ComboBox cbStrategy;
        TabControl tabs;
        TabPage pageCheck;
        ListEditor edMain, edAuto, edExcl;
        ListView lvScan, lvCheck;
        TextBox txtLog;
        ProgressBar progress;
        System.Windows.Forms.Timer timer, applyTimer;
        Icon icoOn, icoOff, icoBusy;
        Bitmap bigOn, bigOff, bigBusy;
        EventWaitHandle showEvent;
        static readonly object logLock = new object();

        public MainForm(bool startHidden, bool screenshotMode)
        {
            this.screenshotMode = screenshotMode;
            allowVisible = !startHidden || !Zapret.ServiceExists();
            BuildUi();
            var h = Handle; // нужен для BeginInvoke до первого показа
            Init();
        }

        // ---------------------------------------------------------------- интерфейс

        int S(int v) { return (int)Math.Round(v * scale); }

        void BuildUi()
        {
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = App.Name;
            ClientSize = new Size(840, 620);
            MinimumSize = new Size(720, 540);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14, 12, 14, 8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // --- шапка: щит, статус, большая кнопка
            var head = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pic = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
            var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            lblState = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Margin = new Padding(0) };
            lblSub = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(500, 0), Margin = new Padding(2, 2, 0, 0) };
            texts.Controls.Add(lblState);
            texts.Controls.Add(lblSub);
            btnToggle = new Button { Text = "Включить", Size = new Size(160, 46), Font = new Font("Segoe UI Semibold", 11F), Anchor = AnchorStyles.Right, Margin = new Padding(8, 4, 0, 0) };
            btnToggle.Click += (s, e) => ToggleBypass();
            head.Controls.Add(pic, 0, 0);
            head.Controls.Add(texts, 1, 0);
            head.Controls.Add(btnToggle, 2, 0);
            root.Controls.Add(head, 0, 0);

            // --- настройки
            var opts = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            var row1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            chkAuto = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true, Margin = new Padding(2, 3, 28, 3) };
            chkDns = new CheckBox { Text = "Защищённый DNS (8.8.8.8 / 1.1.1.1 через HTTPS)", AutoSize = true, Margin = new Padding(2, 3, 3, 3) };
            row1.Controls.Add(chkAuto);
            row1.Controls.Add(chkDns);
            var row2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            var lblStr = new Label { Text = "Стратегия обхода:", AutoSize = true, Margin = new Padding(2, 7, 6, 0) };
            cbStrategy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 3, 8, 0) };
            btnPick = new Button { Text = "Подобрать автоматически", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            btnPick.Click += (s, e) => AutoPick();
            row2.Controls.Add(lblStr);
            row2.Controls.Add(cbStrategy);
            row2.Controls.Add(btnPick);
            opts.Controls.Add(row1, 0, 0);
            opts.Controls.Add(row2, 0, 1);
            root.Controls.Add(opts, 0, 1);

            // --- вкладки
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
            edMain = new ListEditor(App.ListMain, "Сайты, к которым всегда применяется обход. Поддомены включаются сами: steampowered.com покрывает store.steampowered.com, help.steampowered.com и т.д.");
            edAuto = new ListEditor(App.ListAuto, "Сайты, которые программа нашла заблокированными сама: если сайт 3 раза подряд сбросил соединение, он попадает сюда и дальше открывается через обход. Делать ничего не нужно.");
            edAuto.AddButton("Перенести в основной", (s, e) => { var sel = edAuto.Selected; edMain.AddDomains(sel); edAuto.RemoveDomains(sel); });
            edAuto.AddButton("Очистить список", (s, e) =>
            {
                if (MessageBox.Show(this, "Очистить автоматически найденные сайты?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) edAuto.Clear();
            });
            edExcl = new ListEditor(App.ListExclude, "К этим сайтам обход не применяется никогда, даже если они попадут в автоматический список. Добавь сюда сайт, если с включённым обходом он стал работать хуже.");
            AddTab("Сайты в обходе", edMain);
            AddTab("Найдено автоматически", edAuto);
            AddTab("Исключения", edExcl);

            // поиск блокировок
            var scan = Page(
                "Проверяет около 70 популярных сайтов, которые часто блокируют во Вьетнаме (игры, мессенджеры, торренты, книги, новости, 18+, ставки, VPN): " +
                "спрашивает DNS провайдера и пробует открыть каждый сайт напрямую. Найденные блокировки можно одним кликом добавить в обход.");
            btnScan = new Button { Text = "Найти заблокированные сайты", AutoSize = true, Margin = new Padding(0, 0, 8, 6) };
            btnScan.Click += (s, e) => RunScan();
            btnScanAdd = new Button { Text = "Добавить отмеченные в обход", AutoSize = true, Enabled = false, Margin = new Padding(0, 0, 8, 6) };
            btnScanAdd.Click += (s, e) => AddScanChecked();
            ((FlowLayoutPanel)scan.Tag).Controls.AddRange(new Control[] { btnScan, btnScanAdd });
            lvScan = MakeList(true, "Сайт", 150, "Категория", 105, "DNS провайдера", 140, "Напрямую по HTTPS", 185, "Итог", 165);
            ((TableLayoutPanel)scan.Controls[0]).Controls.Add(lvScan, 0, 2);
            tabs.TabPages.Add(scan);
            scan.Text = "Поиск блокировок";

            // проверка
            pageCheck = Page("Проверяет, открываются ли сайты прямо сейчас — так же, как их видят Steam, Roblox и браузер на этом компьютере.");
            btnCheck = new Button { Text = "Проверить сейчас", AutoSize = true, Margin = new Padding(0, 0, 8, 6) };
            btnCheck.Click += (s, e) => RunCheck();
            ((FlowLayoutPanel)pageCheck.Tag).Controls.Add(btnCheck);
            lvCheck = MakeList(false, "Что", 220, "Адрес", 240, "Результат", 260);
            ((TableLayoutPanel)pageCheck.Controls[0]).Controls.Add(lvCheck, 0, 2);
            tabs.TabPages.Add(pageCheck);
            pageCheck.Text = "Проверка";

            // журнал
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), BackColor = Color.White, BorderStyle = BorderStyle.None };
            var logPage = new TabPage("Журнал") { Padding = new Padding(8), UseVisualStyleBackColor = true };
            logPage.Controls.Add(txtLog);
            tabs.TabPages.Add(logPage);
            root.Controls.Add(tabs, 0, 2);

            // --- низ
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var lnkGit = new LinkLabel { Text = "GitHub: mrme0ws", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 3, 14, 0) };
            lnkGit.LinkClicked += (s, e) => App.OpenUrl(App.GitHubUrl);
            var lnkTg = new LinkLabel { Text = "Telegram: @mrmeows", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 3, 14, 0) };
            lnkTg.LinkClicked += (s, e) => App.OpenUrl(App.TelegramUrl);
            progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Size = new Size(110, 14), Visible = false, Anchor = AnchorStyles.Left, MarqueeAnimationSpeed = 30, Margin = new Padding(0, 4, 8, 0) };
            lblBusy = new Label { AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 0) };
            var lnkUninstall = new LinkLabel { Text = "Удалить программу", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 3, 0, 0), LinkColor = Color.Gray };
            lnkUninstall.LinkClicked += (s, e) => Uninstall();
            var lblVer = new Label { Text = "v" + App.Version + " · zapret v72.13", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Right, Margin = new Padding(14, 3, 0, 0) };
            bottom.Controls.Add(progress, 0, 0);
            bottom.Controls.Add(lblBusy, 1, 0);
            bottom.Controls.Add(lnkGit, 2, 0);
            bottom.Controls.Add(lnkTg, 3, 0);
            bottom.Controls.Add(lnkUninstall, 4, 0);
            bottom.Controls.Add(lblVer, 5, 0);
            root.Controls.Add(bottom, 0, 3);

            // --- трей
            var menu = new ContextMenuStrip();
            miOpen = new ToolStripMenuItem("Открыть Zapret VN", null, (s, e) => ShowMain());
            miOpen.Font = new Font(menu.Font, FontStyle.Bold);
            miToggle = new ToolStripMenuItem("Включить обход", null, (s, e) => ToggleBypass());
            miCheck = new ToolStripMenuItem("Проверить сайты", null, (s, e) => { ShowMain(); tabs.SelectedTab = pageCheck; RunCheck(); });
            miExit = new ToolStripMenuItem("Выход", null, (s, e) => ExitApp());
            var miAuthor = new ToolStripMenuItem("Автор");
            miAuthor.DropDownItems.Add("GitHub: mrme0ws", null, (s, e) => App.OpenUrl(App.GitHubUrl));
            miAuthor.DropDownItems.Add("Telegram: @mrmeows", null, (s, e) => App.OpenUrl(App.TelegramUrl));
            menu.Items.AddRange(new ToolStripItem[] { miOpen, new ToolStripSeparator(), miToggle, miCheck, new ToolStripSeparator(), miAuthor, miExit });
            tray = new NotifyIcon { ContextMenuStrip = menu, Text = App.Name, Visible = false };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

            timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (s, e) => { if (!busy) RefreshState(); };
            applyTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            applyTimer.Tick += (s, e) => ApplyListsNow();

            ResumeLayout(false);
            PerformLayout();
        }

        void AddTab(string title, Control c)
        {
            var p = new TabPage(title) { UseVisualStyleBackColor = true };
            p.Controls.Add(c);
            tabs.TabPages.Add(p);
        }

        // страница: подсказка / панель кнопок / место под список (строка 2)
        TabPage Page(string hint)
        {
            var p = new TabPage { UseVisualStyleBackColor = true, Padding = new Padding(8) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var h = new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 8) };
            t.SizeChanged += (s, e) => { h.MaximumSize = new Size(Math.Max(100, t.ClientSize.Width - 10), 0); };
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            t.Controls.Add(h, 0, 0);
            t.Controls.Add(buttons, 0, 1);
            p.Controls.Add(t);
            p.Tag = buttons;
            return p;
        }

        ListView MakeList(bool checkboxes, params object[] cols)
        {
            var lv = new ListView { View = View.Details, FullRowSelect = true, CheckBoxes = checkboxes, Dock = DockStyle.Fill, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            for (int i = 0; i < cols.Length; i += 2) lv.Columns.Add((string)cols[i], S((int)cols[i + 1]));
            return lv;
        }

        // ---------------------------------------------------------------- старт

        void Init()
        {
            int big = Math.Max(32, pic.ClientSize.Width);
            bigOn = Shield.Draw(big, Shield.On);
            bigOff = Shield.Draw(big, Shield.Off);
            bigBusy = Shield.Draw(big, Shield.Busy);
            int small = SystemInformation.SmallIconSize.Width;
            icoOn = Icon.FromHandle(Shield.Draw(small, Shield.On).GetHicon());
            icoOff = Icon.FromHandle(Shield.Draw(small, Shield.Off).GetHicon());
            icoBusy = Icon.FromHandle(Shield.Draw(small, Shield.Busy).GetHicon());

            loadingUi = true;
            foreach (var s in Strategies.All) cbStrategy.Items.Add(s);
            cbStrategy.SelectedItem = Strategies.Get(Settings.Get("strategy", "multidisorder"));
            chkAuto.Checked = AutoStartSetting();
            loadingUi = false;
            edMain.Reload(); edAuto.Reload(); edExcl.Reload();
            autoMtime = Mtime(App.ListAuto);

            chkAuto.CheckedChanged += (s, e) => { if (!loadingUi) SetAutoStart(chkAuto.Checked); };
            chkDns.CheckedChanged += (s, e) => { if (!loadingUi) SetDns(chkDns.Checked); };
            cbStrategy.SelectedIndexChanged += (s, e) => { if (!loadingUi) ChangeStrategy((Strategy)cbStrategy.SelectedItem); };
            edMain.Changed += (s, e) => ScheduleApply();
            edExcl.Changed += (s, e) => ScheduleApply();
            edAuto.Changed += (s, e) => { autoMtime = Mtime(App.ListAuto); ScheduleApply(); };

            RefreshState();
            if (screenshotMode)
            {
                loadingUi = true;
                try { chkDns.Checked = SecureDns.IsEnabled(); } catch { }
                loadingUi = false;
                return;
            }

            tray.Visible = true;
            timer.Start();
            Log("Zapret VN " + App.Version + " запущен");

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ZapretVN_Show");
            var th = new Thread(() => { while (true) { showEvent.WaitOne(); try { BeginInvoke((Action)ShowMain); } catch { } } });
            th.IsBackground = true;
            th.Start();

            // состояние DNS и автозапуска — в фоне
            Task.Run(() =>
            {
                bool dns = false;
                try { dns = SecureDns.IsEnabled(); } catch { }
                try
                {
                    bool auto = AutoStartSetting();
                    if (!Settings.Has("autostart")) Settings.Set("autostart", auto ? "1" : "0");
                    if (auto && !AutoStart.TaskExists()) { AutoStart.Enable(); Log("Автозапуск значка в трее включён"); }
                    if (Zapret.ServiceExists() && Zapret.ServiceAutoStart() != auto) Zapret.SetStartMode(auto);
                    if (Zapret.ServiceExists() && Zapret.IsOutdated())
                    {
                        bool was = Zapret.IsRunning();
                        Zapret.Stop();
                        Zapret.Configure(auto);
                        if (was) Zapret.Start();
                        Log("Параметры службы обновлены под новую версию программы");
                    }
                }
                catch (Exception ex) { Log("Автозапуск: " + ex.Message); }
                BeginInvoke((Action)(() => { loadingUi = true; chkDns.Checked = dns; loadingUi = false; }));
            });

            if (!Zapret.ServiceExists())
                Log("Обход ещё не настроен — нажми «Включить».");
        }

        static bool AutoStartSetting()
        {
            if (Settings.Has("autostart")) return Settings.Get("autostart", "1") == "1";
            return !Zapret.ServiceExists() || Zapret.ServiceAutoStart();
        }

        static DateTime Mtime(string f) { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } }

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible) value = false;
            base.SetVisibleCore(value);
        }

        void ShowMain()
        {
            allowVisible = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && !screenshotMode && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(4000, App.Name, "Программа продолжает работать в трее. Нажми на значок щита, чтобы открыть.", ToolTipIcon.Info);
                }
                return;
            }
            if (tray != null) tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- состояние

        void RefreshState()
        {
            bool exists = Zapret.ServiceExists();
            bool running = exists && Zapret.IsRunning();
            string strat = ((Strategy)cbStrategy.SelectedItem ?? Strategies.All[0]).Title;
            lblState.Text = running ? "Обход включён" : exists ? "Обход выключен" : "Обход не настроен";
            lblState.ForeColor = running ? Color.FromArgb(24, 128, 68) : Color.FromArgb(90, 90, 90);
            lblSub.Text = running
                ? "Steam, Roblox и сайты из списка открываются напрямую, без VPN.\nСтратегия: " + strat
                : exists
                    ? "Заблокированные сайты сейчас не открываются. Нажми «Включить»."
                    : "Нажми «Включить» — программа установит службу и всё настроит.";
            btnToggle.Text = running ? "Выключить" : "Включить";
            miToggle.Text = running ? "Выключить обход" : "Включить обход";
            if (!busy)
            {
                pic.Image = running ? bigOn : bigOff;
                tray.Icon = running ? icoOn : icoOff;
            }
            tray.Text = App.Name + " — " + (running ? "обход включён" : "обход выключен");

            var mt = Mtime(App.ListAuto);
            if (mt != autoMtime)
            {
                autoMtime = mt;
                var before = edAuto.Items;
                edAuto.Reload();
                var added = edAuto.Items.Except(before).ToList();
                if (added.Count > 0) Log("Автоматически добавлено в обход: " + string.Join(", ", added));
            }
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            progress.Visible = on;
            lblBusy.Text = on ? text : "";
            foreach (Control c in new Control[] { btnToggle, btnPick, cbStrategy, chkAuto, chkDns, btnScan, btnCheck }) c.Enabled = !on;
            miToggle.Enabled = !on; miCheck.Enabled = !on;
            if (on)
            {
                pic.Image = bigBusy;
                tray.Icon = icoBusy;
            }
        }

        void SetBusyText(string t)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetBusyText(t))); return; }
            lblBusy.Text = t;
        }

        void RunOp(string text, Action work, Action done)
        {
            if (busy) return;
            SetBusy(true, text);
            Task.Run(work).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                SetBusy(false, null);
                if (t.Exception != null)
                {
                    var ex = t.Exception.GetBaseException();
                    Log("Ошибка: " + ex.Message);
                    if (Visible) MessageBox.Show(this, ex.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else tray.ShowBalloonTip(5000, App.Name, ex.Message, ToolTipIcon.Warning);
                }
                else if (done != null) done();
                RefreshState();
            })));
        }

        void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (logLock)
            {
                try
                {
                    var fi = new FileInfo(App.LogFile);
                    if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete();
                    File.AppendAllText(App.LogFile, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
            if (txtLog == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => AppendLog(line))); return; }
            AppendLog(line);
        }

        void AppendLog(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }

        // ---------------------------------------------------------------- действия

        void ToggleBypass()
        {
            if (busy) return;
            if (Zapret.IsRunning())
            {
                RunOp("Выключаю...", () => { Zapret.Stop(); Log("Обход выключен"); }, null);
                return;
            }
            bool enableDns = false;
            if (!Zapret.ServiceExists() && !chkDns.Checked)
            {
                ShowMain();
                enableDns = MessageBox.Show(this,
                    "Включить заодно защищённый DNS (8.8.8.8 / 1.1.1.1 через HTTPS)?\n\n" +
                    "Провайдеры во Вьетнаме подменяют адреса заблокированных сайтов на 127.0.0.1. " +
                    "Без этого Steam и другие сайты могут не открываться даже с обходом. Рекомендуется.",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            bool auto = AutoStartSetting();
            RunOp("Включаю...", () =>
            {
                if (enableDns) Log("Защищённый DNS включён: " + SecureDns.Enable());
                Zapret.Configure(auto);
                Zapret.Start();
                Log("Обход включён (стратегия: " + Strategies.Get(Settings.Get("strategy", "multidisorder")).Title + ")");
            }, () =>
            {
                if (enableDns) { loadingUi = true; chkDns.Checked = true; loadingUi = false; }
            });
        }

        void SetAutoStart(bool on)
        {
            RunOp(on ? "Включаю автозапуск..." : "Выключаю автозапуск...", () =>
            {
                Settings.Set("autostart", on ? "1" : "0");
                if (on) AutoStart.Enable(); else AutoStart.Disable();
                Zapret.SetStartMode(on);
                Log(on ? "Автозапуск включён: обход и значок в трее стартуют вместе с Windows" : "Автозапуск выключен");
            }, null);
        }

        void SetDns(bool on)
        {
            if (!on && MessageBox.Show(this,
                    "Выключить защищённый DNS?\n\nDNS вернётся к провайдерскому, и сайты, заблокированные через DNS (в том числе Steam), снова могут перестать открываться.",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                loadingUi = true; chkDns.Checked = true; loadingUi = false;
                return;
            }
            RunOp(on ? "Включаю защищённый DNS..." : "Выключаю защищённый DNS...", () =>
            {
                if (on) Log("Защищённый DNS включён: " + SecureDns.Enable());
                else { SecureDns.Disable(); Log("Защищённый DNS выключен, DNS снова от провайдера"); }
            }, null);
        }

        void ChangeStrategy(Strategy s)
        {
            Settings.Set("strategy", s.Id);
            if (!Zapret.ServiceExists()) return;
            bool auto = AutoStartSetting();
            RunOp("Применяю стратегию...", () =>
            {
                bool was = Zapret.IsRunning();
                Zapret.Stop();
                Zapret.Configure(auto);
                if (was) Zapret.Start();
                Log("Стратегия: " + s.Title);
            }, null);
        }

        void ScheduleApply()
        {
            applyTimer.Stop();
            applyTimer.Start();
        }

        void ApplyListsNow()
        {
            applyTimer.Stop();
            if (busy) { applyTimer.Start(); return; }
            if (!Zapret.IsRunning()) { Log("Списки сохранены"); return; }
            RunOp("Применяю списки...", () => { Zapret.Stop(); Zapret.Start(); Log("Списки сохранены и применены"); }, null);
        }

        void AutoPick()
        {
            if (busy) return;
            if (MessageBox.Show(this,
                    "Программа по очереди попробует все стратегии и выберет ту, при которой открывается больше всего сайтов (Steam, Roblox, Telegram).\n\n" +
                    "Это займёт 1–3 минуты, обход на это время будет выключен. Начать?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Strategy best = null;
            bool auto = AutoStartSetting();
            RunOp("Подбираю стратегию...", () =>
            {
                var hosts = Checks.PickHosts;
                SetBusyText("Узнаю настоящие адреса сайтов...");
                var ips = NetTest.DohMany(hosts);
                Zapret.Stop();
                int max = hosts.Length * 2;
                var b = NetTest.Curl(hosts, ips, 1, 6);
                Log("Подбор стратегии. Без обхода открывается: " + b.Values.Count(v => v[0] == 0) + " из " + hosts.Length);
                int bestScore = -1;
                foreach (var s in Strategies.All)
                {
                    SetBusyText("Пробую: " + s.Title + "...");
                    Process p = null;
                    try
                    {
                        p = Process.Start(new ProcessStartInfo(App.Winws, Zapret.BuildArgs(s.Args)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = App.Root });
                        Thread.Sleep(1500);
                        if (p.HasExited) { Log("  " + s.Title + ": не запустилась"); continue; }
                        var r = NetTest.Curl(hosts, ips, 2, 6);
                        int score = r.Values.Sum(v => v.Count(c => c == 0));
                        Log("  " + s.Title + ": " + score + " из " + max);
                        if (score > bestScore) { bestScore = score; best = s; }
                        if (score == max) break;
                    }
                    finally
                    {
                        if (p != null && !p.HasExited) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                    }
                }
                if (best == null) throw new Exception("Ни одна стратегия не запустилась.");
                Settings.Set("strategy", best.Id);
                Zapret.Configure(auto);
                Zapret.Start();
                Log("Выбрана стратегия: " + best.Title + " (" + bestScore + " из " + max + "). Обход включён.");
            }, () =>
            {
                loadingUi = true; cbStrategy.SelectedItem = best; loadingUi = false;
                MessageBox.Show(this, "Готово! Выбрана стратегия: " + best.Title + ".\nПодробности — во вкладке «Журнал».", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        void RunCheck()
        {
            if (busy) return;
            var rows = Checks.Sites.Select(x => new[] { x[0], x[1] }).ToList();
            foreach (var d in edAuto.Items.Take(8)) rows.Add(new[] { "Найдено автоматически", d });
            Dictionary<string, List<int>> res = null;
            tabs.SelectedTab = pageCheck;
            RunOp("Проверяю сайты...", () =>
            {
                res = NetTest.Curl(rows.Select(r => r[1]).ToList(), null, 1, 8);
            }, () =>
            {
                lvCheck.BeginUpdate();
                lvCheck.Items.Clear();
                int ok = 0;
                foreach (var r in rows)
                {
                    int code = res[r[1]][0];
                    if (code == 0) ok++;
                    var it = new ListViewItem(new[] { r[0], r[1], (code == 0 ? "✔ " : "✖ ") + NetTest.Describe(code) });
                    it.ForeColor = code == 0 ? Color.FromArgb(24, 128, 68) : Color.FromArgb(190, 40, 40);
                    lvCheck.Items.Add(it);
                }
                lvCheck.EndUpdate();
                Log("Проверка: открывается " + ok + " из " + rows.Count);
            });
        }

        void RunScan()
        {
            if (busy) return;
            List<ScanResult> list = null;
            RunOp("Ищу блокировки...", () => { list = DoScan(); }, () =>
            {
                lvScan.BeginUpdate();
                lvScan.Items.Clear();
                foreach (var r in list.OrderBy(r => r.Blocked ? 0 : r.Maybe ? 1 : r.InList ? 2 : 3).ThenBy(r => r.Category))
                {
                    var it = new ListViewItem(new[] { r.Domain, r.Category, r.Isp, r.Https, r.Verdict }) { Tag = r };
                    it.Checked = r.Blocked && !r.InList;
                    it.ForeColor = r.Blocked ? Color.FromArgb(190, 40, 40) : r.Maybe ? Color.FromArgb(170, 110, 0) : r.InList ? Color.FromArgb(24, 128, 68) : Color.DimGray;
                    lvScan.Items.Add(it);
                }
                lvScan.EndUpdate();
                btnScanAdd.Enabled = true;
                int blocked = list.Count(r => r.Blocked && !r.InList);
                Log("Поиск блокировок: новых заблокированных " + blocked + ", уже в обходе " + list.Count(r => r.InList) + ", проверено " + list.Count);
            });
        }

        List<ScanResult> DoScan()
        {
            var list = Checks.Candidates.Select(c => { var p = c.Split('|'); return new ScanResult { Category = p[0], Host = p[1], Domain = p[2] }; }).ToList();
            var covered = ListEditor.ReadDomains(App.ListMain).Concat(ListEditor.ReadDomains(App.ListAuto)).ToList();
            var excluded = ListEditor.ReadDomains(App.ListExclude);
            var gw = NetTest.Gateway();
            SetBusyText("Спрашиваю DNS провайдера...");
            var isp = new ConcurrentDictionary<string, string>();
            Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = 16 }, r => { isp[r.Host] = gw == null ? null : NetTest.DnsQuery(gw, r.Host, 2500); });
            SetBusyText("Узнаю настоящие адреса (DNS через HTTPS)...");
            var ips = NetTest.DohMany(list.Select(r => r.Host));
            SetBusyText("Пробую открыть сайты напрямую...");
            var curl = NetTest.Curl(list.Select(r => r.Host).ToList(), ips, 2, 7);
            foreach (var r in list)
            {
                r.InList = covered.Any(d => r.Host == d || r.Host.EndsWith("." + d)) && !excluded.Any(d => r.Host == d || r.Host.EndsWith("." + d));
                string ip; ips.TryGetValue(r.Host, out ip);
                string ispIp; isp.TryGetValue(r.Host, out ispIp);
                var codes = curl[r.Host];
                bool dnsBlock = !string.IsNullOrEmpty(ispIp) && NetTest.IsBogus(ispIp);
                bool ok = codes.Any(c => c == 0);
                bool reset = !ok && codes.All(c => c == 35 || c == 56);
                bool timeout = !ok && codes.All(c => c == 28);
                r.Isp = gw == null ? "—" : ispIp == null ? "нет ответа" : ispIp == "" ? "пустой ответ" : dnsBlock ? ispIp + " (подмена)" : "нормальный";
                if (ip == null) { r.NotFound = true; r.Https = "—"; r.Verdict = "сайт не найден"; continue; }
                r.Https = ok ? (r.InList ? "открывается (через обход)" : "открывается") : NetTest.Describe(codes.First(c => c != 0));
                if (r.InList) r.Verdict = "уже в обходе";
                else if (dnsBlock || reset)
                {
                    r.Blocked = true;
                    r.Verdict = dnsBlock && reset ? "заблокирован (DNS + DPI)" : dnsBlock ? "заблокирован (DNS)" : "заблокирован (DPI)";
                }
                else if (timeout || (ispIp == null && !ok)) { r.Maybe = true; r.Verdict = "не отвечает (возможно блок)"; }
                else r.Verdict = ok ? "доступен" : "ошибка";
            }
            return list;
        }

        void AddScanChecked()
        {
            var domains = lvScan.CheckedItems.Cast<ListViewItem>().Select(i => ((ScanResult)i.Tag).Domain).Distinct().ToList();
            if (domains.Count == 0) { MessageBox.Show(this, "Ничего не отмечено.", App.Name); return; }
            int n = edMain.AddDomains(domains);
            Log("Добавлено в обход: " + string.Join(", ", domains) + (n < domains.Count ? " (часть уже была в списке)" : ""));
            foreach (ListViewItem i in lvScan.CheckedItems)
            {
                i.Checked = false;
                i.SubItems[4].Text = "добавлен в обход";
                i.ForeColor = Color.FromArgb(24, 128, 68);
            }
            tabs.SelectedIndex = 0;
        }

        void Uninstall()
        {
            if (busy) return;
            if (MessageBox.Show(this,
                    "Удалить Zapret VN полностью?\n\nБудут удалены служба обхода, автозапуск, ярлыки и папка " + App.Root + ".",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            bool resetDns = chkDns.Checked && MessageBox.Show(this,
                    "Вернуть DNS как было (от провайдера)?\n\nДа — DNS снова от провайдера.\nНет — оставить 8.8.8.8 / 1.1.1.1 через HTTPS (можно оставить, это безопасно).",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            RunOp("Удаляю...", () =>
            {
                Zapret.RemoveService();
                AutoStart.Disable();
                foreach (var l in Installer.ShortcutPaths()) try { if (File.Exists(l)) File.Delete(l); } catch { }
                if (resetDns) SecureDns.Disable();
            }, () =>
            {
                MessageBox.Show(this, "Zapret VN удалён.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + App.Root + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
                });
                exiting = true;
                tray.Visible = false;
                Application.Exit();
            });
        }

        void ExitApp()
        {
            if (busy) { MessageBox.Show(this, "Подожди, пока закончится текущая операция.", App.Name); return; }
            if (Zapret.IsRunning())
            {
                var r = MessageBox.Show(
                    "Обход сейчас включён.\n\nДа — оставить его работать в фоне (сайты продолжат открываться)\nНет — выключить обход и выйти",
                    App.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No) { try { Zapret.Stop(); Log("Обход выключен"); } catch (Exception ex) { Log("Ошибка: " + ex.Message); } }
            }
            exiting = true;
            tray.Visible = false;
            Application.Exit();
        }

        // ---------------------------------------------------------------- скриншоты для проверки вёрстки

        public void CaptureTabs(string dir)
        {
            Directory.CreateDirectory(dir);
            lvCheck.Items.Add(new ListViewItem(new[] { "Steam — магазин", "store.steampowered.com", "✔ открывается" }) { ForeColor = Color.FromArgb(24, 128, 68) });
            lvCheck.Items.Add(new ListViewItem(new[] { "Roblox — API", "apis.roblox.com", "✖ сброс при подключении (DPI)" }) { ForeColor = Color.FromArgb(190, 40, 40) });
            lvScan.Items.Add(new ListViewItem(new[] { "nyaa.si", "Торренты", "127.0.0.1 (подмена)", "сброс при подключении (DPI)", "заблокирован (DNS + DPI)" }) { Checked = true, ForeColor = Color.FromArgb(190, 40, 40) });
            lvScan.Items.Add(new ListViewItem(new[] { "steampowered.com", "Steam", "127.0.0.1 (подмена)", "открывается (через обход)", "уже в обходе" }) { ForeColor = Color.FromArgb(24, 128, 68) });
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Refresh();
                Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, "tab" + i + ".png"));
                }
            }
        }
    }

    // ===================================================================== точка входа

    static class Program
    {
        static Mutex mutex;

        static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            return i + 1 < args.Length ? args[i + 1] : "";
        }

        [STAThread]
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool tray = Arg(args, "--tray") != null;

            string extract = Arg(args, "--extract");
            if (!string.IsNullOrEmpty(extract)) { Installer.ExtractTo(extract); return; }

            string shot = Arg(args, "--screenshot");
            if (shot != null)
            {
                Settings.Load();
                var f = new MainForm(false, true);
                f.Shown += (s, e) => { f.CaptureTabs(shot); f.Close(); };
                Application.Run(f);
                return;
            }

            bool created;
            mutex = new Mutex(true, "Local\\ZapretVN_SingleInstance", out created);
            if (!created)
            {
                try { using (var ev = EventWaitHandle.OpenExisting("Local\\ZapretVN_Show")) ev.Set(); }
                catch { MessageBox.Show("Zapret VN уже запущен — значок щита в трее.", App.Name); }
                return;
            }

            try
            {
                if (Installer.NeedsInstall())
                {
                    Installer.Install();
                    mutex.ReleaseMutex();
                    mutex.Close();
                    mutex = null;
                    Process.Start(new ProcessStartInfo(App.Exe, tray ? "--tray" : "") { UseShellExecute = false, WorkingDirectory = App.Root });
                    return;
                }
                Installer.EnsureData();
                Settings.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось подготовить файлы программы:\n\n" + ex.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(tray, false));
            GC.KeepAlive(mutex);
        }
    }
}
