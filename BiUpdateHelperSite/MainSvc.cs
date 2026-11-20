using System;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace BiUpdateHelperSite
{
	/// <summary>
	/// <para>The Windows service wrapper.  It has no logic of its own; everything the service does lives in
	/// <see cref="SiteService"/>.</para>
	/// <para>This type must only be loaded on Windows.  See <see cref="WindowsServiceHost"/> for why, and
	/// for how the startup path keeps it from being loaded anywhere else.</para>
	/// </summary>
	[SupportedOSPlatform("windows")]
	public partial class MainSvc : ServiceBase
	{
		private readonly SiteService service = new SiteService();

		public MainSvc()
		{
			InitializeComponent();
		}

		protected override void OnStart(string[] args)
		{
			service.Start();
		}

		protected override void OnStop()
		{
			service.Stop();
		}
	}
}
