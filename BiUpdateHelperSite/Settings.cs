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
		/// <summary>
		/// <para>Blue Iris registration code, used by the "bi6_update_check" API to check for Blue Iris
		/// updates.  If this or <see cref="biUpdateCheckPcCode"/> is not configured, that API responds
		/// with a failure.</para>
		/// <para>This is private information which must not be committed to source control.</para>
		/// </summary>
		public string biUpdateCheckRegCode = "";
		/// <summary>
		/// <para>Blue Iris PC code, used by the "bi6_update_check" API to check for Blue Iris updates.
		/// If this or <see cref="biUpdateCheckRegCode"/> is not configured, that API responds with a
		/// failure.</para>
		/// <para>This is private information which must not be committed to source control.</para>
		/// </summary>
		public string biUpdateCheckPcCode = "";
	}
}
