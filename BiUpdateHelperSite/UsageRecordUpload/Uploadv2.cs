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
		public byte webserverState = 0; // New in BiUpdateHelper 1.7
		public bool ProfileConfirmed = false; // New in BiUpdateHelper 1.7.1
		public new Upload_Camera[] cameras;
		public new Upload_Gpu[] gpus;
	}
	public class Upload_Camera : v1.Upload_Camera
	{
		public bool FPSConfirmed; // New in BiUpdateHelper 1.7.1.0.
		public int MainPixels; // New in BiUpdateHelper 1.9.0.0. If nonzero, the camera is believed to have a sub stream configured.
	}
	public class Upload_Gpu : v1.Upload_Gpu
	{
	}
}