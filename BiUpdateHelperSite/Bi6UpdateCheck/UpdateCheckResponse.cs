using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BiUpdateHelperSite.Bi6UpdateCheck
{
	/// <summary>
	/// The response envelope of the proposed Blue Iris "updatecheck" JSON API command.
	/// See https://github.com/bp2008/ui3/wiki/updatecheck-API-documentation
	/// </summary>
	public class UpdateCheckResponse
	{
		/// <summary>
		/// "success" or "fail".
		/// </summary>
		public string result;
		/// <summary>
		/// An <see cref="UpdateCheckData"/> if the request succeeded, otherwise a <see cref="UpdateCheckFailure"/>.
		/// </summary>
		public object data;
		/// <summary>
		/// Creates a "success" response.
		/// </summary>
		/// <param name="data">The update information to return.</param>
		public UpdateCheckResponse(UpdateCheckData data)
		{
			result = "success";
			this.data = data;
		}
		/// <summary>
		/// Creates a "fail" response.
		/// </summary>
		/// <param name="reason">The reason the request failed.</param>
		public UpdateCheckResponse(string reason)
		{
			result = "fail";
			data = new UpdateCheckFailure(reason);
		}
	}
	/// <summary>
	/// The "data" object of a failed "updatecheck" response.
	/// </summary>
	public class UpdateCheckFailure
	{
		/// <summary>
		/// The reason the request failed.
		/// </summary>
		public string reason;
		public UpdateCheckFailure(string reason)
		{
			this.reason = reason;
		}
	}
	/// <summary>
	/// <para>The "data" object of a successful "updatecheck" response.</para>
	/// <para>The "maintenance_expire" and "maintenance_purchase_link" fields are deliberately not
	/// implemented.  Blue Iris reports support and maintenance status for the license key which this
	/// server checks updates with, and that has nothing to do with whoever is calling this API, so it
	/// would be misleading to pass it along.  Omitting "maintenance_expire" makes the caller skip the
	/// license renewal warnings entirely, which in turn makes "maintenance_purchase_link" pointless.</para>
	/// <para>The "changelog_url" field is not implemented either, because Blue Iris's update check page
	/// does not provide one.  Callers are expected to fall back on their own default.</para>
	/// </summary>
	public class UpdateCheckData
	{
		/// <summary>
		/// Free-form news/announcement text.  Newlines are significant.  Null if Blue Iris did not provide any news.
		/// </summary>
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string news;
		/// <summary>
		/// The list of available versions, in the order Blue Iris listed them.
		/// </summary>
		public List<UpdateCheckVersion> newversions = new List<UpdateCheckVersion>();
	}
	/// <summary>
	/// One available version, as listed in the "newversions" array of an "updatecheck" response.
	/// </summary>
	public class UpdateCheckVersion
	{
		/// <summary>
		/// Dotted version number, e.g. "6.0.8.6".
		/// </summary>
		public string version;
		/// <summary>
		/// Release date in milliseconds since the Unix Epoch (1970/1/1 midnight UTC).  Null if Blue Iris did not provide a usable date.
		/// </summary>
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public long? date;
		/// <summary>
		/// True if this is the version Blue Iris offers as its "latest critical or highly-stable update".
		/// </summary>
		public bool stable;
		/// <summary>
		/// Release notes.  Newlines are significant.  Null if Blue Iris did not provide notes for this version.
		/// </summary>
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string notes;
	}
}
