using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiUpdateHelperSite.UsageRecordUpload.v2
{
	public class Upload_Record : v1.Upload_Record
	{
		public short CpuThreads; // New in v2(.1)
		public string DimmLocations = null;
		public bool ServiceMode = false;
		public bool ConsoleOpen = false;
		public short ConsoleWidth = -1;
		public short ConsoleHeight = -1;
		public short LivePreviewFPS = -1;
	}
	public class Upload_Camera : v1.Upload_Camera
	{
	}
	public class Upload_Gpu : v1.Upload_Camera
	{
	}
}