//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using BiUpdateHelperSite.DB;

//namespace BiUpdateHelperSite.JSAPI
//{
//	public class JSUsageRecord
//	{
//		public DateTime Timestamp;
//		public string OS;
//		public string HelperVersion;
//		public string BiVersion;
//		public string CpuModel;
//		public int CpuMhz;
//		public byte CpuUsage;
//		public byte BiCpuUsage;
//		public int MemMb;
//		public int BiMemUsageMb;
//		public int BiPeakVirtualMemUsageMb;
//		public int MemFreeMb;
//		public string HwAccel;
//		public byte CameraCount;
//		public float Total_Megapixels;
//		public float Total_FPS;
//		public float Total_MPPS;

//		public JSUsageRecord()
//		{
//		}
//		public JSUsageRecord(UsageRecord r)
//		{
//			Timestamp = r.Timestamp;
//			OS = r.OS;
//			HelperVersion = r.HelperVersion;
//			BiVersion = r.BiVersion;
//			CpuModel = r.CpuModel;
//			CpuMhz = r.CpuMhz;
//			CpuUsage = r.CpuUsage;
//			BiCpuUsage = r.BiCpuUsage;
//			MemMb = r.MemMb;
//			BiMemUsageMb = r.BiMemUsageMb;
//			BiPeakVirtualMemUsageMb = r.BiPeakVirtualMemUsageMb;
//			MemFreeMb = r.MemFreeMb;
//			HwAccel = r.HwAccel.ToString();
//			CameraCount = r.CameraCount;
//			Total_Megapixels = r.Total_Megapixels;
//			Total_FPS = r.Total_FPS;
//			Total_MPPS = r.Total_MPPS;
//		}
//	}
//	public class JSCamera
//	{
//		public int Pixels;
//		public byte FPS;
//		public bool LimitDecode;
//		public string Hwaccel;
//		public string Type;
//		public string CapType;
//		public JSCamera()
//		{
//		}
//		public JSCamera(Camera c)
//		{
//			Pixels = c.Pixels;
//			FPS = c.FPS;
//			LimitDecode = c.LimitDecode;
//			Hwaccel = c.Hwaccel.ToString();
//			Type = c.Type.ToString();
//			CapType = c.CapType.ToString();
//		}
//	}
//	public class JSGpu
//	{
//		public string Name;
//		public string Version;
//		public JSGpu()
//		{
//		}
//		public JSGpu(GpuInfo g)
//		{
//			Name = g.Name;
//			Version = g.DriverVersion;
//		}
//	}
//}
