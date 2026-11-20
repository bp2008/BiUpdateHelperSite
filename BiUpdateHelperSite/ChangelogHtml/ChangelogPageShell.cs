using System;
using BPUtil;

namespace BiUpdateHelperSite.ChangelogHtml
{
	/// <summary>
	/// <para>The HTML document which wraps every tier of the "/changelog" page: the structured
	/// conversion, the degraded text conversion, and the page which only links to the PDF.  All three
	/// share one shell so that a reader cannot tell which tier they got by the look of the page, only
	/// by what it says.</para>
	/// <para>The page is entirely self-contained: no external stylesheet, font, script or image, and no
	/// JavaScript at all.  The house style matches <see cref="Bi6UpdateCheck.UpdatesPage"/>.</para>
	/// </summary>
	public static class ChangelogPageShell
	{
		/// <summary>
		/// <para>URL of the Blue Iris 6 changelog PDF, which is the source this page is built from and
		/// which every tier of the page links to.</para>
		/// <para>This is the same URL <c>UpdateChecker</c> puts in its response.</para>
		/// </summary>
		public const string ChangelogPdfUrl = "https://blueirissoftware.com/changelog6.pdf";
		/// <summary>
		/// Title of the page, used when the converted document does not supply one of its own.
		/// </summary>
		public const string DefaultTitle = "Blue Iris 6 Change Log";

		/// <summary>
		/// Builds a complete HTML document.
		/// </summary>
		/// <param name="title">Title of the document, as plain text.</param>
		/// <param name="noticeHtml">A notice to show above the content, as HTML, or null for none.</param>
		/// <param name="bodyHtml">The content of the page, as HTML.</param>
		public static string BuildPage(string title, string noticeHtml, string bodyHtml)
		{
			if (string.IsNullOrWhiteSpace(title))
				title = DefaultTitle;
			string encodedTitle = StringUtil.HtmlEncode(title);
			return "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n"
				+ "<meta charset=\"utf-8\">\n"
				+ "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
				+ "<meta name=\"color-scheme\" content=\"light dark\">\n"
				+ "<meta name=\"description\" content=\"What changed in each version of Blue Iris 6, converted from Blue Iris&#39;s own changelog PDF.\">\n"
				+ "<title>" + encodedTitle + "</title>\n"
				+ "<style>\n" + css + "</style>\n</head>\n<body>\n<main>\n"
				+ "<h1>" + encodedTitle + "</h1>\n"
				+ "<p class=\"lead\">Blue Iris publishes this as a PDF.  This is that PDF, converted to a web page so that it reads on a phone, is searchable with Ctrl+F, and can be linked to one version at a time.</p>\n"
				+ (noticeHtml == null ? "" : noticeHtml)
				+ (bodyHtml == null ? "" : bodyHtml)
				+ documentFoot;
		}

		/// <summary>
		/// Builds a notice box, which every tier other than the structured conversion shows to explain
		/// what the reader is looking at.
		/// </summary>
		/// <param name="headline">First line of the notice, as plain text.</param>
		/// <param name="detail">The rest of the notice, as plain text.</param>
		public static string BuildNotice(string headline, string detail)
		{
			return "<div class=\"notice\">\n<p class=\"noticeHead\">" + StringUtil.HtmlEncode(headline) + "</p>\n"
				+ "<p>" + StringUtil.HtmlEncode(detail) + "</p>\n"
				+ "<p><a class=\"button\" href=\"" + ChangelogPdfUrl + "\" target=\"_blank\" rel=\"noopener noreferrer\">Open the Blue Iris 6 changelog (PDF)</a></p>\n"
				+ "</div>\n";
		}

		/// <summary>
		/// <para>Everything on the page after the content.</para>
		/// <para>The link to the PDF carries no cache-busting timestamp, unlike the one on the "/updates"
		/// page, because this page is cached on disk and served byte-for-byte to every visitor, so a
		/// value which differed per request would make the cached copy wrong.  Nobody needs the trick
		/// here anyway: a reader who follows this link already has a converted copy of the PDF's
		/// contents above it.</para>
		/// </summary>
		private const string documentFoot = @"<footer>
<p>This page is converted from Blue Iris&#39;s changelog PDF, which is checked for changes at most once a minute.  <a href=""" + ChangelogPdfUrl + @""" target=""_blank"" rel=""noopener noreferrer"">The original PDF</a> is the authoritative copy.</p>
<p><a href=""updates"">Blue Iris updates</a> &middot; This page is not affiliated with Blue Iris Software.</p>
</footer>
</main>
</body>
</html>
";
		/// <summary>
		/// The whole stylesheet.  Written out in full rather than shared with the "/updates" page,
		/// because the two pages display different things and only a little of it would be common.
		/// </summary>
		private const string css = @":root {
	--bg: #f4f5f7;
	--card: #ffffff;
	--fg: #1b1f24;
	--muted: #5d6671;
	--line: #dce0e6;
	--link: #1356c0;
	--thBg: #edeff2;
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
		--thBg: #262b33;
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
main { max-width: 46rem; margin: 0 auto; }
h1 { font-size: 1.6rem; line-height: 1.25; margin: 0 0 6px; }
.lead { color: var(--muted); margin: 0 0 18px; }
p { margin: 0 0 10px; }
p:last-child { margin-bottom: 0; }
a { color: var(--link); overflow-wrap: anywhere; }
details.release {
	background: var(--card);
	border: 1px solid var(--line);
	border-radius: 10px;
	padding: 2px 16px 10px;
	margin: 0 0 10px;
}
details.release > summary {
	cursor: pointer;
	margin: 0 -16px;
	padding: 10px 16px;
	list-style-position: inside;
}
details.release > summary:hover { color: var(--link); }
details.release[open] > summary { border-bottom: 1px solid var(--line); margin-bottom: 2px; }
details.release:target { border-color: var(--link); }
summary h2 {
	display: inline;
	font-size: 1.05rem;
	font-weight: 600;
	margin: 0;
}
.version { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; letter-spacing: -0.01em; }
.sep { color: var(--muted); font-weight: 400; }
.date { color: var(--muted); font-weight: 400; font-size: 0.92rem; }
h2 { font-size: 1.05rem; margin: 18px 0 8px; }
h3 {
	font-size: 0.82rem;
	font-weight: 600;
	text-transform: uppercase;
	letter-spacing: 0.08em;
	color: var(--muted);
	margin: 14px 0 4px;
}
ul, ol { margin: 4px 0 10px; padding-left: 1.4rem; }
ul.circle { list-style-type: circle; }
li { margin: 4px 0; }
li.lbl { list-style: none; margin-left: -1.1rem; }
li.lbl > span.lbl { color: var(--muted); margin-right: 0.35em; }
.tableWrap { overflow-x: auto; margin: 10px 0; }
table { border-collapse: collapse; width: 100%; font-size: 0.94em; }
th, td {
	border: 1px solid var(--line);
	padding: 5px 8px;
	text-align: left;
	vertical-align: top;
	overflow-wrap: anywhere;
}
thead th { background: var(--thBg); }
code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 0.92em; }
pre { white-space: pre-wrap; font: inherit; margin: 0; }
.notice {
	background: var(--noticeBg);
	color: var(--noticeFg);
	border: 1px solid var(--noticeLine);
	border-radius: 10px;
	padding: 14px 16px;
	margin: 0 0 18px;
}
.noticeHead { font-weight: 600; }
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
";
	}
}
