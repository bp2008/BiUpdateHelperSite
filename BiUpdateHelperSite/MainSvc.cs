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
