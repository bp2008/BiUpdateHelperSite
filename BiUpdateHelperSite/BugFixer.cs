using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BiUpdateHelperSite.UsageRecordUpload.v1;

namespace BiUpdateHelperSite
{
	/// <summary>
	/// A class providing methods to fix or work around bugs present in BiUpdateHelper versions causing them to upload bad data.
	/// </summary>
	public static class BugFixer
	{
		public static void Fix(Upload_Record record)
		{
			if (record.HelperVersion == "1.6.0.0")
			{
				// This version incorrectly calculated FPS from the "interval" registry key, always reporting 255.
				// 0 would be more accurate!
				foreach (Upload_Camera camera in record.cameras)
					if (camera.FPS == 255)
						camera.FPS = 0;
			}
		}
	}
}
