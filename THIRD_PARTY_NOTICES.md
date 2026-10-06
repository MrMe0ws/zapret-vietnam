# Third-party notices

Zapret VN bundles the following unmodified binaries from the official
[zapret v72.13 release](https://github.com/bol-van/zapret/releases/tag/v72.13)
(`binaries/windows-x86_64/` and `files/fake/`). SHA-256 hashes were verified against
the release's `sha256sum.txt`. In `ZapretVN.exe` these files are embedded as resources
and extracted to `C:\zapret-vietnam` at install time.

| File | Component | License | Source |
|---|---|---|---|
| `bin/winws.exe` | zapret (winws) © 2016-2024 bol-van | MIT | https://github.com/bol-van/zapret |
| `fake/*.bin` | zapret fake packet payloads | MIT | https://github.com/bol-van/zapret |
| `bin/WinDivert.dll`, `bin/WinDivert64.sys` | WinDivert © basil00 | LGPL-3.0 or GPL-2.0 (dual) | https://github.com/basil00/WinDivert |
| `bin/cygwin1.dll` | Cygwin runtime © Red Hat and contributors | LGPL-3.0 (with linking exception) | https://cygwin.com/git/?p=newlib-cygwin.git |

You may replace any of these files with your own build: put them into `bin/` / `fake/`
and rebuild with `src/build.ps1`, or replace them directly in `C:\zapret-vietnam\bin`.

## zapret — MIT License

```
MIT License

Copyright (c) 2016-2024 bol-van

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## WinDivert — LGPL-3.0 / GPL-2.0

WinDivert is dual-licensed under your choice of the GNU Lesser General Public
License (LGPL) Version 3 or the GNU General Public License (GPL) Version 2.
Full license texts: https://github.com/basil00/WinDivert/blob/master/LICENSE

## Cygwin — LGPL-3.0

The Cygwin DLL is licensed under the GNU Lesser General Public License v3.0
with the Cygwin linking exception. Full text and source:
https://cygwin.com/licensing.html
