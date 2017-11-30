using System.Collections.Generic;
using System.Linq;
using BiUpdateHelperSite.DB;

namespace BiUpdateHelperSite.JSAPI
{
	public class AllUsageRecordsResponse : APIResponse
	{
		public IEnumerable<UsageRecord> records;
		/// <summary>
		/// This remains null unless the requester is admin, because if we leaked the secret strings, an attacker could replace anyone else's performance data.
		/// </summary>
		public IEnumerable<UsageRecord_Secret_Map> secrets;
		public AllUsageRecordsResponse() : base("Application logic error") { }
		public AllUsageRecordsResponse(IEnumerable<UsageRecord> records)
		{
			this.records = records;
		}
	}
}