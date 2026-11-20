using System;
using BPUtil;

namespace BiUpdateHelperSite
{
	internal static class MainStatic
	{
		public static Settings settings;
		public const string SettingsPath = "SiteSettings.cfg";

		public static void MainMethod()
		{
			// The data directory is named after the program rather than after the assembly, because the
			// Windows and Linux builds have different assembly names and must share one data directory.
			// AppInit creates this directory, makes it the working directory and puts its logs in it, so
			// the settings file, the databases and Bi6UpdateCheckData all live together inside it.
			Globals.InitializeCommonApplicationData("BiUpdateHelperSite");

			// Must happen before anything opens a database, because the runtime caches the result of
			// resolving a native library name.
			DB.SqliteNative.Initialize();

			WindowsServiceInitOptions options = new WindowsServiceInitOptions();
#if LINUX
			// The name of the systemd unit which AppInit's "install" argument generates.
			options.ServiceName = "biupdatehelpersite";
#endif
			// Takes care of the data directory, the log file, logging unhandled exceptions, loading the
			// service's settings, and running the service: under the Windows service control manager, or
			// under systemd, or interactively.  Returns when the service has stopped.
			AppInit.WindowsService<MainSvc>(options);
		}

		/// <summary>
		/// Loads the settings file, writing a default one first if there is none.  Called by
		/// <see cref="MainSvc"/>'s constructor, which is the first point at which the working directory is
		/// the data directory.
		/// </summary>
		public static void LoadSettings()
		{
			settings = new Settings();
			settings.Load(SettingsPath);
			settings.SaveIfNoExist(SettingsPath);
		}
	}
}
