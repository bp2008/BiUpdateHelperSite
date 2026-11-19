# Running BiUpdateHelperSite on .NET 8

The site used to target .NET Framework 4.5.2 and ran on the Linux server under mono.  It now targets
`net8.0` and runs on the ordinary .NET runtime, so mono is no longer involved and the service no longer
depends on whatever mono version the distribution happens to ship.

## What changed in the project

* `BiUpdateHelperSite.csproj` is an SDK-style project targeting `net8.0`.  `packages.config` is gone;
  Newtonsoft.Json 13.0.4 is a `PackageReference`.  `App.config` is gone, because .NET 8 does not read it.
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
* `MainStatic.cs` only calls `ServiceBase.Run` on Windows now, and when standard input cannot be read
  from — which is the case when supervisor starts the process — it waits for `SIGTERM`/`SIGINT` instead
  of reading a console it does not have.  On Windows, interactively and as a Windows service, the
  behaviour is what it always was.
* `WebServer.stopServer` interrupts the FTP backup thread instead of aborting it, because
  `Thread.Abort` is not implemented on .NET 8.
* **The net45 build was dropped.**  Keeping it would have meant conditional compilation around
  `ServiceBase`, two sets of native SQLite assets and two package styles, for a target the deployment is
  moving away from.  If a .NET Framework build is ever wanted again, this is the commit to look at.

Nothing about the HTTP endpoints, the JSON they return, the `SiteSettings.cfg` format or the database
schema changed.

## Build and publish

A solution-wide build still fails on this machine for an unrelated reason: BPUtil multi-targets
`net10.0`, which the installed SDK (9.0.317) cannot build.  Build the site project on its own.

```
dotnet build BiUpdateHelperSite/BiUpdateHelperSite.csproj
```

For the arm64 server, either publish works.  **Self-contained is the recommendation**, because it carries
its own runtime and so does not care which `dotnet-runtime-*` packages Ubuntu 26.04 ships — which is the
same packaging risk that prompted moving off mono:

```
dotnet publish BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release -r linux-arm64 --self-contained true
```

That produces about 83 MB in `BiUpdateHelperSite/bin/Release/net8.0/linux-arm64/publish/`, with a native
`BiUpdateHelperSite` executable, `libe_sqlite3.so`, and the `www` folder.  It needs OpenSSL and ICU on the
host, both of which a normal Ubuntu install has.

Framework-dependent is the smaller option (about 4 MB) if a .NET 8 runtime is installed on the server:

```
dotnet publish BiUpdateHelperSite/BiUpdateHelperSite.csproj -c Release -r linux-arm64 --self-contained false
```

## Remaining deployment steps on the server

These have **not** been done — the live server was not touched.

1. Install .NET 8 on the server only if going framework-dependent (`apt install dotnet-runtime-8.0`, or
   Microsoft's packages).  A self-contained publish needs nothing installed.
2. Copy the publish output into `/var/biupdatehelpersite`, keeping the existing `SiteSettings.cfg`,
   `UsageDb.s3db`, `UsageDb-Archive.s3db` and `Bi6UpdateCheckData/` in place.  Delete the old
   `BiUpdateHelperSite.exe`, the other `*.exe`/`*.dll` from the mono build, and `libsqlite3.so`.
3. `chmod +x /var/biupdatehelpersite/BiUpdateHelperSite` (the apphost from a self-contained or
   RID-specific publish).
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
   service user, or set a different path.  This is not new — it was the same under mono — but it is worth
   confirming now that `bi6_update_check` depends on it.

## Verified locally

Against copies of the real `UsageDb.s3db` (2697 records) and `UsageDb-Archive.s3db` (95602 records):

* The service starts, binds its port and serves `www` (`/` redirects to `default.html`, static files and
  `%%VERSION%%`/`%%RND%%`/`%%ADMIN%%` substitution all work).
* `POST api/getUsageRecords` returns the full list, `?detailsForId=` returns the record with its cameras
  and GPUs, enums still serialize as strings, and gzip compression still applies.
* `POST api/uploadUsageRecord2` inserts a record with its cameras and GPUs; a payload with a wrong MD5 is
  still rejected with 400.
* Both database files were **byte-for-byte unchanged** after the service opened them, ran its
  `CreateTable` calls and read every row — the schema sqlite-net infers still matches what is on disk.
* `POST bi6_update_check` returns the documented JSON, and feeding the sample response from
  `UpdateChecker`'s own documentation through `ParseUpdateCheckResponse` produces exactly the documented
  success shape (news line, `changelog_url`, `newversions` with `stable` on the right entry).
* `OPTIONS bi6_update_check` returns 204 with the CORS headers; `GET` on the API paths returns 405.
* `SIGINT` while running without a console produces a clean shutdown.

Not verified, because it would require the real server or private data:

* An actual successful update check against `blueirissoftware.com`.  That needs the private Blue Iris
  registration and PC codes, so only the "update check is not configured" path was exercised live; the
  parsing and JSON-building path was verified separately as described above.
* The weekly FTP database backup.  `WebClient`/`FtpWebRequest` still work on .NET 8 (the obsoletion
  warning `SYSLIB0014` is suppressed in the project), but the `backupFtpPath` documented as
  `ftpes://…` was never a scheme `WebClient` understood, on .NET Framework either.  If FTP backup is
  actually in use on the server, test it after deploying.
* Running on arm64 Linux itself.  The publish produces correct aarch64 binaries (checked with `file`),
  but nothing was executed on arm64.
