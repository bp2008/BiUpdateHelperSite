using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace BiUpdateHelperSite
{
	public partial class MainSvc : ServiceBase
	{
		WebServer server;

		public MainSvc()
		{
			InitializeComponent();
		}

		protected override void OnStart(string[] args)
		{
			// Loads cached Blue Iris update information from disk so that the "bi6_update_check" API has
			// something to answer with even if Blue Iris's server is unreachable right now.
			Bi6UpdateCheck.UpdateChecker.Initialize();
			server?.Stop();
			server = new WebServer(MainStatic.settings.webPort);
			server.SocketBound += Server_SocketBound;
			server.Start();
		}

		private void Server_SocketBound(object sender, string e)
		{
			Console.WriteLine(e);
		}

		protected override void OnStop()
		{
			server?.Stop();
		}

		public void DoStart()
		{
			OnStart(null);
		}

		public void DoStop()
		{
			OnStop();
		}
	}
}
