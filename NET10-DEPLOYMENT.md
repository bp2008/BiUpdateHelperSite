# Running BiUpdateHelperSite on .NET 10

The site used to target .NET Framework 4.5.2 and ran on the Linux server under mono.  It now targets
`net10.0` and runs on the ordinary .NET runtime, so mono is no longer involved and the service no longer
depends on whatever mono version the distribution happens to ship.

.NET 10 rather than .NET 8 because it is the LTS release which is supported the longest, and BPUtil
already multi-targets `net10.0` and `net10.0-windows`, so nothing in BPUtil had to change.

## What changed in the project

* `BiUpdateHelperSite.csproj` is an SDK-style project targeting `net10.0`.  `packages.config` is gone;
  Newtonsoft.Json 13.0.4 is a `PackageReference`.  `App.config` is gone, because .NET does not read it.
  The `www` folder is copied to the output directory by a `Content` item instead of by a post-build
  `xcopy`.
* `Properties\AssemblyInfo.cs` is kept (`GenerateAssemblyInfo` is `false`), so the assembly version which
  `default.html` substitutes into `%%VERSION%%` still comes from that one file.
* **The native SQLite library now comes from NuGet.**  `DB\SQLite.cs` is sqlite-net, which P/Invokes a
  library it calls `sqlite3`.  `DB\SqliteNative.cs` installs a `DllImportResolver` which points that name
  at `e_sqlite3`, the library from the `SQLitePCLRaw.lib.e_sqlite3` package.  That package carries a
  prebuilt binary for every runtime identifier, `linux-arm64` included, so the committed `sqlite3.dll`
  (Windows) and `libsqlite3.so` (a 2016 x86-64 build) have been deleted.  No SQL and no database code
  changed: the resolver only translates a file name.
* **The service logic no longer lives in a `ServiceBase` subclass.**  It is in `SiteService`, a class with
  no base type.  `MainSvc` is now a thin `ServiceBase` wrapper which delegates to it, and the one call
  which needs it, `ServiceBase.Run`, lives in `WindowsServiceHost`.  This matters off Windows: a
  non-Windows publish gets the portable build of `System.ServiceProcess.ServiceController`, whose
  `ServiceBase` constructor throws `PlatformNotSupportedException`.  Guarding `ServiceBase.Run` alone was
  not enough, because the non-Windows branch still constructed a `MainSvc`, and construction is what
  throws.  Keeping every mention of `ServiceBase` inside types which only Windows ever calls means the
  assembly is not even loaded anywhere else.  `WindowsServiceHost.Run` is marked `NoInlining` on purpose:
  were it inlined into its caller, compiling the caller would resolve those references again.
* `MainStatic.cs` only hands off to the Windows service control manager on Windows, and when standard
  input cannot be read from — which is the case when supervisor starts the process — it waits for
  `SIGTERM`/`SIGINT` instead of reading a console it does not have.  On Windows, interactively and as a
  Windows service, the behaviour is what it always was.
* `WebServer.stopServer` interrupts the FTP backup thread instead of aborting it, because `Thread.Abort`
  is not implemented on .NET Core and later.
* **One capability was lost to .NET 10: the FTP backup server's certificate is no longer excused.**
  .NET 10 removed `ServicePointManager.ServerCertificateValidationCallback`, which is what BPUtil's
  `CertificateValidation` wrapped; BPUtil excludes that class from its `net10.0` target for exactly that
  reason.  `WebServer`'s `AllowBackupFtpServerCertificate` is therefore compiled out under
  `#if !NET10_0_OR_GREATER`.  Nothing is lost in practice: the only outgoing client the callback could
  have applied to is the `WebClient` used by `DoFtpBackupNow`, which speaks plaintext `ftp://` and so is
  never presented with a certificate at all.  Everything else goes through BPUtil's `WebRequestUtility`,
  which is built on `HttpClient` and was never covered by that callback either.  If a cert-tolerant FTPS
  backup is ever actually wanted, it needs a real FTP client library; that is separate work.
* **The net45 build was dropped.**  Keeping it would have meant conditional compilation around
  `ServiceBase`, two sets of native SQLite assets and two package styles, for a target the deployment is
  moving away from.  If a .NET Framework build is ever wanted again, this is the commit to look at.
* Warning suppressions are scoped rather than project-wide.  `SYSLIB0014` is disabled around the one use
  of `WebClient` in `WebServer.cs`, and `BiUpdateHelperSite/.editorconfig` turns off `CA2200` and
  `SYSLIB0054` for `DB/SQLite.cs` alone, which is third-party code kept as received.  `CA1416` needs no
  suppression any more: the Windows-only types carry `[SupportedOSPlatform("windows")]`, so the analyzer
  is satisfied and will complain if that boundary is ever crossed again.

Nothing about the HTTP endpoints, the JSON they return, the `SiteSettings.cfg` format or the database
schema changed.

## Build and publish

**Build with the `dotnet` CLI, not Visual Studio 2022's MSBuild.**  This machine has two SDKs: VS2022
uses 9.0.317, which cannot build `net10.0` and fails with NETSDK1045, while the `dotnet` CLI resolves
10.0.401, which builds it.  Build the site project on its own:

```
dotnet build BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release
```

For the arm64 server, publish **self-contained**.  The server is settling on Ubuntu 24.04 (noble), whose
archive has no .NET 10 runtime, and a self-contained publish carries its own runtime, so nothing has to
be installed on the server at all:

```
dotnet publish BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release -r linux-arm64 --self-contained true
```

That produces about 91 MB in `BiUpdateHelperSite/bin/Release/net10.0/linux-arm64/publish/`, with a native
`BiUpdateHelperSite` apphost, `libe_sqlite3.so` (both aarch64 ELF), and the `www` folder.  It needs
OpenSSL and ICU on the host, both of which a normal Ubuntu install has.

Framework-dependent is the smaller option (about 4 MB), if a .NET 10 runtime is installed from
Microsoft's package feed rather than Ubuntu's:

```
dotnet publish BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release -r linux-arm64 --self-contained false
```

## Remaining deployment steps on the server

These have **not** been done — the live server was not touched.

1. Nothing to install for a self-contained publish.  Framework-dependent instead needs a .NET 10 runtime,
   which on noble means Microsoft's package feed, not the Ubuntu archive.
2. Copy the publish output into `/var/biupdatehelpersite`, keeping the existing `SiteSettings.cfg`,
   `UsageDb.s3db`, `UsageDb-Archive.s3db` and `Bi6UpdateCheckData/` in place.  Delete the old
   `BiUpdateHelperSite.exe`, the other `*.exe`/`*.dll` from the mono build, and `libsqlite3.so`.
3. `chmod +x /var/biupdatehelpersite/BiUpdateHelperSite` (the apphost).
4. Change the supervisor program from the mono invocation to the .NET one.  `RobustStart.sh` and the
   `/tmp/BiUpdateHelperSite.exe.lock` lock file exist to keep two mono processes from running at once;
   supervisor already guarantees that on its own, so the wrapper can go away:

   ```ini
   [program:biupdatehelpersite]
   command=/var/biupdatehelpersite/BiUpdateHelperSite
   directory=/var/biupdatehelpersite
   autostart=true
   autorestart=true
   stopsignal=TERM
   stopwaitsecs=15
   stdout_logfile=/var/log/biupdatehelpersite.log
   redirect_stderr=true
   ```

   Framework-dependent instead:
   `command=/usr/bin/dotnet /var/biupdatehelpersite/BiUpdateHelperSite.dll`

   `directory=` matters: `SiteSettings.cfg`, `UsageDb.s3db` and `UsageDb-Archive.s3db` are all resolved
   relative to the working directory, as they were before.

   The service takes no command-line arguments.  With stdin redirected the way supervisor redirects it,
   it logs `Standard input is unavailable.  Running until SIGTERM or SIGINT is received.` and then serves
   until supervisor's `stop` sends `SIGTERM`, which it answers by stopping the web server and exiting.
5. `supervisorctl reread && supervisorctl update && supervisorctl restart biupdatehelpersite`, then check
   that nginx still reaches the site and that `POST /bi6_update_check` answers with `"result":"success"`.
6. **Check that the writable directory is writable.**  `Globals.WritableDirectoryBase` is
   `CommonApplicationData` plus the program name, which on .NET for Linux is
   `/usr/share/BiUpdateHelperSite/`.  That is where the error log and `bi6_update_check`'s cached
   response and response archive live.  BPUtil's logger silently gives up if it cannot write there, so if
   that directory does not exist or is not writable by the service user, errors and the cached update
   response are lost without any sign of it.  `mkdir -p /usr/share/BiUpdateHelperSite && chown` it to the
   service user, or set a different path.  This is not new — it was the same under mono — but it is
   worth confirming now that `bi6_update_check` depends on it.

## Verified locally

On .NET 10.0.12, against copies of the real `UsageDb.s3db` (2697 records) and `UsageDb-Archive.s3db`:

* The project builds and publishes with no warnings, and the self-contained `linux-arm64` publish
  contains an aarch64 apphost and an aarch64 `libe_sqlite3.so`, and no `.a` static library.
* **The Linux startup path no longer reaches the `ServiceBase` constructor.**  Two measurements:
  * The exact `System.ServiceProcess.ServiceController.dll` which the `linux-arm64` publish ships (sha256
    `ac8650de...`) was loaded on its own and a `ServiceBase` subclass constructed against it.  It threw
    `System.PlatformNotSupportedException`, which is what the non-Windows publish would have done.
  * The published application was then run under a `DOTNET_STARTUP_HOOKS` hook which records every
    assembly the process loads.  Taking the branch Linux takes, the fixed build loads 41 assemblies and
    `System.ServiceProcess.ServiceController` is not among them; the same build with only the startup
    split reverted does load it.  A constructor in an assembly which is never loaded cannot throw.
* The service starts, binds its port and serves `www` (`/` redirects to `default.html`, static files and
  `%%VERSION%%`/`%%RND%%`/`%%ADMIN%%` substitution all work).
* `POST api/getUsageRecords` returns the full list (2697 records), `?detailsForId=` returns the record
  with its 41 cameras and 1 GPU, enums still serialize as strings, and gzip compression still applies.
* `POST api/uploadUsageRecord2` inserts a record with its camera and GPU, and the record reads back
  correctly (2697 records to 2698); a payload with a wrong MD5 is still rejected with 400.
* Both database files were **byte-for-byte unchanged** after the service opened them, ran its
  `CreateTable` calls and read every row — the schema sqlite-net infers still matches what is on disk.
* `POST bi6_update_check` with `{"cmd":"updatecheck"}` returns the documented JSON, and feeding the
  sample response from `UpdateChecker`'s own documentation through `ParseUpdateCheckResponse` produces
  exactly the documented success shape (news line, `changelog_url`, `newversions` with `stable` on the
  right entry).
* `OPTIONS bi6_update_check` returns 204 with the CORS headers; `GET` on the API paths returns 405;
  `api/deleteRecord` from a non-admin address still answers `{"result":"error","error":"Unauthorized"}`.
* A real Ctrl-C while running with no console input produced `Shutdown signal received.` and a clean
  exit, so the `PosixSignalRegistration` path works on .NET 10.
* `WebRequest.Create("ftp://...")` still returns an `FtpWebRequest` on .NET 10, so the FTP backup's
  client has not been removed from the runtime.

Not verified, because it would require the real server or private data:

* An actual successful update check against `blueirissoftware.com`.  That needs the private Blue Iris
  registration and PC codes, so only the "update check is not configured" path was exercised live; the
  parsing and JSON-building path was verified separately as described above.
* The weekly FTP database backup.  The `FtpWebRequest` machinery still exists, but the `backupFtpPath`
  documented as `ftpes://...` was never a scheme `WebClient` understood, on .NET Framework either.  If
  FTP backup is actually in use on the server, test it after deploying — and note that the certificate
  exemption described above is gone.
* Running on arm64 Linux itself.  The publish produces correct aarch64 binaries (checked with `file`),
  but nothing was executed on arm64: this machine has neither WSL nor a container runtime.
