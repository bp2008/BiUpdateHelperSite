using System;

namespace BiUpdateHelperSite
{
	/// <summary>
	/// <para>Starts and stops the web server.  That is the whole of what this service does, and it is kept
	/// here, in a class with no base type, so that it can run anywhere.</para>
	/// <para>On Windows the service control manager starts the service through <see cref="MainSvc"/>, which
	/// is a thin <c>ServiceBase</c> wrapper around this class.  Nothing outside Windows may touch that
	/// wrapper: a non-Windows publish gets the portable build of
	/// System.ServiceProcess.ServiceController, whose <c>ServiceBase</c> constructor throws
	/// <see cref="PlatformNotSupportedException"/>.  Everywhere else, including Linux, this class is used
	/// directly.</para>
	/// </summary>
	public class SiteService
	{
		private WebServer server;

		/// <summary>
		/// Starts the web server.  Safe to call again; an already-running server is stopped first.
		/// </summary>
		public void Start()
		{
			// Loads cached Blue Iris update information from disk so that the "bi6_update_check" API has
			// something to answer with even if Blue Iris's server is unreachable right now.
			Bi6UpdateCheck.UpdateChecker.Initialize();
			server?.Stop();
			server = new WebServer(MainStatic.settings.webPort);
			server.SocketBound += Server_SocketBound;
			server.Start();
		}

		/// <summary>
		/// Stops the web server, if one is running.
		/// </summary>
		public void Stop()
		{
			server?.Stop();
		}

		private void Server_SocketBound(object sender, string e)
		{
			Console.WriteLine(e);
		}
	}
}
