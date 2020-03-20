using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SQLite;

namespace BiUpdateHelperSite.DB
{
	public class Camera
	{
		[PrimaryKey, AutoIncrement]
		[JsonIgnore]
		public int ID { get; set; }
		/// <summary>
		/// Record ID of the Usage Record that contributed this camera object.
		/// </summary>
		[JsonIgnore]
		[Indexed]
		public int UsageRecordID { get; set; }
		/// <summary>
		/// Precise number of pixels in one frame. "fullxres" * "fullyres"
		/// </summary>
		public int Pixels { get; set; }
		/// <summary>
		/// Frame rate. From JSON API.
		/// </summary>
		public byte FPS { get; set; }
		/// <summary>
		/// New in BiUpdateHelper 1.7.1.0.  True if the FPS came from the web server and is therefore reliable.
		/// </summary>
		public bool FPSConfirmed { get; set; }
		/// <summary>
		/// Only i-frames are decoded. "smartdecode" [0,1]
		/// </summary>
		public bool LimitDecode { get; set; }
		/// <summary>
		/// Hardware acceleration override. "ip_hwaccel" [0,1,2,3]
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public HWAccelCamera Hwaccel { get; set; } = 0;
		/// <summary>
		/// Camera type. "type"
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public CameraType Type { get; set; } = 0;
		/// <summary>
		/// Screencap type. "screencap"
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public ScreenCapType CapType { get; set; } = 0;
		/// <summary>
		/// True if the motion detector is enabled. Registry: /Motion: "enabled"
		/// </summary>
		public bool MotionDetector { get; set; }
		/// <summary>
		/// How recordings are triggered. Registry: /Motion: "continuous"
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public RecordingTriggerType RecordTriggerType { get; set; } = 0;
		/// <summary>
		/// Format used for recording. Registry: /Clips: "movieformat"
		/// </summary>
		[JsonConverter(typeof(StringEnumConverter))]
		public RecordingFormat RecordFormat { get; set; } = 0;
		/// <summary>
		/// Video Codec. Registry: /Clips: "vcodec"
		/// </summary>
		[MaxLength(16)]
		public string VCodec { get; set; }
		/// <summary>
		/// Direct-to-disk recording (no video transcoding). Registry: /Clips: "transcode"
		/// </summary>
		public bool DirectToDisk { get; set; }
	}
	public enum HWAccelCamera : byte
	{
		Default = 0,
		Intel = 1,
		No = 2,
		IntelVPP = 3,
		NvidiaCUDA = 4
	}
	public enum CameraType : byte
	{
		ScreenCapture = 0,
		USBFirewireAnalog = 2,
		Network = 4,
		ClientBroadcast = 5
	}
	public enum ScreenCapType : byte
	{
		UScreenCapture = 0,
		DirectDrawBlits = 1,
		Blackness = 2
	}
	public enum RecordingTriggerType : byte
	{
		Motion = 0,
		Continuous = 1,
		Periodic = 3,
		TriggeredAndPeriodic = 4,
		TriggeredAndContinuous = 5
	}
	public enum RecordingFormat : byte
	{
		AVI = 0,
		BVR = 1,
		WMV = 2,
		MP4 = 3
	}
}
