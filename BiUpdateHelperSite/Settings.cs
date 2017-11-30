using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BPUtil;

namespace BiUpdateHelperSite
{
	public class Settings : SerializableObjectBase
	{
		public int webPort = 5000;
		public string dbPath = "UsageDb.s3db";
		public string adminIp = "";
	}
}
