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
		public string dbArchivePath = "UsageDb-Archive.s3db";
		public string adminIp = "";
		/// <summary>
		/// E.g. "ftpes://ftp.example.com:21/
		/// </summary>
		public string backupFtpPath = "";
		public string backupFtpUser = "";
		public string backupFtpPass = "";
		public long lastFtpBackupMs = 0;
		public bool logVerbose = false;
	}
}
