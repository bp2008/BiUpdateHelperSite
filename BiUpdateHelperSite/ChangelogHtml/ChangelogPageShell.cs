using System;
using BPUtil;

namespace BiUpdateHelperSite.ChangelogHtml
{
	/// <summary>
	/// <para>The HTML document which wraps every tier of the "/changelog" page: the structured
	/// conversion, the degraded text conversion, and the page which only links to the PDF.  All three
	/// share one shell so that a reader cannot tell which tier they got by the look of the page, only
	/// by what it says.</para>
	/// <para>The page is self-contained: no external stylesheet, font, script or image.  It does carry
	/// one small inline script, which adds a "Copy" control to each release so that one version can be
	/// copied as rich text, as plain text, as Markdown or as HTML markup.  That is the script's only
	/// job, and it builds its own buttons, so a reader with JavaScript turned off is served exactly the
	/// page which existed before it: nothing is hidden, nothing is dead, and nothing but the extra
	/// control is lost.  The house style matches <see cref="Bi6UpdateCheck.UpdatesPage"/>.</para>
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
		/// <para>Reduces a PDF signature to a short token which is fit to put in a URL, which the
		/// signature itself is not: it can be an ETag with quotation marks in it, an HTTP date, or a
		/// length and a SHA-1.</para>
		/// <para>The result is twelve lowercase hexadecimal characters.  This is not a security
		/// boundary; the value only has to differ whenever the PDF differs.</para>
		/// </summary>
		/// <param name="pdfSignature">Signature of the PDF, or null if none is known.</param>
		/// <returns>The token, or null if there is no signature to derive one from.</returns>
		public static string BuildPdfVersionToken(string pdfSignature)
		{
			if (string.IsNullOrWhiteSpace(pdfSignature))
				return null;
			return Hash.GetSHA1Hex(pdfSignature).Substring(0, 12);
		}

		/// <summary>
		/// Builds a complete HTML document.
		/// </summary>
		/// <param name="title">Title of the document, as plain text.</param>
		/// <param name="noticeHtml">A notice to show above the content, as HTML, or null for none.</param>
		/// <param name="bodyHtml">The content of the page, as HTML.</param>
		/// <param name="pdfVersionToken">
		/// <para>A short token identifying the copy of the PDF this page was built from, from
		/// <see cref="BuildPdfVersionToken"/>, or null if that is not known.  It becomes the "?v=" on
		/// the link at the top of the page, and nothing else on the page carries it.</para>
		/// <para>The footer's link deliberately carries no cache-busting value at all, for the reason
		/// given on <see cref="documentFoot"/>: this page is cached on disk and served byte-for-byte to
		/// every visitor, so a value which differed per request - a timestamp, say - would be baked into
		/// the cached copy and would then be wrong for everyone served it afterwards.  A token derived
		/// from the PDF's own signature escapes that bind, because it is identical for every visitor
		/// served the same cached page, so the page stays byte-identical, and it changes exactly when
		/// the PDF changes, which is the one moment at which a stale copy in a phone's cache does any
		/// harm.</para>
		/// </param>
		public static string BuildPage(string title, string noticeHtml, string bodyHtml, string pdfVersionToken)
		{
			if (string.IsNullOrWhiteSpace(title))
				title = DefaultTitle;
			string encodedTitle = StringUtil.HtmlEncode(title);
			string pdfHref = ChangelogPdfUrl;
			if (!string.IsNullOrEmpty(pdfVersionToken))
				pdfHref += (pdfHref.IndexOf('?') >= 0 ? "&" : "?") + "v=" + pdfVersionToken;
			return "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n"
				+ "<meta charset=\"utf-8\">\n"
				+ "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
				+ "<meta name=\"color-scheme\" content=\"light dark\">\n"
				+ "<meta name=\"description\" content=\"What changed in each version of Blue Iris 6, converted from Blue Iris&#39;s own changelog PDF.\">\n"
				+ "<title>" + encodedTitle + "</title>\n"
				+ "<style>\n" + css + "</style>\n"
				+ "<script>\n" + copyScript + "</script>\n</head>\n<body>\n<main>\n"
				+ "<h1>" + encodedTitle + "</h1>\n"
				+ "<p class=\"lead\">Blue Iris publishes this as a PDF.  This is that PDF, converted to a web page so that it reads on a phone, is searchable with Ctrl+F, and can be linked to one version at a time.</p>\n"
				+ "<p class=\"pdfLink\"><a class=\"button\" href=\"" + StringUtil.HtmlAttributeEncode(pdfHref) + "\" target=\"_blank\" rel=\"noopener noreferrer\">Open the Blue Iris 6 changelog (PDF)</a></p>\n"
				+ (noticeHtml == null ? "" : noticeHtml)
				+ (bodyHtml == null ? "" : bodyHtml)
				+ documentFoot;
		}

		/// <summary>
		/// <para>Builds a notice box, which every tier other than the structured conversion shows to
		/// explain what the reader is looking at.</para>
		/// <para>It holds no link to the PDF of its own: <see cref="BuildPage"/> puts one at the top of
		/// every tier, a couple of centimetres above wherever this notice lands, so a second one here
		/// would be nothing but a duplicate of the button directly above it.</para>
		/// </summary>
		/// <param name="headline">First line of the notice, as plain text.</param>
		/// <param name="detail">The rest of the notice, as plain text.</param>
		public static string BuildNotice(string headline, string detail)
		{
			return "<div class=\"notice\">\n<p class=\"noticeHead\">" + StringUtil.HtmlEncode(headline) + "</p>\n"
				+ "<p>" + StringUtil.HtmlEncode(detail) + "</p>\n"
				+ "</div>\n";
		}

		/// <summary>
		/// <para>Everything on the page after the content.</para>
		/// <para>The link to the PDF carries no cache-busting value, unlike the one on the "/updates"
		/// page and unlike the one at the top of this page, because there is nothing useful left to put
		/// on it: a per-request timestamp cannot go into a page which is cached on disk and served
		/// byte-for-byte to every visitor, and the one value which can - see the "pdfVersionToken"
		/// parameter of <see cref="BuildPage"/> - is already on the link at the top, which is the one a
		/// reader who wants the PDF meets first.</para>
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
	/* Blue Iris writes registry paths and similar tokens into the prose, and a browser will not
	   break a line after a backslash, so one 53-character token such as the LocalDumps key in
	   6.1.3.11 pushed the whole page sideways on a phone.  break-word rather than anywhere,
	   because only the former is ignored when a browser works out intrinsic widths - see the
	   comment on th, td, where anywhere was crushing table columns for exactly that reason. */
	overflow-wrap: break-word;
}
main { max-width: 46rem; margin: 0 auto; }
h1 { font-size: 1.6rem; line-height: 1.25; margin: 0 0 6px; }
.lead { color: var(--muted); margin: 0 0 14px; }
.pdfLink { margin: 0 0 18px; }
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
/*
	break-word, not anywhere: ""anywhere"" is taken into account when the browser computes a column's
	min-content width, which lets the automatic table layout shrink a narrow column until every word
	in it breaks mid-word (""required"" came out as ""req uire d"") while the widest column keeps the
	space.  ""break-word"" is ignored for intrinsic sizing, so a column is never narrower than its
	longest word, and a single unbreakable token - a URL, a JSON blob - still breaks rather than
	overflowing.  A table which then comes out wider than the text column scrolls inside .tableWrap,
	which is much the better of the two failures.
*/
th, td {
	border: 1px solid var(--line);
	padding: 5px 8px;
	text-align: left;
	vertical-align: top;
	overflow-wrap: break-word;
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
/*
	The copy control exists only where the script built it, so the room for it is reserved only then:
	the ""js"" class is put on <html> by the script itself.
*/
html.js details.release > summary { position: relative; padding-right: 96px; }
.copyWrap { position: absolute; right: 16px; top: 7px; font-size: 0.8rem; font-weight: 400; }
.copyBtn {
	font: inherit;
	min-width: 70px;
	padding: 2px 9px;
	border: 1px solid var(--line);
	border-radius: 6px;
	background: var(--bg);
	color: var(--muted);
	white-space: nowrap;
	cursor: pointer;
}
.copyBtn:hover, .copyBtn:focus { border-color: var(--link); color: var(--link); }
.copyMenu { display: none; }
.copyWrap.open > .copyMenu {
	display: block;
	position: absolute;
	right: 0;
	top: calc(100% + 4px);
	z-index: 5;
	min-width: 9.5rem;
	padding: 4px;
	border: 1px solid var(--line);
	border-radius: 8px;
	background: var(--card);
	box-shadow: 0 6px 18px rgba(0, 0, 0, 0.22);
}
.copyMenu > button {
	display: block;
	width: 100%;
	font: inherit;
	padding: 6px 10px;
	border: 0;
	border-radius: 5px;
	background: none;
	color: var(--fg);
	text-align: left;
	white-space: nowrap;
	cursor: pointer;
}
.copyMenu > button:hover, .copyMenu > button:focus { background: var(--thBg); color: var(--link); }
footer {
	margin-top: 34px;
	padding-top: 14px;
	border-top: 1px solid var(--line);
	color: var(--muted);
	font-size: 0.85rem;
}
";
		/// <summary>
		/// <para>The whole script: a "Copy" control on each release, which copies that one release as
		/// rich text, as plain text, as Markdown or as HTML markup.</para>
		/// <para>It builds its own buttons rather than wiring up buttons the server wrote, so that a
		/// reader without JavaScript sees no control at all instead of a dead one.  Every format is
		/// produced by walking the release's own DOM, so nothing is fetched and the server needs no
		/// endpoint for this.</para>
		/// <para>Three of the four go onto the clipboard as plain text only, the "HTML source" one
		/// included: that one is "copy as HTML", not "copy rich text".  "Rich text" is the one which
		/// puts a "text/html" flavour on the clipboard, so that a paste into mail, a forum editor or a
		/// word processor keeps the heading, the bullets, the bold and the tables.  It is offered only
		/// where one of the two ways of doing that is actually available, because a menu item which
		/// silently does nothing is worse than one which is not there.</para>
		/// <para>It ships inside every cached copy of the page, which the gzipped response absorbs.</para>
		/// </summary>
		private const string copyScript = @"(function () {
	'use strict';
	var FORMATS = [['rich', 'Rich text'], ['text', 'Plain text'], ['md', 'Markdown'], ['html', 'HTML source']];
	var openWrap = null;

	function tidy(s) { return s.replace(/\s+/g, ' ').trim(); }
	function pad(s, w) { while (s.length < w) s += ' '; return s; }
	/* Keeps the whitespace around an emphasized run outside the Markdown markers, which otherwise
	   would not be recognized as emphasis at all. */
	function mark(inner, m) {
		var p = /^(\s*)([\s\S]*?)(\s*)$/.exec(inner);
		return p[2] ? p[1] + m + p[2] + m + p[3] : inner;
	}

	/* The text of one block.  The blocks nested inside it are left to the caller. */
	function inlineOf(node, fmt) {
		var out = '';
		for (var i = 0; i < node.childNodes.length; i++) {
			var c = node.childNodes[i], tag, inner;
			if (c.nodeType === 3) { out += c.nodeValue; continue; }
			if (c.nodeType !== 1) continue;
			tag = c.nodeName.toLowerCase();
			if (tag === 'ul' || tag === 'ol' || tag === 'div' || tag === 'table') continue;
			if (tag === 'br') { out += ' '; continue; }
			inner = inlineOf(c, fmt);
			if (fmt !== 'md') out += inner;
			else if (tag === 'strong' || tag === 'b') out += mark(inner, '**');
			else if (tag === 'em' || tag === 'i') out += mark(inner, '*');
			else if (tag === 'code') out += mark(inner, '`');
			else if (tag === 'a') out += '[' + tidy(inner) + '](' + (c.getAttribute('href') || '') + ')';
			else out += inner;
		}
		return out;
	}

	function rowsOf(table, fmt) {
		var rows = [], trs = table.querySelectorAll('tr');
		for (var i = 0; i < trs.length; i++) {
			var cells = [], kids = trs[i].children;
			for (var j = 0; j < kids.length; j++) {
				var n = kids[j].nodeName.toLowerCase();
				if (n === 'th' || n === 'td') cells.push(tidy(inlineOf(kids[j], fmt)));
			}
			if (cells.length) rows.push(cells);
		}
		return rows;
	}
	function widthOf(rows) {
		var w = 0;
		for (var i = 0; i < rows.length; i++) w = Math.max(w, rows[i].length);
		return w;
	}
	function mdTable(table) {
		var rows = rowsOf(table, 'md'), i;
		if (!rows.length) return '';
		var w = widthOf(rows);
		var line = function (r) {
			var c = r.slice();
			while (c.length < w) c.push('');
			return '| ' + c.map(function (x) { return x.replace(/\|/g, '\\|'); }).join(' | ') + ' |';
		};
		var out = [line(rows[0]), '|' + new Array(w + 1).join(' --- |')];
		for (i = 1; i < rows.length; i++) out.push(line(rows[i]));
		return out.join('\n');
	}
	/* Columns are padded so that they line up, except the last one and any column so wide that
	   padding the rest of its rows out to it would do more harm than good.  Nothing is wrapped: the
	   place the text is pasted into can do that better than this can. */
	function textTable(table) {
		var rows = rowsOf(table, 'text'), w = widthOf(rows), cols = [], out = [], cw, i, j;
		for (j = 0; j < w; j++) {
			cw = 0;
			for (i = 0; i < rows.length; i++)
				if (rows[i][j]) cw = Math.max(cw, rows[i][j].length);
			cols.push(cw > 24 ? 0 : cw);
		}
		for (i = 0; i < rows.length; i++) {
			var parts = [];
			for (j = 0; j < rows[i].length; j++)
				parts.push(j === rows[i].length - 1 ? rows[i][j] : pad(rows[i][j], cols[j]));
			out.push(parts.join('  ').replace(/\s+$/, ''));
		}
		return out.join('\n');
	}
	function tableIn(el, fmt) {
		var t = el.nodeName.toLowerCase() === 'table' ? el : el.querySelector('table');
		return t ? (fmt === 'md' ? mdTable(t) : textTable(t)) : '';
	}

	function listOf(list, depth, fmt) {
		var ordered = list.nodeName.toLowerCase() === 'ol';
		var n = parseInt(list.getAttribute('start') || '1', 10);
		var indent = new Array(depth + 1).join('  ');
		var lines = [], items = list.children;
		if (isNaN(n)) n = 1;
		for (var i = 0; i < items.length; i++) {
			if (items[i].nodeName.toLowerCase() !== 'li') continue;
			lines.push(indent + (ordered ? (n++) + '. ' : '- ') + tidy(inlineOf(items[i], fmt)));
			var kids = items[i].children;
			for (var j = 0; j < kids.length; j++) {
				var tag = kids[j].nodeName.toLowerCase();
				if (tag === 'ul' || tag === 'ol') lines.push(listOf(kids[j], depth + 1, fmt));
				else if (tag === 'div' || tag === 'table') lines.push(tableIn(kids[j], fmt));
			}
		}
		return lines.filter(function (s) { return s.length > 0; }).join('\n');
	}

	function headingOf(sec) {
		var h = sec.querySelector('summary h2');
		return h ? tidy(h.textContent) : '';
	}
	function bodyOf(sec, fmt) {
		var parts = [], kids = sec.children;
		for (var i = 0; i < kids.length; i++) {
			var el = kids[i], tag = el.nodeName.toLowerCase(), t;
			if (tag === 'summary') continue;
			if (/^h[1-6]$/.test(tag)) {
				t = tidy(inlineOf(el, fmt));
				parts.push(fmt === 'md' && t ? new Array(+tag.charAt(1) + 1).join('#') + ' ' + t : t);
			}
			else if (tag === 'ul' || tag === 'ol') parts.push(listOf(el, 0, fmt));
			else if (tag === 'div' || tag === 'table') parts.push(tableIn(el, fmt));
			else parts.push(tidy(inlineOf(el, fmt)));
		}
		return parts.filter(function (s) { return s.length > 0; }).join('\n\n');
	}
	function asText(sec, fmt) {
		var head = fmt === 'md' ? '## ' + headingOf(sec) : headingOf(sec);
		return (head + '\n\n' + bodyOf(sec, fmt)).replace(/\s+$/, '') + '\n';
	}

	/* One release, with the copy control taken back out and the <details>/<summary> wrapper replaced
	   by the plain <h2> heading it held.  The HTML source and the rich text are both built from this,
	   so the two can never disagree about what the section contains. */
	function plainClone(sec) {
		var clone = sec.cloneNode(true);
		var wrap = clone.querySelector('.copyWrap');
		var summary = clone.querySelector('summary');
		var h2, h;
		if (wrap) wrap.parentNode.removeChild(wrap);
		if (summary) {
			h = summary.querySelector('h2');
			h2 = document.createElement('h2');
			h2.innerHTML = h ? h.innerHTML.trim() : '';
			summary.parentNode.replaceChild(h2, summary);
		}
		return clone;
	}
	/* The markup as text.  The server's indentation is kept, less the one level which belonged to the
	   <details> wrapper. */
	function asHtml(sec) {
		return plainClone(sec).innerHTML.split('\n')
			.map(function (l) { return l.replace(/^  /, ''); })
			.join('\n').replace(/^(\s*\n)+/, '').replace(/\s+$/, '')
			.replace(/\n{3,}/g, '\n\n') + '\n';
	}
	/* The same content for the clipboard's ""text/html"" flavour.  This page's stylesheet does not
	   travel with it, so the little of it a table needs in order to still look like a table is put
	   onto the elements themselves. */
	function asRichHtml(sec) {
		var clone = plainClone(sec);
		var tables = clone.querySelectorAll('table');
		var cells = clone.querySelectorAll('th, td');
		var i;
		for (i = 0; i < tables.length; i++)
			tables[i].setAttribute('style', 'border-collapse: collapse');
		for (i = 0; i < cells.length; i++)
			cells[i].setAttribute('style', 'border: 1px solid #999999; padding: 4px 7px; text-align: left; vertical-align: top');
		return '<meta charset=""utf-8"">' + clone.innerHTML;
	}

	function legacyCopy(text) {
		var ta = document.createElement('textarea'), ok = false;
		ta.value = text;
		ta.setAttribute('readonly', '');
		ta.style.position = 'fixed';
		ta.style.left = '-9999px';
		document.body.appendChild(ta);
		ta.select();
		ta.setSelectionRange(0, ta.value.length);
		try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
		document.body.removeChild(ta);
		return ok;
	}
	/* The way to put two flavours on the clipboard without navigator.clipboard: a one-shot ""copy""
	   listener writes them onto the event.  The off-screen container is selected first because a
	   browser will not raise a copy event at all without a selection to copy. */
	function legacyCopyRich(html, text) {
		var holder = document.createElement('div');
		var selection = window.getSelection();
		var range = document.createRange();
		var ok = false;
		var onCopy = function (e) {
			e.clipboardData.setData('text/html', html);
			e.clipboardData.setData('text/plain', text);
			e.preventDefault();
		};
		holder.setAttribute('style', 'position: fixed; left: -9999px; top: 0');
		holder.innerHTML = html;
		document.body.appendChild(holder);
		range.selectNodeContents(holder);
		selection.removeAllRanges();
		selection.addRange(range);
		document.addEventListener('copy', onCopy, true);
		try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
		document.removeEventListener('copy', onCopy, true);
		selection.removeAllRanges();
		document.body.removeChild(holder);
		return ok;
	}
	/* True if either way of putting a ""text/html"" flavour on the clipboard exists here.  Asked once,
	   while the menu is being built, so that the item is simply absent where neither does. */
	function richSupported() {
		if (navigator.clipboard && navigator.clipboard.write && window.ClipboardItem)
			return true;
		try { return !!document.queryCommandSupported && document.queryCommandSupported('copy'); }
		catch (e) { return false; }
	}

	function flash(btn, msg) {
		btn.textContent = msg;
		clearTimeout(btn.flashTimer);
		btn.flashTimer = setTimeout(function () { btn.textContent = 'Copy'; }, 1500);
	}
	/* navigator.clipboard does not exist on an insecure origin, and this page can be reached over
	   plain HTTP, so the deprecated path is the one which really runs for some readers.  Either way
	   the button says what happened; it never claims a copy which did not happen. */
	function copyText(text, btn) {
		if (navigator.clipboard && navigator.clipboard.writeText)
			navigator.clipboard.writeText(text).then(
				function () { flash(btn, 'Copied'); },
				function () { flash(btn, legacyCopy(text) ? 'Copied' : 'Copy failed'); });
		else
			flash(btn, legacyCopy(text) ? 'Copied' : 'Copy failed');
	}
	/* Both flavours at once, so that a rich target keeps the structure while a plain one still gets
	   readable text rather than markup.  The ClipboardItem is constructed here, synchronously inside
	   the click, because Safari drops the user-gesture association across an await. */
	function copyRich(html, text, btn) {
		var item = null;
		if (navigator.clipboard && navigator.clipboard.write && window.ClipboardItem) {
			try {
				item = new ClipboardItem({
					'text/html': new Blob([html], { type: 'text/html' }),
					'text/plain': new Blob([text], { type: 'text/plain' })
				});
			} catch (e) { item = null; }
		}
		if (item)
			navigator.clipboard.write([item]).then(
				function () { flash(btn, 'Copied'); },
				function () { flash(btn, legacyCopyRich(html, text) ? 'Copied' : 'Copy failed'); });
		else
			flash(btn, legacyCopyRich(html, text) ? 'Copied' : 'Copy failed');
	}
	function copyAs(fmt, sec, btn) {
		if (fmt === 'rich') copyRich(asRichHtml(sec), asText(sec, 'text'), btn);
		else if (fmt === 'html') copyText(asHtml(sec), btn);
		else copyText(asText(sec, fmt), btn);
	}

	function closeMenu() {
		if (!openWrap) return;
		openWrap.classList.remove('open');
		openWrap.firstChild.setAttribute('aria-expanded', 'false');
		openWrap = null;
	}
	function enhance(sec, formats) {
		var summary = sec.querySelector('summary');
		if (!summary) return;
		var wrap = document.createElement('span');
		var btn = document.createElement('button');
		var menu = document.createElement('span');
		wrap.className = 'copyWrap';
		btn.type = 'button';
		btn.className = 'copyBtn';
		btn.textContent = 'Copy';
		btn.title = 'Copy this version';
		btn.setAttribute('aria-haspopup', 'true');
		btn.setAttribute('aria-expanded', 'false');
		menu.className = 'copyMenu';
		wrap.appendChild(btn);
		wrap.appendChild(menu);

		/* A click anywhere in a <summary> opens or closes the <details>, which is not what this
		   button is for, so neither the default action nor the <summary> ever sees the event. */
		btn.addEventListener('click', function (e) {
			var wasOpen = wrap.classList.contains('open');
			e.preventDefault();
			e.stopPropagation();
			closeMenu();
			if (!wasOpen) {
				wrap.classList.add('open');
				btn.setAttribute('aria-expanded', 'true');
				openWrap = wrap;
				menu.firstChild.focus();
			}
		});
		formats.forEach(function (f) {
			var item = document.createElement('button');
			item.type = 'button';
			item.textContent = f[1];
			item.addEventListener('click', function (e) {
				e.preventDefault();
				e.stopPropagation();
				closeMenu();
				btn.focus();
				copyAs(f[0], sec, btn);
			});
			menu.appendChild(item);
		});
		wrap.addEventListener('keydown', function (e) {
			if (e.key === 'Escape') { closeMenu(); btn.focus(); return; }
			if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
			var items = Array.prototype.slice.call(menu.children);
			var i = items.indexOf(document.activeElement);
			if (i < 0) return;
			e.preventDefault();
			items[(i + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length].focus();
		});
		summary.appendChild(wrap);
	}

	function init() {
		var secs = document.querySelectorAll('details.release');
		if (!secs.length) return;
		var rich = richSupported();
		var formats = FORMATS.filter(function (f) { return f[0] !== 'rich' || rich; });
		document.documentElement.className += ' js';
		for (var i = 0; i < secs.length; i++) enhance(secs[i], formats);
		document.addEventListener('click', closeMenu);
		document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeMenu(); });
	}
	if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
	else init();
})();
";
	}
}
