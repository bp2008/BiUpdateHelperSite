using System;
using BPUtil;
#if !LINUX
using System.ServiceProcess;
#endif

namespace BiUpdateHelperSite
{
	/// <summary>
	/// <para>The service.  On Windows it is a <c>ServiceBase</c>, so that the Windows service control
	/// manager can drive it.  On Linux it derives from nothing at all, and BPUtil's
	/// <see cref="BPUtil.AppInit.WindowsService{ServiceType}"/> calls <c>OnStart</c> and <c>OnStop</c>
	/// directly.</para>
	/// <para>The base type is compiled out rather than avoided at run time on purpose.  A non-Windows
	/// publish gets the portable build of System.ServiceProcess.ServiceController, whose
	/// <c>ServiceBase</c> constructor throws <see cref="PlatformNotSupportedException"/>, so a runtime
	/// guard around the service's use is not enough: merely constructing the object would throw.  With
	/// the base type gone from the Linux build, there is nothing left to throw.</para>
	/// </summary>
	public partial class MainSvc
#if !LINUX
		: ServiceBase
#endif
	{
		private WebServer server;
		/// <summary>
		/// The time <see cref="DoStart"/> ran, used to log how long the service had been running when it
		/// stopped.  Null until the service has started.
		/// </summary>
		private DateTime? startedUtc;

		public MainSvc()
		{
			// AppInit has already pointed the working directory at the data directory by the time it
			// constructs this, which is where the settings file and the databases live.
			MainStatic.LoadSettings();
#if !LINUX
			InitializeComponent();
#endif
		}

#if LINUX
		protected void OnStart(string[] args)
		{
			DoStart();
		}

		protected void OnStop()
		{
			DoStop();
		}
#else
		protected override void OnStart(string[] args)
		{
			DoStart();
		}

		protected override void OnStop()
		{
			DoStop();
		}
#endif

		private void DoStart()
		{
			startedUtc = DateTime.UtcNow;
			Logger.Info(Globals.AssemblyName + " " + Globals.AssemblyVersion + " Starting Up");

			// Loads cached Blue Iris update information from disk so that the "bi6_update_check" API has
			// something to answer with even if Blue Iris's server is unreachable right now.
			Bi6UpdateCheck.UpdateChecker.Initialize();
			server?.Stop();
			server = new WebServer(MainStatic.settings.webPort);
			server.SocketBound += Server_SocketBound;
			server.Start();
		}

		private void DoStop()
		{
			// Only an orderly stop (the service control manager, systemd, or Ctrl+C) reaches this method, so
			// a "Starting Up" line with no "Shutting Down" line after it means the process died instead of
			// being asked to stop.  The uptime is logged with it, because that is what tells a normal restart
			// apart from a crash-restart loop: a loop shows a run of start/stop-less starts, or starts
			// followed by stops after only seconds of uptime.
			string uptime = startedUtc == null ? "unknown (the service never finished starting)" : TimeUtil.ToDHMS(DateTime.UtcNow - startedUtc.Value);
			Logger.Info(Globals.AssemblyName + " " + Globals.AssemblyVersion + " Shutting Down after an orderly stop request.  Uptime: " + uptime);

			server?.Stop();
		}

		private void Server_SocketBound(object sender, string e)
		{
			Console.WriteLine(e);
		}
	}
}
