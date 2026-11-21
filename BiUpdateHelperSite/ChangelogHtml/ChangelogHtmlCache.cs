using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BPUtil;
using Newtonsoft.Json;

namespace BiUpdateHelperSite.ChangelogHtml
{
	/// <summary>
	/// Which of the three tiers of the "/changelog" page a reader is being served.
	/// </summary>
	public enum ChangelogTier
	{
		/// <summary>
		/// The PDF's logical structure was converted to headings, lists, tables and links.
		/// </summary>
		Structured,
		/// <summary>
		/// The PDF's text, in reading order, without any of its structure.
		/// </summary>
		Fallback,
		/// <summary>
		/// Nothing but a link to the PDF, because there is no converted copy to serve.
		/// </summary>
		LinkOnly
	}

	/// <summary>
	/// <para>One converted copy of the changelog, ready to serve.  Immutable.</para>
	/// </summary>
	public class ChangelogPage
	{
		/// <summary>
		/// The complete HTML document.
		/// </summary>
		public readonly string Html;
		/// <summary>
		/// <para>The value of the "ETag" response header for this page, quoted and ready to send, or
		/// null if this page must not be revalidated by a client.</para>
		/// <para>It is derived from the PDF's signature and the tier, so the same PDF converted the same
		/// way always produces the same ETag, on any instance of this service and after a restart.</para>
		/// </summary>
		public readonly string ETag;
		/// <summary>
		/// Which tier of the page this is.
		/// </summary>
		public readonly ChangelogTier Tier;
		/// <summary>
		/// <para>The signature of the PDF this page was built from: whatever identifies that exact file.</para>
		/// <para>See <see cref="ChangelogHtmlCache.ComputeSignature"/>.</para>
		/// </summary>
		public readonly string PdfSignature;
		/// <summary>
		/// Which property of the PDF <see cref="PdfSignature"/> was taken from, e.g. "ETag".
		/// </summary>
		public readonly string SignatureSource;
		/// <summary>
		/// When this page was converted (UTC).
		/// </summary>
		public readonly DateTime ConvertedUtc;

		/// <summary>
		/// Creates a page.
		/// </summary>
		/// <param name="html">The complete HTML document.</param>
		/// <param name="tier">Which tier of the page this is.</param>
		/// <param name="pdfSignature">Signature of the PDF it was built from, or null if it was not built from one.</param>
		/// <param name="signatureSource">Which property of the PDF the signature was taken from.</param>
		/// <param name="convertedUtc">When the page was converted (UTC).</param>
		public ChangelogPage(string html, ChangelogTier tier, string pdfSignature, string signatureSource, DateTime convertedUtc)
		{
			Html = html;
			Tier = tier;
			PdfSignature = pdfSignature;
			SignatureSource = signatureSource;
			ConvertedUtc = convertedUtc;
			// A page which is not built from a known PDF gets no ETag, because there is nothing for it to
			// mean: the next request may well be answered with different content.
			ETag = pdfSignature == null
				? null
				: "\"" + Hash.GetSHA1Hex(tier.ToString() + "|" + pdfSignature) + "\"";
		}
	}

	/// <summary>
	/// <para>Serves the Blue Iris 6 changelog as HTML, converted from the PDF Blue Iris publishes.</para>
	/// <para>This class is shaped like <see cref="Bi6UpdateCheck.UpdateChecker"/>, which solves the same
	/// problems: a rate-limited outbound request, an in-memory cache, state under
	/// <see cref="Globals.WritableDirectoryBase"/> so that a restart does not start from nothing, and
	/// nothing thrown out of the request path.</para>
	/// <para>What it adds is that a visitor never waits for the network when there is something to
	/// serve, and that the expensive part - downloading 400 KB and converting 54 pages - is skipped
	/// entirely unless the PDF has actually changed, which a conditional request establishes for free.</para>
	/// </summary>
	public static class ChangelogHtmlCache
	{
		/// <summary>
		/// Prefix used on every log line this class writes.
		/// </summary>
		private const string logTag = "changelog";
		/// <summary>
		/// <para>The shortest time between two requests to Blue Iris's server, in seconds, however many
		/// visitors arrive in between.</para>
		/// <para>This is the one number to change if the poll rate ever needs to come down; anything up
		/// to five minutes was considered fine.  It is also the length of the cache-busting window in
		/// <see cref="BuildFetchUrl"/>, so lowering the poll rate lowers the number of distinct URLs
		/// Cloudflare is asked to cache as well.</para>
		/// </summary>
		private const int minSecondsBetweenFetches = 60;
		/// <summary>
		/// <para>How long a request on a cold cache may wait for the PDF before giving up, in
		/// milliseconds.</para>
		/// <para>Only a cold cache ever waits at all, and the bound exists so that a server which
		/// accepts a connection and then says nothing cannot pin a request thread.</para>
		/// </summary>
		private const int coldFetchTimeoutMs = 15000;
		/// <summary>
		/// <para>The largest PDF this class will accept, in bytes.  Anything bigger is treated as a
		/// failed fetch and is not converted.</para>
		/// <para>The real file is 397 KB.  The converter holds the whole document in memory and needed
		/// 110 MB for 54 pages, so an absurd file is a way to exhaust this process's memory and must be
		/// refused on sight rather than opened.</para>
		/// </summary>
		private const int maxPdfBytes = 25 * 1024 * 1024;
		/// <summary>
		/// The most pages this class will convert, for the same reason as <see cref="maxPdfBytes"/>.
		/// The real file has 54.
		/// </summary>
		private const int maxPdfPages = 400;
		/// <summary>
		/// The shortest HTML document which is worth serving, in bytes.  Anything shorter means the
		/// conversion produced nothing useful.
		/// </summary>
		private const int minUsableHtmlLength = 20000;
		/// <summary>
		/// The most converter warnings to write to the log per conversion.  An untagged PDF can produce
		/// one per structure element, and the first few say everything the rest would.
		/// </summary>
		private const int maxWarningsToLog = 10;
		/// <summary>
		/// <para>User agent sent to Blue Iris's server.</para>
		/// <para>Generic on purpose: it names the software making the request and nothing about whoever
		/// is running it.</para>
		/// </summary>
		private const string userAgent = "BiUpdateHelperSite";
		/// <summary>
		/// Encoding used for the files written by this class.  UTF8 without a byte order mark.
		/// </summary>
		private static readonly Encoding utf8NoBom = new UTF8Encoding(false);
		/// <summary>
		/// Directory where this class keeps its persistent state.  Like the settings file and the
		/// database, this path is relative to the working directory.
		/// </summary>
		private static string DataDirectory => Globals.WritableDirectoryBase + "ChangelogHtmlCache/";
		/// <summary>
		/// Path of the file the last known good HTML is persisted to.
		/// </summary>
		private static string LastGoodHtmlPath => DataDirectory + "LastGood.html";
		/// <summary>
		/// Path of the file describing what is in <see cref="LastGoodHtmlPath"/>.
		/// </summary>
		private static string LastGoodMetadataPath => DataDirectory + "LastGood.json";
		/// <summary>
		/// Lock which serializes this class's file writes so that two background writes cannot collide.
		/// </summary>
		private static readonly object diskLock = new object();

		private static WebRequestUtility wru = new WebRequestUtility(userAgent, coldFetchTimeoutMs);
		/// <summary>
		/// <para>The converted page, cached so that a burst of visitors results in at most one request to
		/// Blue Iris's server per <see cref="minSecondsBetweenFetches"/>.</para>
		/// <para>The maximum age is effectively infinite because a stale page served immediately always
		/// beats a fresh one a visitor had to wait for; what bounds staleness is the refresh which
		/// reaching the minimum age triggers.</para>
		/// </summary>
		private static CachedObject<ChangelogPage> cachedPage = new CachedObject<ChangelogPage>(
			BuildPage,
			TimeSpan.FromSeconds(minSecondsBetweenFetches),
			TimeSpan.FromDays(3650),
			ex => Logger.Debug(ex, logTag));
		/// <summary>
		/// <para>The last page which was worth serving, which is what is served whenever a later fetch or
		/// conversion fails.</para>
		/// <para>This is persisted and loaded again by <see cref="Initialize"/>, so a restart does not
		/// force a reconversion and does not leave the first visitor waiting.</para>
		/// </summary>
		private static ChangelogPage lastGood = null;
		/// <summary>
		/// <para>The "ETag" of the PDF as last fetched, sent back as "If-None-Match" so that an unchanged
		/// PDF costs a 304 and no body.</para>
		/// <para>This is the validator of the most recent PDF <i>seen</i>, which is not always the one
		/// <see cref="lastGood"/> was built from: when a new PDF fails to convert, the old HTML goes on
		/// being served while the new PDF's validators are remembered, so that the failure is not
		/// re-downloaded and re-attempted every minute.</para>
		/// </summary>
		private static string pdfETag = null;
		/// <summary>
		/// The "Last-Modified" of the PDF as last fetched, sent back as "If-Modified-Since".
		/// </summary>
		private static string pdfLastModified = null;
		/// <summary>
		/// Signature of the most recent PDF seen, whether or not it converted.
		/// </summary>
		private static string lastSeenSignature = null;
		/// <summary>
		/// 1 while a background refresh is running, which keeps requests from starting another.
		/// </summary>
		private static int refreshRunning = 0;

		/// <summary>
		/// Loads this class's persistent state from disk.  Call this once during startup, before the web
		/// server begins accepting requests, so that the first visitor is served without waiting for
		/// Blue Iris's server.  This method does not throw.
		/// </summary>
		public static void Initialize()
		{
			try
			{
				if (!File.Exists(LastGoodMetadataPath) || !File.Exists(LastGoodHtmlPath))
					return;
				string json = File.ReadAllText(LastGoodMetadataPath, Encoding.UTF8);
				CachedPageMetadata meta = JsonConvert.DeserializeObject<CachedPageMetadata>(json);
				string html = File.ReadAllText(LastGoodHtmlPath, Encoding.UTF8);
				if (meta == null || string.IsNullOrEmpty(meta.pdfSignature) || html.Length < minUsableHtmlLength)
				{
					Logger.Debug(logTag + ": Ignoring unusable cache files in \"" + DataDirectory + "\"");
					return;
				}
				if (meta.htmlLength != html.Length)
				{
					// The two files are written separately, so a crash between the writes can leave them
					// describing different pages.  Then the HTML is still fine to serve, but the ETag it
					// would be served with is not trustworthy, so the cache is thrown away instead.
					Logger.Debug(logTag + ": Ignoring cache files which do not match each other in \"" + DataDirectory + "\"");
					return;
				}
				ChangelogTier tier;
				if (!Enum.TryParse(meta.tier, out tier) || tier == ChangelogTier.LinkOnly)
					return;
				lastGood = new ChangelogPage(html, tier, meta.pdfSignature, meta.signatureSource, meta.convertedUtc);
				lastSeenSignature = meta.pdfSignature;
				pdfETag = meta.pdfETag;
				pdfLastModified = meta.pdfLastModified;
			}
			catch (Exception ex)
			{
				lastGood = null;
				lastSeenSignature = null;
				pdfETag = null;
				pdfLastModified = null;
				Logger.Debug(ex, logTag);
			}
		}

		/// <summary>
		/// <para>Returns the page to serve.  This method does not throw.</para>
		/// <para>It waits for the network only when there is nothing at all to serve yet.</para>
		/// </summary>
		public static ChangelogPage GetPage()
		{
			try
			{
				ChangelogPage good = lastGood;
				if (good == null)
				{
					// A cold cache: this request waits for the fetch and the conversion.  CachedObject
					// serializes that, so a burst of first-time visitors makes one request between them,
					// and a failure is cached too, so a server which is down is not retried per visitor.
					return cachedPage.GetInstance();
				}
				// There is something worth serving, so no visitor waits on Blue Iris's server: the cached
				// page is returned now and the check for a newer PDF happens on a background thread.
				// CachedObject is still what decides whether that check does anything at all - it is what
				// enforces the once-a-minute floor and the single-flight guard - it is just never called
				// from a request thread while there is a page in hand.
				if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) == 0)
				{
					try
					{
						SetTimeout.OnBackground(() =>
						{
							try
							{
								cachedPage.GetInstance();
							}
							finally
							{
								Interlocked.Exchange(ref refreshRunning, 0);
							}
						}, 0, ex =>
						{
							Interlocked.Exchange(ref refreshRunning, 0);
							Logger.Debug(ex, logTag);
						});
					}
					catch
					{
						// Nothing was started, so the flag must not be left set: a flag which stayed set
						// would stop this page ever looking for a new PDF again.
						Interlocked.Exchange(ref refreshRunning, 0);
						throw;
					}
				}
				// Re-read, because a refresh may have replaced it since the start of this method.
				ChangelogPage current = lastGood;
				return current ?? good;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, logTag);
				ChangelogPage good = lastGood;
				return good ?? BuildLinkOnlyPage();
			}
		}

		/// <summary>
		/// <para>Fetches the PDF if it is time to, converts it if it has changed, and returns the page to
		/// serve.  This method does not throw.</para>
		/// <para>It is only ever called by <see cref="cachedPage"/>, which calls it under its own lock,
		/// so it is the only writer of this class's mutable state and does not need a lock of its own.</para>
		/// </summary>
		private static ChangelogPage BuildPage()
		{
			try
			{
				FetchResult fetch = FetchPdf();
				if (fetch.NotModified)
				{
					ChangelogPage unchanged = lastGood;
					if (unchanged != null)
						return unchanged;
					// A 304 with nothing cached to serve means the validators outlived the page they
					// belong to.  Drop them so that the next fetch asks for the whole file.
					pdfETag = null;
					pdfLastModified = null;
					return BuildLinkOnlyPage();
				}
				if (fetch.FailureReason != null)
				{
					Logger.Debug(logTag + ": Could not fetch the changelog PDF: " + fetch.FailureReason);
					ChangelogPage good = lastGood;
					return good ?? BuildLinkOnlyPage();
				}

				string signatureSource;
				string signature = ComputeSignature(fetch, out signatureSource);
				// Remembered before anything can go wrong with the conversion, so that a PDF which does
				// not convert is not downloaded again and again.
				pdfETag = fetch.ETag;
				pdfLastModified = fetch.LastModified;

				ChangelogPage existing = lastGood;
				if (existing != null && signature == lastSeenSignature)
					return existing; // This exact PDF has already been dealt with.
				lastSeenSignature = signature;

				ChangelogPage page = ConvertPdf(fetch.Body, signature, signatureSource);
				if (page == null)
				{
					// The last page which was worth serving outlives a PDF which cannot be converted.
					// Keeping a correct but stale page is the most valuable of the three tiers.
					ChangelogPage good = lastGood;
					return good ?? BuildLinkOnlyPage();
				}
				Logger.Info(logTag + ": Converted the changelog PDF (" + fetch.Body.Length + " bytes) to "
					+ page.Html.Length + " bytes of HTML.  Tier: " + page.Tier
					+ ".  PDF identified by its " + signatureSource + ".");
				SetLastGood(page);
				return page;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, logTag);
				ChangelogPage good = lastGood;
				return good ?? BuildLinkOnlyPage();
			}
		}

		/// <summary>
		/// <para>Converts the PDF, trying the structured conversion first and the degraded text
		/// conversion second.  This method does not throw.</para>
		/// </summary>
		/// <param name="pdfBytes">The whole PDF file.</param>
		/// <param name="signature">Signature of the PDF.</param>
		/// <param name="signatureSource">Which property of the PDF the signature was taken from.</param>
		/// <returns>The converted page, or null if neither conversion produced anything worth serving.</returns>
		private static ChangelogPage ConvertPdf(byte[] pdfBytes, string signature, string signatureSource)
		{
			try
			{
				// Reduced once here rather than in each converter, because every tier of the page links to the
				// same copy of the PDF and must therefore carry the same token.
				string pdfVersionToken = ChangelogPageShell.BuildPdfVersionToken(signature);
				TaggedPdfConverter converter = null;
				bool tagged = false;
				try
				{
					tagged = TaggedPdfConverter.TryOpen(pdfBytes, out converter);
					if (!tagged)
						Logger.Info(logTag + ": The changelog PDF carries no usable structure tree.  Falling back to text extraction.");
					else if (converter.PageCount > maxPdfPages)
					{
						Logger.Debug(logTag + ": Refusing to convert a changelog PDF of " + converter.PageCount + " pages.");
						return null;
					}
					else
					{
						string html = converter.Convert(null, pdfVersionToken);
						LogConverterWarnings(converter);
						string failureReason = converter.GetQualityFailureReason(html);
						if (failureReason == null)
							return new ChangelogPage(html, ChangelogTier.Structured, signature, signatureSource, DateTime.UtcNow);
						// This is the day Blue Iris changed how the PDF is produced.  It is worth saying
						// loudly, because everything still works and nobody would otherwise notice.
						Logger.Debug(logTag + ": The structured conversion of the changelog PDF is not trustworthy: " + failureReason
							+ ".  Falling back to text extraction.");
					}
				}
				finally
				{
					converter?.Dispose();
				}

				string fallbackHtml = FallbackConverter.Convert(pdfBytes, pdfVersionToken);
				if (fallbackHtml != null && fallbackHtml.Length >= minUsableHtmlLength)
					return new ChangelogPage(fallbackHtml, ChangelogTier.Fallback, signature, signatureSource, DateTime.UtcNow);
				Logger.Debug(logTag + ": Text extraction from the changelog PDF produced "
					+ (fallbackHtml == null ? 0 : fallbackHtml.Length) + " bytes, which is not worth serving.");
				return null;
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, logTag);
				return null;
			}
		}

		/// <summary>
		/// Writes a converter's warnings to the log.  These are the early warning that Blue Iris has
		/// changed how the PDF is produced, so they are logged even when the conversion was accepted.
		/// </summary>
		/// <param name="converter">A converter which has finished converting.</param>
		private static void LogConverterWarnings(TaggedPdfConverter converter)
		{
			if (converter.Warnings.Count == 0)
				return;
			StringBuilder sb = new StringBuilder();
			sb.Append(logTag).Append(": The changelog PDF produced ").Append(converter.Warnings.Count)
				.Append(" conversion warning(s).  Glyphs emitted: ").Append(converter.GlyphsEmitted)
				.Append(" of ").Append(converter.GlyphsOnPages).Append('.');
			for (int i = 0; i < converter.Warnings.Count && i < maxWarningsToLog; i++)
				sb.AppendLine().Append("  ").Append(converter.Warnings[i]);
			if (converter.Warnings.Count > maxWarningsToLog)
				sb.AppendLine().Append("  (").Append(converter.Warnings.Count - maxWarningsToLog).Append(" more)");
			Logger.Info(sb.ToString());
		}

		/// <summary>
		/// <para>Assigns <see cref="lastGood"/> and begins writing it to disk on a background thread, so
		/// that the request which triggered the conversion is not delayed by the file writes.</para>
		/// </summary>
		/// <param name="page">The page which was just converted.</param>
		private static void SetLastGood(ChangelogPage page)
		{
			if (page == null || page.Tier == ChangelogTier.LinkOnly)
				return;
			lastGood = page;
			string pdfETagAtConversion = pdfETag;
			string pdfLastModifiedAtConversion = pdfLastModified;
			SetTimeout.OnBackground(() =>
			{
				lock (diskLock)
				{
					// Whatever is current by the time the lock is acquired is the copy worth having on
					// disk, so a write which lost a race to a newer conversion writes the newer page
					// instead of overwriting it with an older one.
					ChangelogPage current = lastGood;
					if (current == null || current.Tier == ChangelogTier.LinkOnly)
						return;
					CachedPageMetadata meta = new CachedPageMetadata();
					meta.tier = current.Tier.ToString();
					meta.pdfSignature = current.PdfSignature;
					meta.signatureSource = current.SignatureSource;
					meta.convertedUtc = current.ConvertedUtc;
					meta.htmlLength = current.Html.Length;
					meta.pdfETag = current == page ? pdfETagAtConversion : pdfETag;
					meta.pdfLastModified = current == page ? pdfLastModifiedAtConversion : pdfLastModified;
					Directory.CreateDirectory(DataDirectory);
					// Written to a temporary file and moved into place so that a crash or a power loss
					// during a write cannot leave a truncated file to be loaded at the next startup.
					WriteFileAtomic(LastGoodHtmlPath, current.Html);
					WriteFileAtomic(LastGoodMetadataPath, JsonConvert.SerializeObject(meta, Formatting.Indented));
				}
			}, 0, ex => Logger.Debug(ex, logTag));
		}

		/// <summary>
		/// Writes a file by writing a temporary file beside it and moving that into place.
		/// </summary>
		/// <param name="path">Path of the file to write.</param>
		/// <param name="contents">Contents to write.</param>
		private static void WriteFileAtomic(string path, string contents)
		{
			string tmpPath = path + ".tmp";
			File.WriteAllText(tmpPath, contents, utf8NoBom);
			FileUtil.FileMove(tmpPath, path);
		}

		/// <summary>
		/// <para>Returns the signature of the PDF which was just fetched: whatever identifies that exact
		/// file, so that a converted copy of it can be cached and an ETag derived from it.</para>
		/// <para>The origin is IIS, whose ETag is a file index and change number and has been observed
		/// to be stable, so that is the first choice.  "Last-Modified" is the second.  Hashing the bytes
		/// is the last resort, and is the only one which costs anything.</para>
		/// </summary>
		/// <param name="fetch">A successful fetch.</param>
		/// <param name="source">(Output) Which property of the PDF the signature was taken from.</param>
		private static string ComputeSignature(FetchResult fetch, out string source)
		{
			if (!string.IsNullOrWhiteSpace(fetch.ETag))
			{
				source = "ETag";
				return "etag:" + fetch.ETag.Trim();
			}
			if (!string.IsNullOrWhiteSpace(fetch.LastModified))
			{
				source = "Last-Modified";
				return "modified:" + fetch.LastModified.Trim();
			}
			source = "length and SHA-1";
			return "sha1:" + fetch.Body.Length + ":" + Hash.GetSHA1Hex(fetch.Body);
		}

		/// <summary>
		/// <para>Builds the URL to fetch the PDF from, with a cache-busting query parameter.</para>
		/// <para>The origin sits behind Cloudflare with <c>Cache-Control: max-age=14400</c>, so without
		/// this parameter a newly published changelog can be invisible for up to four hours.  Cloudflare's
		/// cache key includes the query string, which was measured against the live URL: no query gave
		/// <c>cf-cache-status: REVALIDATED</c>, a query value Cloudflare had not seen gave <c>MISS</c>
		/// (so, the origin's answer), the same value again gave <c>HIT</c>, and <b>a conditional request
		/// on a fresh value still returned 304 with an empty body</b>.  So this parameter buys
		/// origin-authoritative freshness and costs nothing when the file has not changed.</para>
		/// <para>The value is the number of the current fetch window rather than a per-request timestamp,
		/// which is enough to force a miss on each real poll without filling Cloudflare's cache with an
		/// entry per request.</para>
		/// </summary>
		private static string BuildFetchUrl()
		{
			long window = TimeUtil.GetTimeInMsSinceEpoch() / 1000 / minSecondsBetweenFetches;
			string url = ChangelogPageShell.ChangelogPdfUrl;
			return url + (url.IndexOf('?') >= 0 ? "&" : "?") + "t=" + window.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// <para>Fetches the PDF, conditionally if a copy has been seen before.  This method does not
		/// throw.</para>
		/// </summary>
		/// <remarks>
		/// BPUtil's <see cref="WebRequestUtility"/> is used, the same client the update check uses: it
		/// takes arbitrary request headers as a flat array and exposes the response's headers and status
		/// code, which is everything a conditional request needs, so there is no reason to reach for
		/// <c>HttpClient</c> directly here.
		/// </remarks>
		private static FetchResult FetchPdf()
		{
			FetchResult result = new FetchResult();
			List<string> headers = new List<string>();
			headers.Add("Accept");
			headers.Add("application/pdf,*/*");
			if (!string.IsNullOrWhiteSpace(pdfETag))
			{
				headers.Add("If-None-Match");
				headers.Add(pdfETag);
			}
			if (!string.IsNullOrWhiteSpace(pdfLastModified))
			{
				headers.Add("If-Modified-Since");
				headers.Add(pdfLastModified);
			}

			// One byte beyond the limit is downloaded on purpose, so that a file which is exactly at the
			// limit is accepted and anything larger is recognized as too large instead of being silently
			// truncated into something which looks like a valid PDF.
			BpWebResponse response = wru.GET(BuildFetchUrl(), headers.ToArray(), maxPdfBytes + 1);

			if (response.StatusCode == 304)
			{
				result.NotModified = true;
				return result;
			}
			if (response.StatusCode != 200)
			{
				result.FailureReason = "the server responded with status code " + response.StatusCode
					+ (response.ex == null ? "" : " (" + response.ex.Message + ")");
				return result;
			}
			long contentLength;
			string contentLengthHeader = GetHeader(response, "Content-Length");
			if (contentLengthHeader != null && long.TryParse(contentLengthHeader.Trim(), out contentLength) && contentLength > maxPdfBytes)
			{
				result.FailureReason = "the server offered " + contentLength + " bytes, over the " + maxPdfBytes + " byte limit";
				return result;
			}
			if (response.data == null || response.data.Length == 0)
			{
				result.FailureReason = "the server returned an empty body";
				return result;
			}
			if (response.data.Length > maxPdfBytes)
			{
				result.FailureReason = "the server returned more than the " + maxPdfBytes + " byte limit";
				return result;
			}
			result.Body = response.data;
			result.ETag = GetHeader(response, "ETag");
			result.LastModified = GetHeader(response, "Last-Modified");
			return result;
		}

		/// <summary>
		/// Returns one header of a response, or null if it has no such header.  The keys of
		/// <see cref="BpWebResponse.headers"/> are case-sensitive, which the names a server chooses are
		/// not, so the lookup is done case-insensitively.
		/// </summary>
		/// <param name="response">A response from Blue Iris's server.</param>
		/// <param name="name">Name of the header to read.</param>
		private static string GetHeader(BpWebResponse response, string name)
		{
			if (response.headers == null)
				return null;
			foreach (KeyValuePair<string, string> kvp in response.headers)
				if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
					return kvp.Value;
			return null;
		}

		/// <summary>
		/// Builds the last tier of the page: no converted content at all, just an explanation and the
		/// link to the PDF.  Never an empty page.
		/// </summary>
		private static ChangelogPage BuildLinkOnlyPage()
		{
			string notice = ChangelogPageShell.BuildNotice(
				"The changelog could not be loaded right now.",
				"Blue Iris's changelog PDF could not be fetched or read.  This page retries on its own, so refreshing in a minute may be all it takes; in the meantime the PDF itself is linked at the top of this page.");
			// No PDF was fetched, so there is no signature to derive a token from and the link at the top of
			// the page gets no "?v=" at all.
			return new ChangelogPage(ChangelogPageShell.BuildPage(null, notice, "", null), ChangelogTier.LinkOnly, null, null, DateTime.UtcNow);
		}

		/// <summary>
		/// The outcome of one attempt to fetch the PDF.
		/// </summary>
		private class FetchResult
		{
			/// <summary>
			/// True if the server answered 304, meaning the PDF has not changed since it was last fetched.
			/// </summary>
			public bool NotModified;
			/// <summary>
			/// The PDF, or null if it was not fetched.
			/// </summary>
			public byte[] Body;
			/// <summary>
			/// Value of the response's "ETag" header, or null if it had none.
			/// </summary>
			public string ETag;
			/// <summary>
			/// Value of the response's "Last-Modified" header, or null if it had none.
			/// </summary>
			public string LastModified;
			/// <summary>
			/// Why the fetch did not produce a PDF, or null if it did or if the PDF has not changed.
			/// </summary>
			public string FailureReason;
		}

		/// <summary>
		/// What is written beside the cached HTML to describe it.  This is the on-disk format, so a
		/// field removed from it is a field an older cache file still carries.
		/// </summary>
		private class CachedPageMetadata
		{
			public string tier;
			public string pdfSignature;
			public string signatureSource;
			public DateTime convertedUtc;
			public int htmlLength;
			public string pdfETag;
			public string pdfLastModified;
		}
	}
}
