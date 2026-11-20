# Running BiUpdateHelperSite on .NET 10

The site used to target .NET Framework 4.5.2 and ran on the Linux server under mono.  It now targets
.NET 10 and runs on the ordinary .NET runtime, so mono is no longer involved.

The structure follows the KVStore service, which is a .NET 10 service already running on the same
arm64 server: multi-targeted, with the Windows service plumbing compiled out of the Linux build, and
BPUtil's `AppInit` owning the service lifetime on both platforms.

## What changed in the project

* **The project multi-targets** `net10.0-windows7.0;net10.0`.  The Windows target framework produces
  `BiUpdateHelperSite.dll`; the cross-platform one produces `BiUpdateHelperSiteLinux.dll` with the
  `LINUX` constant defined, and that is what is deployed.
* **`MainSvc` only derives from `ServiceBase` in the Windows build.**

  ```csharp
  public partial class MainSvc
  #if !LINUX
      : ServiceBase
  #endif
  ```

  This is what fixes the Linux startup fault: a non-Windows publish gets the portable build of
  `System.ServiceProcess.ServiceController`, whose `ServiceBase` constructor throws
  `PlatformNotSupportedException`.  A runtime guard around `ServiceBase.Run` was not enough, because
  constructing the service was itself the throw.  With the base type compiled out, the Linux assembly
  does not reference that assembly at all.  `MainSvc.Designer.cs`, which overrides `ServiceBase`
  members, is excluded from the Linux build.
* **BPUtil's `AppInit.WindowsService<MainSvc>()` runs the service**, on both platforms.  The
  hand-rolled `Console.ReadLine` loop and `PosixSignalRegistration` signal handling are gone; AppInit
  does the same job and is the code path already proven in production by KVStore.
* **`System.ServiceProcess.ServiceController` is now a direct `PackageReference`** on the Windows
  target framework, instead of being relied on through BPUtil's own reference.
* **The native SQLite library comes from NuGet.**  `DB\SQLite.cs` is sqlite-net, which P/Invokes a
  library it calls `sqlite3`.  `DB\SqliteNative.cs` installs a `DllImportResolver` pointing that name
  at `e_sqlite3` from the `SQLitePCLRaw.lib.e_sqlite3` package, which ships a prebuilt arm64 Linux
  binary.  No SQL and no database code changed.  The committed `sqlite3.dll` and the 2016 x86-64
  `libsqlite3.so` are deleted.
* `WebServer.stopServer` interrupts the FTP backup thread instead of aborting it, because
  `Thread.Abort` is not implemented on modern .NET.
* **One capability was lost to .NET 10: the FTP backup server's certificate is no longer excused.**
  .NET 10 removed `ServicePointManager.ServerCertificateValidationCallback`, which is what BPUtil's
  `CertificateValidation` wrapped; BPUtil excludes that class from its `net10.0` target for the same
  reason.  `WebServer`'s `AllowBackupFtpServerCertificate` is compiled out to match.  Nothing is lost
  in practice: the only client it could apply to is the `WebClient` in `DoFtpBackupNow`, which speaks
  plaintext `ftp://` and is never offered a certificate.  A cert-tolerant FTPS backup would need a real
  FTP client library — separate work.
* **The net45 build was dropped.**  Keeping it would have meant conditional compilation around
  `ServiceBase`, two sets of native SQLite assets and two package styles.
* Warning suppressions are scoped: `SYSLIB0014` around the one `WebClient` use, and `CA2200` plus
  `SYSLIB0054` for `DB/SQLite.cs` alone via `BiUpdateHelperSite/.editorconfig`.  `Properties\AssemblyInfo.cs`
  is kept (so `%%VERSION%%` still comes from one file) and now carries
  `[assembly: SupportedOSPlatform("windows7.0")]` under `#if !LINUX`, which the SDK would have emitted
  itself if this project generated its assembly attributes.

Nothing about the `bi6_update_check` API, the JSON it returns, the `SiteSettings.cfg` format or the
database schema changed.

## Endpoints added since the mono build

Two pages for a person to read, both GET-only and both answering `405 Method Not Allowed` to a POST,
exactly as a GET of the JSON API does:

* **`GET /updates`** — the update information the `bi6_update_check` API answers with, rendered as a
  page.  Built fresh per request and served `Cache-Control: no-cache, no-store`.
* **`GET /changelog`** — Blue Iris's changelog, which Blue Iris publishes as a PDF, converted to HTML
  in this process by `ChangelogHtml/`.  `PdfPig` (Apache-2.0, entirely managed) walks the PDF's
  logical structure tree, so the page keeps the headings, lists, tables and links; each release is a
  collapsible `<details>` which defaults to open.  The page is served `Cache-Control: no-cache` with
  an `ETag` derived from the identity of the PDF, and a conditional request is answered `304`.
  "no-cache" means revalidate, not "do not store": the point of the page is that mobile browsers go
  on serving a stale copy of the PDF for days, so a stale copy of this page must not be served blind
  either.

  The PDF is fetched at most **once a minute** however many visitors arrive
  (`ChangelogHtmlCache.minSecondsBetweenFetches`, the one number to change), conditionally with
  `If-None-Match`, so an unchanged PDF costs a 304 and no body.  The URL carries a cache-busting
  query parameter which changes once per fetch window, because the origin sits behind Cloudflare with
  `max-age=14400` and Cloudflare's cache key includes the query string; without it a newly published
  changelog could be invisible for up to four hours.  A visitor never waits for Blue Iris's server
  unless there is nothing at all to serve yet, and if the conversion ever fails the page degrades in
  three steps: the last good HTML, then the PDF's text in a `<pre>`, then a page which only links to
  the PDF.

## !! The data directory moves !!

This is the one change that needs care at deploy time.  `AppInit` sets the working directory to
`Globals.WritableDirectoryBase`, which on Linux is `/usr/share/BiUpdateHelperSite/`.  `SiteSettings.cfg`,
`UsageDb.s3db` and `UsageDb-Archive.s3db` are opened by relative path, so **they must be moved there**;
otherwise the service will not find them and will create a fresh, empty database.

`Bi6UpdateCheckData/` already lives in that directory, and `AppInit` creates the directory and a
`Logs/` subdirectory inside it, so after this move everything the service reads and writes is in one
place — which also removes the old failure mode where the error log silently went nowhere because the
directory did not exist.

`ChangelogHtmlCache/` is created there too, the first time the `/changelog` page converts the PDF.
It holds `LastGood.html` (the converted page) and `LastGood.json` (which PDF it was converted from,
and the validators to revalidate that PDF with), both written to a temporary file and moved into
place.  Nothing needs to be migrated into it: it is rebuilt on demand, so deleting it costs one
conversion.  Copying it to a new deployment is still worth doing, because then the first visitor
after the move is served without waiting.

`Globals.InitializeCommonApplicationData("BiUpdateHelperSite")` is called before `AppInit`, so the
directory is named after the program and not after the assembly.  Without it the Linux build would use
`/usr/share/BiUpdateHelperSiteLinux/`.  The monthly log file is named after the assembly, so it is
`Logs/BiUpdateHelperSiteLinux_YYYY_MM.txt`.

## Build and publish

**Use the `dotnet` CLI (SDK 10.0.401), not Visual Studio 2022's MSBuild.**  VS2022 on this machine
resolves SDK 9.0.317, which cannot build `net10.0` and fails with NETSDK1045 — so loading this project
in VS2022 will now fail where previously only a solution-wide build did.  Options: update Visual
Studio, pin the SDK with a `global.json`, or build from the CLI.

```
dotnet build BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release
```

Publish **framework-dependent** for the server:

```
dotnet publish BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release -f net10.0 -r linux-arm64 --self-contained false
```

That is 10.6 MB / 42 files in `BiUpdateHelperSite/bin/Release/net10.0/linux-arm64/publish/`, with an
aarch64 `BiUpdateHelperSiteLinux` apphost, `libe_sqlite3.so` beside it, and `www/`.  (It was 5.2 MB /
35 files before `PdfPig`, which adds seven managed assemblies and 5.7 MB.)  There is **no `runtimes/`
folder** and `libe_sqlite3.so` is the only native file in the publish: `PdfPig` ships no native assets
at all, so it needs nothing installed on the server.  Dropping `-r` also works and produces a portable
publish, which carries every platform's native SQLite under `runtimes/`; the RID-specific publish is
smaller and is the recommended one.

Self-contained is not needed: Ubuntu 24.04 arm64 offers `dotnet-runtime-10.0` (10.0.15) from
noble-updates and noble-security, so

```
sudo apt install dotnet-runtime-10.0
```

gives a runtime that Ubuntu security-patches.  Note that the `dotnet` currently on the server
(10.0.12 under `/usr/share/dotnet`) is **not** apt-managed — `dpkg -S /usr/bin/dotnet` finds no owning
package — so it receives no updates; installing the apt package is worth doing.

## Remaining deployment steps on the server

These have **not** been done — the live server was not touched.

1. `sudo apt install dotnet-runtime-10.0`.
2. **Move the data, then replace the binaries, in that order.**  Doing it the other way round deletes
   files that were just copied in, because both the mono build and the .NET build contain `BPUtil.dll`
   and `Newtonsoft.Json.dll`:
   * `mkdir -p /usr/share/BiUpdateHelperSite`
   * stop the service, then move `SiteSettings.cfg`, `UsageDb.s3db` and `UsageDb-Archive.s3db` from
     `/var/biupdatehelpersite` into `/usr/share/BiUpdateHelperSite/` (back them up first)
   * clear the old build out of `/var/biupdatehelpersite` (or deploy to a fresh directory)
   * copy the publish output in
   * `chmod +x /var/biupdatehelpersite/BiUpdateHelperSiteLinux`
   * no `chown` is needed: the unit AppInit generates sets no `User=`, so the service runs as root
     and a directory created with `sudo mkdir` is already root-owned.  Ownership only matters if
     `User=` is added in the hardening drop-in, and then the files inside need it too
3. **Switch to systemd, not supervisor.**  The service now takes an `svc` argument, and AppInit's
   non-systemd Linux service mode loops on `Console.ReadLine()`, which with stdin redirected from
   `/dev/null` (the way supervisor runs it) returns immediately and spins.  systemd sets
   `INVOCATION_ID`, which AppInit detects and uses to wait for `SIGTERM` instead.  This is also what
   KVStore does on the same machine.

   The binary generates its own unit file:

   ```
   sudo /usr/bin/dotnet /var/biupdatehelpersite/BiUpdateHelperSiteLinux.dll install
   ```

   which writes `/etc/systemd/system/biupdatehelpersite.service` with
   `ExecStart=/usr/bin/dotnet "/var/biupdatehelpersite/BiUpdateHelperSiteLinux.dll" svc`.  KVStore adds
   a hardening drop-in at `<unit>.d/hardening.conf` (`NoNewPrivileges`, `PrivateTmp`,
   `ProtectSystem=strict`, `ProtectHome=tmpfs`, `BindReadOnlyPaths`, `ReadWritePaths`,
   `RestrictAddressFamilies`); doing the same here is worth it, and `ReadWritePaths` must include
   `/usr/share/BiUpdateHelperSite`.  `status`, `start`, `stop`, `restart` and `uninstall` are also
   accepted as arguments.

   `install` takes `ExecStart` from the path of the dll it is run from, and does nothing at all if
   the unit file already exists, so moving the deployment directory afterwards means `uninstall`
   and `install` again.  It constructs the service, so it writes a default `SiteSettings.cfg` if
   the data directory has none; it does not open the databases, which happens only when the
   service starts.

   If supervisor must be kept, the program line becomes
   `/usr/bin/dotnet /var/biupdatehelpersite/BiUpdateHelperSiteLinux.dll svc` **and** supervisor has to
   set `environment=INVOCATION_ID=supervisor`, or the process will spin on EOF stdin.  That is a hack;
   systemd is the better answer.
4. Remove the old supervisor program, `RobustStart.sh` and `/tmp/BiUpdateHelperSite.exe.lock`.
   supervisor existed partly to stop two mono processes racing, which systemd handles itself.
5. Check that nginx still reaches the site and that `POST /bi6_update_check` answers
   `"result":"success"`.

## Verified locally

On .NET 10.0.12, against copies of the real `UsageDb.s3db` (2697 records) and `UsageDb-Archive.s3db`:

* Both target frameworks build with **0 warnings**.
* **The Linux build was run, and served.**  Because the Linux target framework is an ordinary
  platform-neutral console build, it runs on Windows too.  A harness reproduced what AppInit does on
  Linux — AppInit's own prologue (data directory, `Logs/`, error file path, working directory,
  `Logger.CatchAll`), then `new MainSvc()`, then `OnStart` / `OnStop` by reflection exactly as
  `AppInit` calls them.  Results:
  * `MainSvc`'s base type in that build is `System.Object`, and the assembly's references are
    `BPUtil, Newtonsoft.Json` and framework assemblies — **no `System.ServiceProcess` of any kind.**
  * `new MainSvc()` succeeded, `OnStart` bound the port, the site served, and `OnStop` stopped it.
  * For contrast, the exact `System.ServiceProcess.ServiceController.dll` a non-Windows publish ships
    does throw `System.PlatformNotSupportedException` when a `ServiceBase` subclass is constructed
    against it — that was the fault, and the type is no longer in the hierarchy.
* Against that Linux build: `/` redirects to `default.html`; `default.html` returns 200;
  `POST api/getUsageRecords` returns 2697 records with `result: success`; `GET` on an API path returns
  405; gzip still applies; `POST bi6_update_check` with `{"cmd":"updatecheck"}` returns the documented
  JSON; `OPTIONS bi6_update_check` returns all four CORS headers.
* Both database files were **byte-for-byte unchanged** after the Linux build opened them, ran its
  `CreateTable` calls and read every row.
* The `SqliteNative` import resolver works through the `runtimes/<rid>/native/` layout of a portable
  publish (on Windows it loaded `runtimes/win-x64/native/e_sqlite3.dll`).
* The `linux-arm64` publish contains an aarch64 apphost and an aarch64 `libe_sqlite3.so`, and no `.a`
  static library.
* Also against the Linux build: `POST api/uploadUsageRecord2` inserted a record with its camera and
  GPU (2697 records to 2698) and the record read back correctly through `?detailsForId=`, with enums
  still serialized as strings; a payload with a wrong MD5 was rejected with 400.  `AppInit`'s
  `Logs/` directory was created and stayed empty, so nothing was logged as an error.
* Feeding `UpdateChecker`'s own documented sample response through `ParseUpdateCheckResponse`
  produces exactly the documented success JSON.  That was measured on the `net8.0` build; neither
  `Bi6UpdateCheck` nor the JSON types have changed since.
* **`/changelog`, against the real PDF**, through the same harness: a cold cache fetched and converted
  in 1.6 s and answered 200 with 131 `<h2>`, 473 `<li>`, 2 tables and no `data-pdf-tag`; five rapid
  requests after it were answered from memory in 1–3 ms, byte-for-byte identical and with no outbound
  request; a request carrying the served `ETag` was answered `304` with no body; a stale `ETag` got
  the page; `POST /changelog` answered 405; and `/updates` links to it while keeping its own
  cache-busting `?t=` on the PDF link.
* **The once-a-minute floor**: 4,816 calls to the page over 150 seconds produced exactly 3 requests to
  Blue Iris, at 0.6 s, 61.1 s and 121.8 s.  Each was conditional and answered `304`, so nothing was
  downloaded or converted again.
* **The conversion itself**: 68,725 of the PDF's 68,741 glyphs emitted (the 16 are bullet characters
  a CSS list marker draws instead), 54 pages in 607 ms with a 109 MB peak working set, 113 KB of HTML.
* **The degraded tiers**: a valid but untagged 30-page PDF produced the text tier (99 KB, with its
  explanatory notice); 20 KB of garbage with a PDF header produced no page at all, which is what makes
  the caller keep serving the last good one; and the link-only page renders at 4.7 KB.
* Both target frameworks still build with **0 warnings** with `PdfPig` referenced, and the
  `linux-arm64` publish gained no `runtimes/` folder and no native assets.

Not verified:

* **Anything architecture- or OS-specific.**  The Linux build was run on Windows, so the arm64
  `libe_sqlite3.so` actually loading, glibc compatibility, and Linux file paths and permissions are
  untested.  This machine has neither WSL nor a container runtime, and the only arm64 machine available
  is the owner's live server.
* The systemd unit AppInit generates, and the data-directory move.  Both need the server.
* A real successful update check against `blueirissoftware.com`, which needs the private Blue Iris
  registration and PC codes.
* The weekly FTP database backup.  `WebRequest.Create("ftp://...")` still returns an `FtpWebRequest` on
  .NET 10, so the client survives, but the `ftpes://` scheme documented for `backupFtpPath` was never
  understood by `WebClient` on .NET Framework either.
* The Windows build's service-manager behaviour.  AppInit shows a WinForms service manager when run
  interactively on Windows, which replaces the old "type exit to close" console; the Windows build
  compiles, but that window was not driven.
