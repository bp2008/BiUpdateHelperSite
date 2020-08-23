using System;
using System.IO;
using System.ServiceProcess;
using BPUtil;

namespace BiUpdateHelperSite
{
	internal static class MainStatic
	{
		public static Settings settings;
		public const string SettingsPath = "SiteSettings.cfg";

		public static void MainMethod(string[] args)
		{
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

			settings = new Settings();
			settings.Load(SettingsPath);
			settings.SaveIfNoExist(SettingsPath);

			BPUtil.SimpleHttp.SimpleHttpLogger.RegisterLogger(BPUtil.Logger.httpLogger, settings.logVerbose);
			//bool gotServiceArg = (args.Length > 0 && args[0] == "svc");
			bool gotCmdArg = (args.Length > 0 && args[0] == "cmd");
			if (Environment.UserInteractive || gotCmdArg
				//&& (Environment.OSVersion.Platform == PlatformID.Win32NT
				//	|| Environment.OSVersion.Platform == PlatformID.Win32S
				//	|| Environment.OSVersion.Platform == PlatformID.Win32Windows
				//	|| Environment.OSVersion.Platform == PlatformID.WinCE)
				)
			{
				MainSvc svc = new MainSvc();
				svc.DoStart();
				do
				{
					Console.WriteLine("Type \"exit\" to close.");
				}
				while (Console.ReadLine().Trim().ToLower() != "exit");
				svc.DoStop();
			}
			else
			{
				ServiceBase.Run(new MainSvc());
			}
		}

		private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			if (e.ExceptionObject is Exception)
				Logger.Debug((Exception)e.ExceptionObject);
			else
				Logger.Debug(e.ExceptionObject?.ToString());
		}
	}
}