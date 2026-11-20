using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BiUpdateHelperSite.DB
{
	/// <summary>
	/// <para>Points sqlite-net (<c>DB\SQLite.cs</c>) at the native SQLite library which is deployed with
	/// this application.</para>
	/// <para>sqlite-net declares its P/Invokes against the library name "sqlite3", which the runtime looks
	/// for as "sqlite3.dll" on Windows and "libsqlite3.so" on Linux.  The library actually deployed is the
	/// one from the SQLitePCLRaw.lib.e_sqlite3 NuGet package, which is named "e_sqlite3" and exists as a
	/// prebuilt binary for every runtime identifier this application is published for, arm64 Linux
	/// included.  It is an ordinary SQLite build and exports the same <c>sqlite3_*</c> functions, so only
	/// the file name needs translating.</para>
	/// </summary>
	public static class SqliteNative
	{
		/// <summary>
		/// The library name which sqlite-net's P/Invoke declarations use.
		/// </summary>
		private const string sqliteNetLibraryName = "sqlite3";
		/// <summary>
		/// The library name which the SQLitePCLRaw.lib.e_sqlite3 package deploys.
		/// </summary>
		private const string deployedLibraryName = "e_sqlite3";

		private static bool initialized = false;
		private static readonly object initLock = new object();

		/// <summary>
		/// <para>Installs the import resolver.  Call this once during startup, before anything opens a
		/// database, because the runtime caches the result of resolving a library name.</para>
		/// <para>Calling this more than once does nothing.</para>
		/// </summary>
		public static void Initialize()
		{
			lock (initLock)
			{
				if (initialized)
					return;
				// sqlite-net is compiled into this assembly, so this is the assembly whose imports need resolving.
				Assembly assembly = typeof(SQLite.SQLiteConnection).Assembly;
				NativeLibrary.SetDllImportResolver(assembly, Resolve);
				// Set last, so that a failure here is not silently turned into a no-op by a later call.
				initialized = true;
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (libraryName == sqliteNetLibraryName)
			{
				IntPtr handle;
				if (NativeLibrary.TryLoad(deployedLibraryName, assembly, searchPath, out handle))
					return handle;
			}
			// IntPtr.Zero tells the runtime to carry on with its own default resolution for this name.
			return IntPtr.Zero;
		}
	}
}
