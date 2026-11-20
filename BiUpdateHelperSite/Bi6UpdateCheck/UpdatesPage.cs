using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BPUtil;

namespace BiUpdateHelperSite.Bi6UpdateCheck
{
	/// <summary>
	/// <para>Builds the HTML page served at "/updates", which presents the same Blue Iris update
	/// information the "bi6_update_check" API answers with, for a person to read instead of a program.</para>
	/// <para>The page is entirely self-contained: no external stylesheet, font, script or image, so it
	/// renders the same whether or not the reader can reach anything besides this server.</para>
	/// <para>Everything which came from Blue Iris's update server is treated as untrusted text and is HTML
	/// encoded on the way in to the page.  Nothing from the settings file reaches the page; in particular
	/// the registration code and PC code used for the update check are private and must never appear here,
	/// not even in a comment.</para>
	/// </summary>
	public static class UpdatesPage
	{
		/// <summary>
		/// <para>URL of the Blue Iris 6 changelog, used when the update check did not supply one (which is
		/// the case whenever it failed).  This is the same URL <c>UpdateChecker</c> puts in its response.</para>
		/// </summary>
		private const string defaultChangelogUrl = "https://blueirissoftware.com/changelog6.pdf";
		/// <summary>
		/// Separator which <c>UpdateChecker</c> puts between the fields of a news line: two spaces.
		/// </summary>
		private const string newsFieldSeparator = "  ";

		/// <summary>
		/// Builds the complete HTML document for the "/updates" page.  This method does not throw.
		/// </summary>
		/// <param name="result">The outcome of an update check, from <see cref="UpdateChecker.GetResponseData"/>.</param>
		public static string BuildHtml(UpdateCheckResult result)
		{
			// A fresh timestamp per page load, for the changelog link.  See AppendChangelog.
			long nowMs = TimeUtil.GetTimeInMsSinceEpoch();
			try
			{
				return BuildPage(result, nowMs);
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, "updates page");
				// The failure form of the page is built out of nothing but constants, so it cannot fail for
				// whatever reason the attempt above did.
				return BuildPage(new UpdateCheckResult("the update information could not be rendered"), nowMs);
			}
		}

		/// <summary>
		/// Builds the complete HTML document for the "/updates" page.
		/// </summary>
		/// <param name="result">The outcome of an update check.</param>
		/// <param name="nowMs">The current time in milliseconds since the Unix Epoch.</param>
		private static string BuildPage(UpdateCheckResult result, long nowMs)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(documentHead);
			sb.Append("<h1>Blue Iris Updates</h1>\n");
			sb.Append("<p class=\"lead\">The versions of Blue Iris 6 which are available right now, as reported by Blue Iris&#39;s own update server.</p>\n");

			if (result == null || !result.Success)
			{
				string reason = result == null ? null : result.failureReason;
				sb.Append("<div class=\"notice\">\n");
				sb.Append("<p class=\"noticeHead\">Update information is not available right now.</p>\n");
				sb.Append("<p>The update check did not succeed");
				if (!string.IsNullOrWhiteSpace(reason))
					sb.Append(": ").Append(Enc(reason.Trim()));
				sb.Append(".  Please try again later.</p>\n");
				sb.Append("</div>\n");
			}
			else
			{
				AppendNews(sb, result.data.news);
				AppendVersions(sb, result.data.newversions);
			}

			AppendChangelog(sb, result != null && result.Success ? result.data.changelog_url : null, nowMs);
			sb.Append(documentFoot);
			return sb.ToString();
		}

		/// <summary>
		/// Appends the news section, or nothing at all if Blue Iris did not provide any news.
		/// </summary>
		/// <param name="sb">StringBuilder the page is being built in.</param>
		/// <param name="news">The "news" field of the update check data.</param>
		private static void AppendNews(StringBuilder sb, string news)
		{
			string[] lines = SplitLines(news);
			if (lines.Length == 0)
				return;
			sb.Append("<h2>News from Blue Iris</h2>\n");
			foreach (string line in lines)
			{
				string date, text, url;
				ParseNewsLine(line, out date, out text, out url);
				sb.Append("<section class=\"card\">\n");
				if (date != null)
					sb.Append("<p class=\"meta\">").Append(date).Append("</p>\n");
				if (text != null)
					sb.Append("<p class=\"newsText\">").Append(Enc(text)).Append("</p>\n");
				if (url != null)
					sb.Append("<p><a href=\"").Append(Attr(url)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">Read the announcement (PDF)</a></p>\n");
				sb.Append("</section>\n");
			}
		}

		/// <summary>
		/// <para>Splits one line of the "news" field into the pieces the page displays separately.</para>
		/// <para>A news line is the fields of the news item separated from each other by two spaces, any of
		/// which may be missing, e.g.</para>
		/// <code>2026-11-10  Critical Security Updates  https://blueirissoftware.com/news/3.pdf</code>
		/// </summary>
		/// <param name="line">One line of the "news" field.</param>
		/// <param name="date">(Output) The publication date as HTML, or null if the line did not begin with a date.</param>
		/// <param name="text">(Output) The headline, or null if the line did not contain one.</param>
		/// <param name="url">(Output) The URL of the news item, or null if the line did not contain a usable one.</param>
		private static void ParseNewsLine(string line, out string date, out string text, out string url)
		{
			date = null;
			text = null;
			url = null;
			List<string> textFields = new List<string>();
			string[] fields = line.Split(new string[] { newsFieldSeparator }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < fields.Length; i++)
			{
				string field = fields[i].Trim();
				if (field.Length == 0)
					continue;
				if (IsSafeHttpUrl(field))
					url = field;
				else if (i == 0 && date == null && TryFormatIsoDate(field, out date))
					continue;
				else
					textFields.Add(field);
			}
			if (textFields.Count > 0)
				text = string.Join(" ", textFields);
		}

		/// <summary>
		/// Appends the list of available versions, newest first.
		/// </summary>
		/// <param name="sb">StringBuilder the page is being built in.</param>
		/// <param name="newversions">The "newversions" list of the update check data, in the order Blue Iris listed them.</param>
		private static void AppendVersions(StringBuilder sb, List<UpdateCheckVersion> newversions)
		{
			List<UpdateCheckVersion> versions = new List<UpdateCheckVersion>();
			if (newversions != null)
				foreach (UpdateCheckVersion v in newversions)
					if (v != null)
						versions.Add(v);
			// Blue Iris lists the oldest version first.  The newest is what a reader came for, so it goes on top.
			versions.Reverse();

			sb.Append("<h2>Available versions</h2>\n");
			if (versions.Count == 0)
			{
				sb.Append("<div class=\"card\"><p>Blue Iris&#39;s update server did not list any versions.</p></div>\n");
				return;
			}
			bool anyStable = false;
			foreach (UpdateCheckVersion v in versions)
				if (v.stable)
					anyStable = true;
			if (anyStable)
				sb.Append("<p class=\"legend\">Blue Iris offers the version marked <span class=\"badge\">Stable</span> as its latest critical or highly stable update.</p>\n");

			foreach (UpdateCheckVersion v in versions)
			{
				sb.Append("<section class=\"card\">\n<div class=\"versionHead\">");
				string version = v.version == null ? "" : v.version.Trim();
				sb.Append("<span class=\"versionNumber\">").Append(Enc(version.Length > 0 ? version : "Unknown version")).Append("</span>");
				if (v.stable)
					sb.Append("<span class=\"badge\">Stable</span>");
				string releaseDate = v.date == null ? null : FormatDate(v.date.Value);
				sb.Append("<span class=\"meta\">").Append(releaseDate ?? "Release date not provided").Append("</span>");
				sb.Append("</div>\n");
				string[] notes = SplitLines(v.notes);
				if (notes.Length > 0)
				{
					sb.Append("<ul class=\"notes\">\n");
					foreach (string note in notes)
						sb.Append("<li>").Append(Enc(note)).Append("</li>\n");
					sb.Append("</ul>\n");
				}
				sb.Append("</section>\n");
			}
		}

		/// <summary>
		/// Appends the changelog section.
		/// </summary>
		/// <param name="sb">StringBuilder the page is being built in.</param>
		/// <param name="changelogUrl">The "changelog_url" field of the update check data, or null to use the default.</param>
		/// <param name="nowMs">The current time in milliseconds since the Unix Epoch.</param>
		private static void AppendChangelog(StringBuilder sb, string changelogUrl, long nowMs)
		{
			string url = changelogUrl == null ? null : changelogUrl.Trim();
			if (string.IsNullOrEmpty(url) || !IsSafeHttpUrl(url))
				url = defaultChangelogUrl;
			// The current time is appended as a query parameter on purpose, and it must be generated afresh
			// for every request.  Mobile browsers cache this PDF aggressively and will go on serving an old
			// copy of it for days, long after Blue Iris has published a new one, which makes the link useless
			// exactly when it matters.  A timestamp which differs on every page load makes each link a URL
			// the browser has never fetched, so it has no cached copy to serve.
			url += (url.IndexOf('?') >= 0 ? "&" : "?") + "t=" + nowMs.ToString(CultureInfo.InvariantCulture);

			sb.Append("<h2>Changelog</h2>\n<div class=\"card\">\n");
			sb.Append("<p>Blue Iris publishes what changed in each version as a single PDF.</p>\n");
			sb.Append("<p><a class=\"button\" href=\"").Append(Attr(url)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">Open the Blue Iris 6 changelog (PDF)</a></p>\n");
			sb.Append("</div>\n");
		}

		/// <summary>
		/// <para>Formats a date from the update check data as a <c>&lt;time&gt;</c> element, e.g.
		/// <c>&lt;time datetime="2026-06-30"&gt;June 30, 2026&lt;/time&gt;</c>.</para>
		/// <para>These timestamps are built from date-only strings and are midnight UTC, so they are
		/// formatted in UTC and without a time of day.  Formatting them in any other time zone would shift
		/// some of them onto the day before or after the date Blue Iris actually published.</para>
		/// </summary>
		/// <param name="msSinceEpoch">A date in milliseconds since the Unix Epoch.</param>
		/// <returns>The date as HTML, or null if the value was not a usable date.</returns>
		private static string FormatDate(long msSinceEpoch)
		{
			try
			{
				DateTime date = TimeUtil.DateTimeFromEpochMS(msSinceEpoch);
				return BuildTimeElement(date);
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Formats a "yyyy-MM-dd" date string as a <c>&lt;time&gt;</c> element.
		/// </summary>
		/// <param name="str">A date string in "yyyy-MM-dd" format.</param>
		/// <param name="html">(Output) The date as HTML, or null if the string was not such a date.</param>
		/// <returns>True if the string was a "yyyy-MM-dd" date.</returns>
		private static bool TryFormatIsoDate(string str, out string html)
		{
			DateTime date;
			if (DateTime.TryParseExact(str, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
			{
				html = BuildTimeElement(date);
				return true;
			}
			html = null;
			return false;
		}

		/// <summary>
		/// Builds a <c>&lt;time&gt;</c> element containing the date part of the given value, written out for
		/// a reader and, in the "datetime" attribute, for a machine.  Both forms are in a fixed culture, so
		/// the page reads the same way wherever it is served from.
		/// </summary>
		/// <param name="date">The date to format.  Only its date part is used.</param>
		private static string BuildTimeElement(DateTime date)
		{
			return "<time datetime=\"" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\">"
				+ Enc(date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)) + "</time>";
		}

		/// <summary>
		/// <para>Returns true if the given string is an absolute http or https URL which is safe to put in an
		/// "href" attribute.</para>
		/// <para>URLs on this page come from Blue Iris's update server, so a scheme such as "javascript:"
		/// must not be allowed through, and neither must a value with whitespace or control characters in it.</para>
		/// </summary>
		/// <param name="str">A string which may be a URL.</param>
		private static bool IsSafeHttpUrl(string str)
		{
			if (string.IsNullOrEmpty(str))
				return false;
			if (!str.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !str.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				return false;
			foreach (char c in str)
				if (c <= ' ' || c == '\u007f')
					return false;
			return true;
		}

		/// <summary>
		/// Splits a multi-line field of the update check data into its non-empty lines, trimmed.  Returns an
		/// empty array if the field is null or holds nothing but whitespace.
		/// </summary>
		/// <param name="str">A field of the update check data in which newlines are significant.</param>
		private static string[] SplitLines(string str)
		{
			if (string.IsNullOrWhiteSpace(str))
				return new string[0];
			List<string> lines = new List<string>();
			foreach (string rawLine in str.Replace("\r\n", "\n").Split('\n'))
			{
				string line = rawLine.Trim();
				if (line.Length > 0)
					lines.Add(line);
			}
			return lines.ToArray();
		}

		/// <summary>
		/// HTML encodes text for insertion between tags, treating null as an empty string.
		/// </summary>
		/// <param name="str">Text which may contain HTML-reserved characters.</param>
		private static string Enc(string str)
		{
			return str == null ? "" : StringUtil.HtmlEncode(str);
		}

		/// <summary>
		/// HTML encodes text for insertion into an attribute value, treating null as an empty string.
		/// </summary>
		/// <param name="str">Text which may contain HTML-reserved characters.</param>
		private static string Attr(string str)
		{
			return str == null ? "" : StringUtil.HtmlAttributeEncode(str);
		}

		/// <summary>
		/// Everything on the page before the first heading, including the whole stylesheet.
		/// </summary>
		private const string documentHead = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<meta name=""description"" content=""Available versions of Blue Iris 6, with release dates and release notes."">
<title>Blue Iris Updates</title>
<style>
:root {
	--bg: #f4f5f7;
	--card: #ffffff;
	--fg: #1b1f24;
	--muted: #5d6671;
	--line: #dce0e6;
	--link: #1356c0;
	--badgeBg: #e2f0e4;
	--badgeFg: #1c6b33;
	--noticeBg: #fdf3e0;
	--noticeFg: #6a4c10;
	--noticeLine: #e0c279;
}
@media (prefers-color-scheme: dark) {
	:root {
		--bg: #15181c;
		--card: #1d2128;
		--fg: #e5e9ef;
		--muted: #9aa4b1;
		--line: #2d333c;
		--link: #7fb0ff;
		--badgeBg: #1d3626;
		--badgeFg: #8ed7a4;
		--noticeBg: #342c17;
		--noticeFg: #f0dcab;
		--noticeLine: #6a5a2c;
	}
}
* { box-sizing: border-box; }
body {
	margin: 0;
	padding: 28px 16px 56px;
	background: var(--bg);
	color: var(--fg);
	font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
	font-size: 16px;
	line-height: 1.55;
	-webkit-text-size-adjust: 100%;
}
main { max-width: 44rem; margin: 0 auto; }
h1 { font-size: 1.6rem; line-height: 1.25; margin: 0 0 6px; }
h2 {
	font-size: 0.82rem;
	font-weight: 600;
	text-transform: uppercase;
	letter-spacing: 0.08em;
	color: var(--muted);
	margin: 34px 0 12px;
}
p { margin: 0 0 10px; }
p:last-child { margin-bottom: 0; }
.lead { color: var(--muted); margin-bottom: 4px; }
.legend { color: var(--muted); font-size: 0.9rem; margin: -4px 0 12px; }
.card {
	background: var(--card);
	border: 1px solid var(--line);
	border-radius: 10px;
	padding: 14px 16px;
	margin: 0 0 10px;
}
.versionHead { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px 10px; }
.versionNumber {
	font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
	font-size: 1.2rem;
	font-weight: 600;
	letter-spacing: -0.01em;
}
.meta { color: var(--muted); font-size: 0.9rem; margin: 0; }
.badge {
	background: var(--badgeBg);
	color: var(--badgeFg);
	border-radius: 999px;
	padding: 2px 9px;
	font-size: 0.72rem;
	font-weight: 600;
	letter-spacing: 0.04em;
	text-transform: uppercase;
	white-space: nowrap;
}
.notes { margin: 10px 0 0; padding-left: 20px; }
.notes li { margin: 3px 0; }
.newsText { font-weight: 600; }
.notice {
	background: var(--noticeBg);
	color: var(--noticeFg);
	border: 1px solid var(--noticeLine);
	border-radius: 10px;
	padding: 14px 16px;
	margin: 22px 0 0;
}
.noticeHead { font-weight: 600; }
a { color: var(--link); }
a.button {
	display: inline-block;
	margin-top: 2px;
	padding: 9px 14px;
	border: 1px solid var(--line);
	border-radius: 8px;
	background: var(--bg);
	text-decoration: none;
	font-weight: 600;
}
a.button:hover { border-color: var(--link); }
footer {
	margin-top: 34px;
	padding-top: 14px;
	border-top: 1px solid var(--line);
	color: var(--muted);
	font-size: 0.85rem;
}
</style>
</head>
<body>
<main>
";
		/// <summary>
		/// Everything on the page after the last section.
		/// </summary>
		private const string documentFoot = @"<footer>
<p>The information on this page comes from Blue Iris&#39;s own update server and is cached briefly, so it can be slightly behind.</p>
<p>This page is not affiliated with Blue Iris Software.</p>
</footer>
</main>
</body>
</html>
";
	}
}
