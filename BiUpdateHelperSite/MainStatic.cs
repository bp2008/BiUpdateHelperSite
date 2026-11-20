using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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

			// Must happen before anything opens a database, because the runtime caches the result of
			// resolving a native library name.
			DB.SqliteNative.Initialize();

			settings = new Settings();
			settings.Load(SettingsPath);
			settings.SaveIfNoExist(SettingsPath);

			//bool gotServiceArg = (args.Length > 0 && args[0] == "svc");
			bool gotCmdArg = (args.Length > 0 && args[0] == "cmd");
			// The Windows service control manager exists only on Windows, and a Windows service session is
			// the only non-interactive session there is.  Everything else, Linux included, runs the service
			// in this process and waits here until it is asked to shut down.
			if (!gotCmdArg && !Environment.UserInteractive && OperatingSystem.IsWindows())
			{
				// Deliberately an indirect call into a type of its own.  System.ServiceProcess is not
				// usable off Windows, so this method must not name any type from it; see
				// WindowsServiceHost for the detail.
				WindowsServiceHost.Run();
			}
			else
			{
				SiteService svc = new SiteService();
				svc.Start();
				try
				{
					WaitForShutdownRequest();
				}
				finally
				{
					svc.Stop();
				}
			}
		}

		/// <summary>
		/// <para>Blocks until this process is asked to shut down.</para>
		/// <para>If standard input can be read from, that means typing "exit", as it always has.  When it
		/// cannot be read from, which is the case when a service manager such as supervisor started this
		/// process, this waits for SIGTERM or SIGINT instead, so that the service keeps running rather than
		/// stopping the moment it finds no console to read from.</para>
		/// </summary>
		private static void WaitForShutdownRequest()
		{
			while (true)
			{
				Console.WriteLine("Type \"exit\" to close.");
				string line;
				try
				{
					line = Console.ReadLine();
				}
				catch (IOException)
				{
					line = null;
				}
				if (line == null)
				{
					// Standard input is at its end (e.g. redirected from /dev/null) and will never deliver
					// a command, so reading it again would just spin.
					WaitForTerminationSignal();
					return;
				}
				if (line.Trim().ToLower() == "exit")
					return;
			}
		}

		/// <summary>
		/// Blocks until this process receives SIGTERM or SIGINT, and keeps the runtime from terminating the
		/// process itself when it arrives so that the service gets a chance to stop cleanly.
		/// </summary>
		private static void WaitForTerminationSignal()
		{
			Console.WriteLine("Standard input is unavailable.  Running until SIGTERM or SIGINT is received.");
			using (ManualResetEventSlim signalReceived = new ManualResetEventSlim(false))
			{
				Action<PosixSignalContext> handler = context =>
				{
					context.Cancel = true;
					signalReceived.Set();
				};
				using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, handler))
				using (PosixSignalRegistration.Create(PosixSignal.SIGINT, handler))
				{
					signalReceived.Wait();
				}
			}
			Console.WriteLine("Shutdown signal received.");
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
