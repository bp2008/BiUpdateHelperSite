using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SQLite;

namespace BiUpdateHelperSite.DB
{
	public class GpuInfo
	{
		[PrimaryKey, AutoIncrement]
		[JsonIgnore]
		public int ID { get; set; }
		/// <summary>
		/// Record ID of the Usage Record that contributed this gpuInfo object.
		/// </summary>
		[JsonIgnore]
		[Indexed]
		public int UsageRecordID { get; set; }
		[MaxLength(48)]
		public string Name { get; set; }
		[MaxLength(32)]
		public string DriverVersion { get; set; }
	}
}
