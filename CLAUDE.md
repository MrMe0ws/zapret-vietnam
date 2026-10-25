# CLAUDE.md — guide for AI agents working on Zapret VN

Zapret VN is a Windows tray app that bypasses DPI censorship in Vietnam (Steam, Roblox, torrent
trackers, etc.) by driving [zapret](https://github.com/bol-van/zapret)'s `winws.exe` as a Windows
service, plus enabling DNS-over-HTTPS. UI language is **Russian** (the author and users are
Russian speakers living in Vietnam). Keep user-facing strings in Russian; code comments are Russian too.

Author: Meows — GitHub `MrMe0ws`, Telegram `@mrmeows`. Repo: https://github.com/MrMe0ws/zapret-vietnam

## Layout

```
src/ZapretVN.cs      the whole app (single file, ~1800 lines, see "Code map")
src/Shield.cs        draws the shield/"Z" icon (tray, window, exe icon)
src/IconMaker.cs     build-time tool that produced src/app.ico (not part of the app)
src/app.manifest     requireAdministrator + dpiAware
src/app.ico          exe icon (DIB frames 16..128 + PNG 256)
src/build.ps1        build script -> dist/ZapretVN.exe (build.cmd is a wrapper)
bin/                 zapret v72.13 windows-x86_64 binaries (winws.exe, WinDivert*, cygwin1.dll)
fake/                zapret fake TLS/QUIC payloads used by strategies
lists/               default hostlists shipped with the app (vietnam.txt, exclude.txt)
docs/                README screenshots
dist/                build output (git-ignored)
```

`bin/`, `fake/`, `lists/` are **embedded into the exe** as manifest resources named
`payload/<dir>/<file>`; the release is a single self-contained `ZapretVN.exe`.

## Build & test

Toolchain: only `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (.NET Framework 4.8,
present on every Win10/11). **This compiler is C# 5** — do NOT use string interpolation `$""`,
`?.`, `nameof`, expression-bodied members, `out var`, tuples, exception filters, `using static`.
`async/await` is allowed but the code uses `Task.Run(...).ContinueWith(...)`; keep that style.

```powershell
powershell -ExecutionPolicy Bypass -File src\build.ps1          # dist\ZapretVN.exe
powershell -ExecutionPolicy Bypass -File src\build.ps1 -Test    # + dist\test\ZapretVN_test.exe (asInvoker)
```

The real exe requires admin (UAC prompt) — you can't drive it non-interactively. Use the
**test build** (same code, `asInvoker` manifest) with hidden CLI flags:

- `ZapretVN_test.exe --screenshot <dir>` — renders every tab via `DrawToBitmap` into `<dir>\tabN.png`
  and exits. Read the PNGs to verify layout. Admin-only actions are not executed.
- `ZapretVN_test.exe --extract <dir>` — dumps embedded payload; compare hashes with `bin/` etc.

Note: `Start-Process -Wait` in Windows PowerShell 5.1 waits for the whole process tree — don't use it
to launch the GUI app or the call will hang.

Gotchas seen while building:
- csc fails with CS1619 on very long temp/output paths → build.ps1 compiles in `%TEMP%\zvn-build`.
- PowerShell 5.1 reads BOM-less `.ps1` as ANSI → Cyrillic breaks. Keep `.ps1`/`.cmd` ASCII, or save with BOM.
- Hostlists must be UTF-8 **without BOM** (winws would treat the BOM as part of the first domain).

## Runtime model

- `C:\zapret-vietnam` (`App.Root`) is the install dir: `ZapretVN.exe`, `bin\`, `fake\`, `lists\`,
  `config\settings.ini`, `zapret-vn.log`. Fixed ASCII path on purpose: Desktop is often OneDrive-synced
  and contains spaces/Cyrillic, which is unreliable for a boot-time service.
- Launching the exe from anywhere else = install/update: `Installer.Install()` extracts payload
  (binaries overwritten only if changed; **lists never overwritten**), copies itself, creates
  Start-menu (+ Desktop on first install) shortcuts, relaunches from `App.Root`.
- Bypass = Windows service **`zapret-vn`** whose `binPath` is `winws.exe <args>`
  (winws natively supports running as a service). Created/updated with `sc.exe`
  (`Zapret.Configure`). On startup the app compares the service ImagePath with the args it would
  generate (`Zapret.IsOutdated`) and reconfigures — this is how updates propagate.
- Tray autostart = Task Scheduler task **`ZapretVN`** (logon trigger, HighestAvailable, no battery
  limits, no time limit) running `ZapretVN.exe --tray`. Imported from XML via `schtasks /XML`.
  "Autostart" checkbox toggles both this task and the service start type (auto/demand).
- Secure DNS = PowerShell `Set-DnsClientDohServerAddress -AutoUpgrade $true -AllowFallbackToUdp $false`
  for 8.8.8.8/8.8.4.4/1.1.1.1/1.0.0.1 + static DNS on physical Up adapters.
- Single instance: mutex `Local\ZapretVN_SingleInstance`; a second launch signals event
  `Local\ZapretVN_Show` so the running instance shows its window.

### winws arguments (`Zapret.BuildArgs`)

```
--wf-tcp=80,443 --wf-udp=443
--filter-tcp=80,443 --hostlist=lists\vietnam.txt --hostlist-exclude=lists\exclude.txt
  --hostlist-auto=lists\autohostlist.txt --hostlist-auto-fail-threshold=3  <TCP strategy>
--new --filter-udp=443 --hostlist=lists\vietnam.txt --hostlist-exclude=lists\exclude.txt
  --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=fake\quic_initial_www_google_com.bin
```

TCP strategies live in `Strategies.All` (order = priority for auto-pick). `{FAKE}` is replaced
with the fake dir. Default `multidisorder --dpi-desync-split-pos=1,midsld`.

**Important:** `winws --dry-run` refuses to run while another winws with the same filter is running
("A copy of winws is already running with the same filter"). `Zapret.Configure` therefore requires
the service to be **stopped** — always `Stop() → Configure() → Start()`.

## Code map (src/ZapretVN.cs)

| Class | Role |
|---|---|
| `App` | constants: paths, service/task names, author URLs, `OpenUrl` (via explorer.exe so the browser isn't elevated) |
| `Defaults` | fallback contents for lists |
| `Strategy`, `Strategies` | TCP desync presets |
| `Checks` | hosts for the Check tab, auto-pick test hosts, ~70 scan candidates (`category|testHost|domain`) |
| `Settings` | `config\settings.ini` key=value (`strategy`, `autostart`) |
| `Shell` | run processes / PowerShell (`-EncodedCommand`), OEM encoding for sc/schtasks |
| `Zapret` | service lifecycle: Configure / Start / Stop / IsOutdated / RemoveService |
| `AutoStart` | scheduled task create/delete/query |
| `SecureDns` | enable/disable/query DoH |
| `NetTest` | DoH resolve (dns.google → cloudflare fallback), parallel `curl.exe -Z` probes, raw UDP DNS query to the gateway, exit-code descriptions |
| `Installer` | payload extraction, install/update, shortcuts |
| `ListEditor` | UserControl editing one hostlist file (normalizes domains, keeps header comments) |
| `MainForm` | UI, tray, all user actions (`RunOp` runs work on a background task with a busy state) |
| `Program` | entry point, CLI flags `--tray`, `--screenshot`, `--extract` |

## Findings about Vietnamese censorship (FPT Telecom, Oct 2026)

- ISP DNS answers `127.0.0.1` for blocked domains and sometimes drops plain UDP DNS queries → DoH needed.
- DPI resets TLS by **SNI** (same IP works with a different SNI). Steam store/community/help/checkout,
  `*.fastly.steamstatic.com`, steam-chat, nyaa.si, 1337x.to, bet365, pornhub… are blocked.
- `multisplit`/`multidisorder` at `1,midsld` defeat it; `fake` with `badseq` alone and `hostfakesplit` don't.
- Roblox client APIs (`apis/gamejoin/auth/realtime-signalr.roblox.com`) work with the bypass.
  `www.roblox.com` HTML pages (`/`, `/home`, `/login`, `/users/<id>/profile`) hang even with a valid TLS
  session and genuine certificate while `/robots.txt`, `/discover` and the APIs work. This is a
  **Roblox-side geoblock of Vietnamese IPs** (Roblox operates in VN via VNG since 2026-07): check-host.net
  loaded the page from ~40 countries and failed only from the Vietnam node; the same profile page loads
  instantly through a UK VPN exit. Not fixable by DPI bypass — needs a non-VN IP (VPN/proxy).
- A full-tunnel VPN (Wintun TUN, e.g. "incy") coexists with the running service — verified working
  with both on. Side effect seen: while the VPN was broken/connecting, every connection failed and
  autohostlist filled up with Steam download CDN hosts; `steamcontent.com` / `steampipe.akamaized.net`
  are now in the default exclude list for that reason.
- **Telegram is IP-blocked** on FPT (seen 2026-10-26): the whole `149.154.167.0/24` (DC2 `.51`, DC4 `.91`,
  web.telegram.org `.99`) gets no SYN-ACK on any port, on PC and phone alike. DC1 `149.154.175.53`,
  DC3 `149.154.175.100`, DC5 `91.108.56.130` are reachable. Not fixable by DPI bypass — needs an
  MTProxy/SOCKS5 proxy in Telegram or a VPN. The Check tab probes DCs by TCP (`Checks.TelegramDcs`)
  and reports "заблокирован по IP". Telegram's HTTP transport sends `Host: <IP>`, so winws used to
  put bare IPs into autohostlist; the app now strips them (`ListEditor.StripIps`).
- curl (Schannel) and Chromium ClientHellos differ; verify strategies with a real Chromium
  (Steam's CEF) when possible, e.g. headless Edge `--dump-dom` run as a **non-admin** process
  (Edge refuses to run elevated).

## Release process

1. Bump `App.Version` and `AssemblyVersion`/`AssemblyFileVersion` in `src/ZapretVN.cs`; update `CHANGELOG.md`.
2. `src\build.ps1` → note the printed SHA-256.
3. Commit, tag `vX.Y.Z`, `gh release create vX.Y.Z dist/ZapretVN.exe --title "Zapret VN X.Y.Z" --notes ...`
   (include the SHA-256 and the SmartScreen note in the notes).

Updating zapret: download the new official release, verify hashes against its `sha256sum.txt`,
replace `bin/` + `fake/`, update the version in `THIRD_PARTY_NOTICES.md` and the footer label
(`"zapret v72.13"` in `MainForm.BuildUi`), rebuild, re-test strategies.
