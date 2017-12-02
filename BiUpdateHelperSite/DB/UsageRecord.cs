using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BPUtil;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SQLite;

namespace BiUpdateHelperSite.DB
{
	public class UsageRecord
	{
		[PrimaryKey, AutoIncrement]
		public int ID { get; set; }
		/// <summary>
		/// Unique key created by the client so it can update its own usage record without creating a new record every time.
		/// If [Secret] and [CpuModel] both match an existing record, that record is updated instead of a new record being added.
		/// </summary>
		[MaxLength(64)]
		[JsonIgnore]
		public string Secret { get; set; }
		/// <summary>
		/// The time at which this record was created, in milliseconds since the Unix Epoch.
		/// Because DateTime loses its time zone if we use DateTime natively.
		/// </summary>
		public long Timestamp { get; set; }
		/// <summary>
		/// The OS name and version information.  E.g. "Windows 10 Pro v1703 b15063 (64 bit)"
		/// </summary>
		[MaxLength(128)]
		public string OS { get; set; }
		/// <summary>
		/// Version number of the helper app. e.g. "1.6.0.0"
		/// </summary>
		[MaxLength(16)]
		public string HelperVersion { get; set; }
		/// <summary>
		/// The Blue Iris version. e.g. "4.6.4.12 x64"
		/// </summary>
		[MaxLength(32)]
		public string BiVersion { get; set; }
		/// <summary>
		/// CPU model information. e.g. "Intel(R) Core(TM) i7-7700K CPU @ 4.20 GHz"
		/// </summary>
		[MaxLength(128)]
		public string CpuModel { get; set; }
		/// <summary>
		/// CPU max speed in MHz.  Not always accurate in case of turbo boost.
		/// </summary>
		public int CpuMHz { get; set; }
		/// <summary>
		/// Total system CPU usage [0-100]
		/// </summary>
		public byte CpuUsage { get; set; }
		/// <summary>
		/// Combined CPU usage of BlueIris.exe processes [0-100]
		/// </summary>
		public byte BiCpuUsage { get; set; }
		/// <summary>
		/// Total system memory, in MB.
		/// </summary>
		public int MemMB { get; set; }
		/// <summary>
		/// Combined memory usage of BlueIris.exe processes, in MB.
		/// </summary>
		public int BiMemUsageMB { get; set; }
		/// <summary>
		/// Combined virtual memory usage of BlueIris.exe processes, in MB.
		/// </summary>
		public int BiPeakVirtualMemUsageMB { get; set; }
		/// <summary>
		/// Free memory, in MB.
		/// </summary>
		public int MemFreeMB { get; set; }
		/// <summary>
		/// Value of the global Intel HD hardware acceleration setting. [Options]:"hwaccel"
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public HWAccel HwAccel { get; set; } = 0;
		/// <summary>
		/// Physical memory capacity in GiB.
		/// </summary>
		public float RamGiB { get; set; }
		/// <summary>
		/// Number of memory channels.  If 0, the true number is unknown.
		/// </summary>
		public ushort RamChannels { get; set; }
		/// <summary>
		/// [New in v2] A semicolon-separated string of Dimm Locations (slot names)
		/// </summary>
		[MaxLength(128)]
		public string DimmLocations { get; set; }
		/// <summary>
		/// Speed of the first physical memory DIMM, in MHz.
		/// </summary>
		public ushort RamMHz { get; set; }
		/// <summary>
		/// [New in v2] If true, BI was configured to run in service mode.
		/// </summary>
		public bool ServiceMode { get; set; }
		/// <summary>
		/// [New in v2] If true, the console was open.
		/// </summary>
		public bool ConsoleOpen { get; set; }
		/// <summary>
		/// [New in v2] -1 if unavailable. -2 if minimized.  Otherwise, a positive number, probably not zero.
		/// </summary>
		public short ConsoleWidth { get; set; }
		/// <summary>
		/// [New in v2] -1 if unavailable. -2 if minimized.  Otherwise, a positive number, probably not zero.
		/// </summary>
		public short ConsoleHeight { get; set; }
		/// <summary>
		/// [New in v2] -1 if unavailable. -2 if the limit is disabled.  Otherwise, a number typically between 1 and 30.
		/// </summary>
		public short LivePreviewFPS { get; set; }
		/// <summary>
		/// Number of enabled cameras.
		/// </summary>
		public byte CameraCount { get; set; }
		/// <summary>
		/// Total number of pixels, in millions, of all cameras together.
		/// </summary>
		public float Total_Megapixels { get; set; }
		/// <summary>
		/// Total number of frames per second being input into the system.
		/// </summary>
		public float Total_FPS { get; set; }
		/// <summary>
		/// Megapixels per second being input into the system.
		/// </summary>
		public float Total_MPPS { get; set; }
	}
	public enum HWAccel : byte
	{
		No = 0,
		Yes_H264 = 1,
		Yes_VPP = 2
	}
	public class UsageRecord_Secret_Map
	{
		public int ID;
		public string Secret;
	}
	public class UnixEpochDateTimeConverter : DateTimeConverterBase
	{
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			long ms = NumberUtil.ParseLong(reader.ReadAsString());
			return TimeUtil.DateTimeFromEpochMS(ms);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			writer.WriteRawValue(TimeUtil.GetTimeInMsSinceEpoch((DateTime)value).ToString());
		}
	}
}
