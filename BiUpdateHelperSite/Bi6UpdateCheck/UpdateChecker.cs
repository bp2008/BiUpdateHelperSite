using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BPUtil;
using Newtonsoft.Json;

namespace BiUpdateHelperSite.Bi6UpdateCheck
{
	/// <summary>
	/// <para>Queries Blue Iris's update check page and translates the response into the JSON format
	/// proposed for the Blue Iris "updatecheck" JSON API command.</para>
	/// <para>See https://github.com/bp2008/ui3/wiki/updatecheck-API-documentation</para>
	/// </summary>
	public static class UpdateChecker
	{
		/// <summary>
		/// <para>The version number which is reported to Blue Iris's update check page.</para>
		/// <para>Blue Iris's server returns the full version history regardless of the version reported
		/// here (a request reporting 6.0.8.6 was answered with the history going back to 6.0.0.1), so
		/// this value only needs to be a plausible version number.</para>
		/// </summary>
		private const string reportedVersion = "6.0.8.6";
		/// <summary>
		/// The User-Agent header value which Blue Iris sends when it checks for updates.
		/// </summary>
		private const string userAgent = "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 5.1; Trident/4.0; .NET CLR 2.0.50727; .NET CLR 3.0.04506.648; .NET CLR 3.5.21022; .NET4.0C; .NET4.0E)";
		/// <summary>
		/// Date formats which have been observed in Blue Iris's update check response.
		/// </summary>
		private static readonly string[] dateFormats = new string[] { "M/d/yyyy", "M-d-yyyy" };

		private static WebRequestUtility wru = new WebRequestUtility(userAgent, 30000);
		/// <summary>
		/// <para>The JSON response body, cached so that a burst of API calls results in at most one
		/// request to Blue Iris's server per minute.</para>
		/// <para>Between 0 and 65 seconds of age, the cached response is returned immediately.  Older than that, the caller waits for a new one.</para>
		/// </summary>
		private static CachedObject<string> cachedResponse = new CachedObject<string>(BuildResponse, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(65));
		/// <summary>
		/// The most recently built successful response, returned to callers if a later update check fails.
		/// </summary>
		private static string lastSuccessfulResponse = null;

		/// <summary>
		/// Returns the JSON body to respond with.  This method does not throw.
		/// </summary>
		public static string GetResponseJson()
		{
			try
			{
				return cachedResponse.GetInstance();
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, "bi6_update_check");
				return JsonConvert.SerializeObject(new UpdateCheckResponse("update check failed"));
			}
		}

		/// <summary>
		/// Checks for updates and returns the JSON body to respond with.  This method does not throw.
		/// </summary>
		private static string BuildResponse()
		{
			try
			{
				string regcode = MainStatic.settings.biUpdateCheckRegCode;
				string pccode = MainStatic.settings.biUpdateCheckPcCode;
				if (string.IsNullOrWhiteSpace(regcode) || string.IsNullOrWhiteSpace(pccode))
					return JsonConvert.SerializeObject(new UpdateCheckResponse("update check is not configured"));

				// These request parameters and headers mimic the update check which Blue Iris 6 performs.
				// The date parameter is not URL-encoded by Blue Iris apart from the space between the date and the time.
				string url = "https://blueirissoftware.com:443/version6.asp"
					+ "?news=1"
					+ "&v=" + reportedVersion
					+ "&date=" + DateTime.Now.ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture).Replace(" ", "%20")
					+ "&regcode=" + Uri.EscapeDataString(regcode)
					+ "&pccode=" + Uri.EscapeDataString(pccode);

				BpWebResponse response = wru.GET(url, new string[] {
					"Accept", "*/*",
					"Accept-Language", "en-us",
					"Referer", "https://blueirissoftware.com:443/",
					"Cache-Control", "no-cache"
				});
				if (response.StatusCode != 200)
					throw new Exception("Blue Iris update check responded with status code " + response.StatusCode, response.ex);

				UpdateCheckData data = ParseUpdateCheckResponse(response.str);
				if (data.newversions.Count == 0)
					throw new Exception("Blue Iris update check response did not contain any version information.  The configured regcode and pccode may no longer be valid.");

				string json = JsonConvert.SerializeObject(new UpdateCheckResponse(data));
				lastSuccessfulResponse = json;
				return json;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, "bi6_update_check");
				// Stale update information is a lot more useful than no update information.
				string lastGood = lastSuccessfulResponse;
				if (lastGood != null)
					return lastGood;
				return JsonConvert.SerializeObject(new UpdateCheckResponse("update check failed"));
			}
		}

		/// <summary>
		/// <para>Parses the body of a response from Blue Iris's update check page.</para>
		/// <para>The interesting part of the response is a series of lines wrapped in some HTML:</para>
		/// <code>
		/// News=2,6-24-2026,Upgrade to Version 6
		/// Support=2,1/1/2030,1,
		/// Version=6.0.8.3,6/22/2026
		/// Version=6.0.8.4,6/23/2026
		/// Fixes for 6.0.7.7 A/V sync issues
		/// Version=6.0.8.6,6/30/2026
		/// Smoother timeline playback for UI3 and app
		/// </code>
		/// <para>Any line which is not one of the recognized "Key=Value" lines is a continuation of the
		/// previous one, such as the release notes belonging to a Version line.</para>
		/// </summary>
		/// <param name="body">Response body from Blue Iris's update check page.</param>
		private static UpdateCheckData ParseUpdateCheckResponse(string body)
		{
			List<string> newsLines = new List<string>();
			List<ParsedVersion> versions = new List<ParsedVersion>();
			// The line collection which continuation lines currently belong to.  Null if they belong to nothing.
			List<string> continuation = null;
			// False until the first recognized line is found, so that the HTML above it can be ignored.
			bool foundData = false;

			foreach (string rawLine in body.Split('\n'))
			{
				string line = rawLine.Trim();
				if (line.Length == 0)
					continue;
				if (line.StartsWith("News=", StringComparison.OrdinalIgnoreCase))
				{
					// e.g. "News=2,6-24-2026,Upgrade to Version 6".  The meaning of the first two fields
					// is unknown, so only the message text is used.
					foundData = true;
					continuation = newsLines;
					string[] parts = line.Substring("News=".Length).Split(new char[] { ',' }, 3);
					if (parts.Length == 3 && parts[2].Trim().Length > 0)
						newsLines.Add(parts[2].Trim());
				}
				else if (line.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
				{
					// e.g. "Version=6.0.8.6,6/30/2026"
					foundData = true;
					continuation = null;
					string[] parts = line.Substring("Version=".Length).Split(',');
					string version = parts[0].Trim();
					if (!IsPlausibleVersionNumber(version))
						continue;
					ParsedVersion v = new ParsedVersion();
					v.version = version;
					if (parts.Length > 1)
						v.date = ParseDate(parts[1]);
					versions.Add(v);
					continuation = v.notes;
				}
				else if (line.StartsWith("Support=", StringComparison.OrdinalIgnoreCase))
				{
					// Support and maintenance status of the license key which this server checks updates
					// with.  It does not apply to whoever is calling this API, so it is not parsed.
					foundData = true;
					continuation = null;
				}
				else if (!foundData || line.StartsWith("<"))
					continue; // HTML surrounding the data.
				else if (continuation != null)
					continuation.Add(line);
			}

			// Blue Iris repeats two versions at the end of the list: the "latest critical or highly-stable
			// update" followed by the "latest update available".  They may be the same version.
			if (versions.Count >= 2)
				versions[versions.Count - 2].stable = true;

			UpdateCheckData data = new UpdateCheckData();
			if (newsLines.Count > 0)
				data.news = string.Join("\n", newsLines);

			Dictionary<string, UpdateCheckVersion> versionMap = new Dictionary<string, UpdateCheckVersion>();
			foreach (ParsedVersion v in versions)
			{
				string notes = v.notes.Count > 0 ? string.Join("\n", v.notes) : null;
				UpdateCheckVersion existing;
				if (versionMap.TryGetValue(v.version, out existing))
				{
					// A version can be listed more than once, e.g. when the latest version is also the
					// latest stable version.  Merge the duplicate into the entry we already have.
					if (v.date != null)
						existing.date = v.date;
					if (v.stable)
						existing.stable = true;
					if (notes != null)
						existing.notes = notes;
				}
				else
				{
					UpdateCheckVersion ucv = new UpdateCheckVersion();
					ucv.version = v.version;
					ucv.date = v.date;
					ucv.stable = v.stable;
					ucv.notes = notes;
					versionMap[ucv.version] = ucv;
					data.newversions.Add(ucv);
				}
			}
			return data;
		}

		/// <summary>
		/// <para>Returns true if the given string looks like a Blue Iris version number, e.g. "6.0.8.6".</para>
		/// <para>Blue Iris's update check page answers a request carrying a missing or unrecognized
		/// regcode with HTTP 200 and the single line "Version=0", which must not be mistaken for an
		/// available update.</para>
		/// </summary>
		/// <param name="version">Version string from Blue Iris's update check response.</param>
		private static bool IsPlausibleVersionNumber(string version)
		{
			string[] parts = version.Split('.');
			if (parts.Length < 2)
				return false;
			foreach (string part in parts)
			{
				if (part.Length == 0)
					return false;
				foreach (char c in part)
					if (c < '0' || c > '9')
						return false;
			}
			return true;
		}

		/// <summary>
		/// Parses a date such as "6/30/2026", returning the number of milliseconds since the Unix Epoch
		/// (1970/1/1 midnight UTC), or null if the date could not be parsed.
		/// </summary>
		/// <param name="str">Date string from Blue Iris's update check response.</param>
		private static long? ParseDate(string str)
		{
			DateTime date;
			if (DateTime.TryParseExact(str.Trim(), dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
				return TimeUtil.GetTimeInMsSinceEpoch(new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc));
			return null;
		}

		/// <summary>
		/// One version listed by Blue Iris's update check page, before its release notes have been joined together.
		/// </summary>
		private class ParsedVersion
		{
			public string version;
			public long? date;
			public bool stable;
			public List<string> notes = new List<string>();
		}
	}
}
