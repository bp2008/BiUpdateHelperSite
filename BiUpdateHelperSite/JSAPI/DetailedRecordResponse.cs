using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BiUpdateHelperSite.DB;

namespace BiUpdateHelperSite.JSAPI
{
	public class DetailedRecordResponse : APIResponse
	{
		public UsageRecord usage;
		/// <summary>
		/// This remains null unless the user is admin.
		/// </summary>
		public string secret;
		public IEnumerable<Camera> cameras;
		public IEnumerable<GpuInfo> gpus;
		public DetailedRecordResponse() : base("Application logic error") { }
		public DetailedRecordResponse(UsageRecord usage, IEnumerable<Camera> cameras, IEnumerable<GpuInfo> gpus)
		{
			this.usage = usage;
			this.cameras = cameras;
			this.gpus = gpus;
		}
	}
}
