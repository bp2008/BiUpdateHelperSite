using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiUpdateHelperSite.UsageRecordUpload.v1
{
	public class Upload_Record
	{
		public string Secret;
		public string HelperVersion;
		public string OS;
		public string BiVersion;
		public string CpuModel;
		public int CpuMHz;
		public byte CpuUsage;
		public byte BiCpuUsage;
		public int MemMB;
		public int BiMemUsageMB;
		public int BiPeakVirtualMemUsageMB;
		public int MemFreeMB;
		public byte HwAccel;
		public float RamGiB;
		public ushort RamChannels;
		public ushort RamMHz;
		public Upload_Camera[] cameras;
		public Upload_Gpu[] gpus;
	}
	public class Upload_Camera
	{
		public int Pixels;
		public byte FPS;
		public bool LimitDecode;
		public byte Hwaccel;
		public byte Type;
		public byte CapType;
		public bool MotionDetector;
		public byte RecordTriggerType;
		public byte RecordFormat;
		public bool DirectToDisk;
		public string VCodec;
	}
	public class Upload_Gpu
	{
		public string Name;
		public string Version;
	}
}