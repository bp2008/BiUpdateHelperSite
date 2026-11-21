using System;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace BiUpdateHelperSite.ChangelogHtml
{
	/// <summary>
	/// <para>What to serve when the structure tree is missing or the structured conversion cannot be
	/// trusted: the PDF's text in reading order, wrapped in a <c>&lt;pre&gt;</c> which wraps on a
	/// phone.</para>
	/// <para>It is unstyled and has no headings, lists or tables, but it is never wrong and it is still
	/// fully searchable and copyable, which is most of why this page exists.</para>
	/// </summary>
	public static class FallbackConverter
	{
		/// <summary>
		/// Converts a PDF to a degraded HTML page.  Throws if the bytes cannot be opened as a PDF at all.
		/// </summary>
		/// <param name="pdfBytes">The whole PDF file.</param>
		/// <param name="pdfVersionToken">Token identifying the PDF this page is built from, for the link
		/// at the top of the page.  See the parameter of the same name on <see cref="ChangelogPageShell.BuildPage"/>.</param>
		public static string Convert(byte[] pdfBytes, string pdfVersionToken = null)
		{
			StringBuilder sb = new StringBuilder();
			string title = null;
			using (PdfDocument doc = PdfDocument.Open(pdfBytes))
			{
				for (int p = 1; p <= doc.NumberOfPages; p++)
					sb.AppendLine(ContentOrderTextExtractor.GetText(doc.GetPage(p)));
				title = doc.Information.Title;
			}
			string text = sb.ToString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
			string notice = ChangelogPageShell.BuildNotice(
				"This is a plain text rendering of the changelog.",
				"The PDF could not be converted into a structured page this time, so its text is shown as it was extracted.  Nothing is missing, but the headings, lists and tables are not marked up.");
			return ChangelogPageShell.BuildPage(title, notice, "<pre>" + text + "</pre>\n", pdfVersionToken);
		}
	}
}
