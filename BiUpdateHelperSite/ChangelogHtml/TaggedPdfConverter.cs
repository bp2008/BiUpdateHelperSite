using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace BiUpdateHelperSite.ChangelogHtml
{
	/// <summary>
	/// <para>Converts a <i>tagged</i> PDF (one which carries a <c>/StructTreeRoot</c>) to HTML by
	/// walking the logical structure tree and joining each structure element to the glyphs of its
	/// marked-content ranges.  Nothing here guesses structure from glyph coordinates.</para>
	/// <para>Blue Iris's changelog is produced by Microsoft Word, which tags it completely: every glyph
	/// on every page is reachable through the structure tree, and the tree carries real lists, tables
	/// and link annotations.  What Word does <i>not</i> do is apply heading styles, so heading level is
	/// recovered by the two heuristics in <see cref="WriteParagraph"/>.</para>
	/// <para>This class is not thread-safe and is used once per conversion.  It does not log; the
	/// caller logs <see cref="Warnings"/>, which is the early-warning system for Blue Iris changing how
	/// the PDF is produced.</para>
	/// <para><b>Four defects in the output come from the source document, not from this converter.</b>
	/// Three of them are reproduced as the PDF's own tags describe them, deliberately, because
	/// "correcting" a document which this converter cannot see is guesswork:</para>
	/// <list type="number">
	/// <item>The first body row of the request-parameter table in the 6.1.6.0 entry is tagged
	/// <c>/THead</c>, so the table's header row reads "key | required | Path below the Blue Iris
	/// key: ..." — the third cell is the description of the <c>key</c> parameter, not a column title.</item>
	/// <item>The long GitHub URL in the 6.0.1.20 entry is tagged as a top-level <c>/P</c> rather than as
	/// part of the list item it visually belongs to, so it renders as a paragraph after the list.</item>
	/// <item>The last cell of that same table is truncated ("...so the running") in the PDF itself;
	/// <c>pdftotext</c> shows the same truncation.</item>
	/// <item>The "Reply data" sub-list of the 6.1.6.0 entry is tagged as a nested <c>/L</c> inside the
	/// list item above it while its label paragraph sits at the top level after the request table, so
	/// the tag tree puts those bullets before the table and leaves the label with nothing under it.
	/// <b>This one is repaired</b>, by <see cref="RepairReadingOrder"/>, because the PDF contradicts
	/// itself here and the page's own reading order settles it.</item>
	/// </list>
	/// </summary>
	public class TaggedPdfConverter : IDisposable
	{
		/// <summary>
		/// <para>Whether to insert a space where a text run wraps onto a new line.</para>
		/// <para>Word keeps the space which caused a wrap at the end of the wrapped line, so adding one
		/// is redundant and corrupts a wrap which fell inside a token ("dword" becomes "d word").  This
		/// exists for a future producer which strips the trailing space instead; for Word output, false
		/// is correct and is what has been verified.</para>
		/// </summary>
		public static bool InsertSpaceAtLineWrap = false;

		private readonly PdfDocument doc;
		private readonly Structure structure;
		private readonly DictionaryToken structTreeRoot;
		/// <summary>
		/// Page object reference (object number, generation) to 1-based page number.  PdfPig does not
		/// expose a page's own object reference, so the <c>/Pages</c> tree is walked to build this, which
		/// is what resolves a structure element's <c>/Pg</c>.
		/// </summary>
		private readonly Dictionary<(long obj, int gen), int> pageNumberByRef = new Dictionary<(long, int), int>();
		/// <summary>
		/// (page number, marked-content ID) to the marked-content element, which carries the glyphs.
		/// </summary>
		private readonly Dictionary<(int page, int mcid), MarkedContentElement> markedContent = new Dictionary<(int, int), MarkedContentElement>();
		/// <summary>
		/// <para>(page number, marked-content ID) to the position of that marked-content range in the
		/// document's reading order, counting from the first range on page 1.</para>
		/// <para>Marked content appears in the page content stream in the order the page is read, so this
		/// is a reading-order index, and comparing two of these values answers "which of these two pieces
		/// of text comes first on the page".  That is what <see cref="RepairReadingOrder"/> needs.</para>
		/// </summary>
		private readonly Dictionary<(int page, int mcid), int> readingOrder = new Dictionary<(int, int), int>();
		/// <summary>
		/// The structure tree's <c>/RoleMap</c>: a producer-specific tag name to the standard tag it
		/// stands for.
		/// </summary>
		private readonly Dictionary<string, string> roleMap = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>
		/// <para>Anything the converter did not understand, for the caller to log.</para>
		/// <para>These are the early warning that Blue Iris has changed how the PDF is produced.</para>
		/// </summary>
		public List<string> Warnings { get; } = new List<string>();
		/// <summary>
		/// Number of pages in the document.
		/// </summary>
		public int PageCount => doc.NumberOfPages;
		/// <summary>
		/// Number of glyphs present on the pages of the document.
		/// </summary>
		public int GlyphsOnPages { get; private set; }
		/// <summary>
		/// <para>Number of glyphs which were reached through the structure tree and emitted.</para>
		/// <para>This is the main measure of whether the structure tree still describes the whole
		/// document.  It falls a few glyphs short of <see cref="GlyphsOnPages"/> even when all is well,
		/// because a list item's bullet character is replaced by a CSS list marker.</para>
		/// </summary>
		public int GlyphsEmitted { get; private set; }
		/// <summary>
		/// Number of release headings (<c>&lt;h2&gt;</c>) which were emitted.
		/// </summary>
		public int ReleaseHeadingCount { get; private set; }
		/// <summary>
		/// Number of list items (<c>&lt;li&gt;</c>) which were emitted.
		/// </summary>
		public int ListItemCount { get; private set; }
		/// <summary>
		/// Number of tables which were emitted.
		/// </summary>
		public int TableCount { get; private set; }
		/// <summary>
		/// <para>Number of structure elements whose tag this converter does not map to an HTML element,
		/// each of which was rendered as <c>&lt;div data-pdf-tag="..."&gt;</c>.</para>
		/// <para>Any value other than zero means the document now contains something which deserves a
		/// look, so it fails the sanity check in <see cref="GetQualityFailureReason"/>.</para>
		/// </summary>
		public int UnmappedTagCount { get; private set; }
		/// <summary>
		/// Number of structure elements which <see cref="RepairReadingOrder"/> moved because the
		/// structure tree disagreed with the page about where their text belongs.
		/// </summary>
		public int ReorderedElementCount { get; private set; }

		private TaggedPdfConverter(PdfDocument doc, DictionaryToken structTreeRoot)
		{
			this.doc = doc;
			this.structure = doc.Structure;
			this.structTreeRoot = structTreeRoot;
		}

		/// <summary>
		/// <para>Opens a PDF for structured conversion.  Returns false, with a null converter, when the
		/// PDF carries no usable logical structure, which is the signal for the caller to fall back to
		/// plain text extraction.</para>
		/// <para>This method throws if the bytes are not a PDF at all, or if the document is encrypted
		/// with a real password.</para>
		/// </summary>
		/// <param name="pdfBytes">The whole PDF file.</param>
		/// <param name="converter">(Output) The converter, or null if the PDF is not usably tagged.</param>
		public static bool TryOpen(byte[] pdfBytes, out TaggedPdfConverter converter)
		{
			converter = null;
			PdfDocument doc = PdfDocument.Open(pdfBytes);
			try
			{
				DictionaryToken catalog = doc.Structure.Catalog.CatalogDictionary;
				IToken rootToken;
				if (!catalog.Data.TryGetValue("StructTreeRoot", out rootToken))
				{
					doc.Dispose();
					return false;
				}
				int guard = 0;
				while (rootToken is IndirectReferenceToken && guard++ < 64)
					rootToken = doc.Structure.GetObject(((IndirectReferenceToken)rootToken).Data).Data;
				DictionaryToken rootDict = rootToken as DictionaryToken;
				if (rootDict == null || !rootDict.Data.ContainsKey("K"))
				{
					doc.Dispose();
					return false;
				}

				TaggedPdfConverter c = new TaggedPdfConverter(doc, rootDict);
				c.BuildIndexes();
				converter = c;
				return true;
			}
			catch
			{
				doc.Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			doc.Dispose();
		}

		#region Indexes

		/// <summary>
		/// Follows indirect references until a direct object is reached.
		/// </summary>
		/// <param name="token">A token which may be an indirect reference.</param>
		private IToken Resolve(IToken token)
		{
			int guard = 0;
			while (token is IndirectReferenceToken && guard++ < 64)
				token = structure.GetObject(((IndirectReferenceToken)token).Data).Data;
			return token;
		}

		/// <summary>
		/// Returns the value of a key of a dictionary, resolved, or null if the key is absent.
		/// </summary>
		/// <param name="dict">A dictionary token.</param>
		/// <param name="key">Name of the key to read.</param>
		private IToken Get(DictionaryToken dict, string key)
		{
			IToken value;
			if (!dict.Data.TryGetValue(key, out value))
				return null;
			return Resolve(value);
		}

		/// <summary>
		/// Builds the page number, marked content and role maps.  Called once, before conversion.
		/// </summary>
		private void BuildIndexes()
		{
			int pageCounter = 0;
			WalkPageTree(doc.Structure.Catalog.CatalogDictionary.Data["Pages"], ref pageCounter, 0);

			int orderCounter = 0;
			for (int p = 1; p <= doc.NumberOfPages; p++)
			{
				Page page = doc.GetPage(p);
				GlyphsOnPages += page.Letters.Count;
				foreach (MarkedContentElement mc in page.GetMarkedContents())
					IndexMarkedContent(mc, p, ref orderCounter);
			}

			DictionaryToken rm = Get(structTreeRoot, "RoleMap") as DictionaryToken;
			if (rm != null)
			{
				foreach (KeyValuePair<string, IToken> kvp in rm.Data)
				{
					NameToken name = Resolve(kvp.Value) as NameToken;
					if (name != null)
						roleMap[kvp.Key] = name.Data;
				}
			}
		}

		/// <summary>
		/// Walks the <c>/Pages</c> tree, numbering the pages in document order.
		/// </summary>
		/// <param name="node">A node of the page tree.</param>
		/// <param name="pageCounter">The number of pages numbered so far.</param>
		/// <param name="depth">Recursion depth, which guards against a page tree which refers to itself.</param>
		private void WalkPageTree(IToken node, ref int pageCounter, int depth)
		{
			if (depth > 64)
				return;
			(long, int) key = node is IndirectReferenceToken
				? ((long)((IndirectReferenceToken)node).Data.ObjectNumber, ((IndirectReferenceToken)node).Data.Generation)
				: (-1L, -1);
			DictionaryToken d = Resolve(node) as DictionaryToken;
			if (d == null)
				return;
			NameToken type = Get(d, "Type") as NameToken;
			if (type != null && type.Data == "Page")
			{
				pageNumberByRef[key] = ++pageCounter;
				return;
			}
			ArrayToken kids = Get(d, "Kids") as ArrayToken;
			if (kids != null)
				foreach (IToken kid in kids.Data)
					WalkPageTree(kid, ref pageCounter, depth + 1);
		}

		/// <summary>
		/// Records a marked-content range and its position in the document's reading order.
		/// </summary>
		/// <param name="mc">A marked-content element from a page.</param>
		/// <param name="page">1-based number of the page it is on.</param>
		/// <param name="orderCounter">The number of ranges recorded so far, across all pages.</param>
		private void IndexMarkedContent(MarkedContentElement mc, int page, ref int orderCounter)
		{
			if (mc.MarkedContentIdentifier >= 0)
			{
				markedContent[(page, mc.MarkedContentIdentifier)] = mc;
				readingOrder[(page, mc.MarkedContentIdentifier)] = orderCounter;
			}
			orderCounter++;
			foreach (MarkedContentElement child in mc.Children)
				IndexMarkedContent(child, page, ref orderCounter);
		}

		#endregion
		#region Structure tree

		/// <summary>
		/// Returns the standard structure tag of an element, with <c>/RoleMap</c> applied.
		/// </summary>
		/// <param name="el">A structure element.</param>
		private string TagOf(DictionaryToken el)
		{
			NameToken s = Get(el, "S") as NameToken;
			string tag = s == null ? "" : s.Data;
			string mapped;
			if (roleMap.TryGetValue(tag, out mapped))
				return mapped;
			return tag;
		}

		/// <summary>
		/// Returns the page an element's content is on, from its <c>/Pg</c> key, or the page inherited
		/// from its parent if it has none.
		/// </summary>
		/// <param name="el">A structure element.</param>
		/// <param name="inherited">The page number inherited from the element's parent.</param>
		private int PageOf(DictionaryToken el, int inherited)
		{
			IToken pg;
			if (el.Data.TryGetValue("Pg", out pg) && pg is IndirectReferenceToken)
			{
				IndirectReferenceToken r = (IndirectReferenceToken)pg;
				int p;
				if (pageNumberByRef.TryGetValue(((long)r.Data.ObjectNumber, r.Data.Generation), out p))
					return p;
			}
			return inherited;
		}

		/// <summary>
		/// Returns the attribute dictionaries of an element, which is the <c>/A</c> key, either one
		/// dictionary or an array of them.
		/// </summary>
		/// <param name="el">A structure element.</param>
		private IEnumerable<DictionaryToken> AttributeDicts(DictionaryToken el)
		{
			IToken a = Get(el, "A");
			DictionaryToken d = a as DictionaryToken;
			if (d != null)
				yield return d;
			else
			{
				ArrayToken arr = a as ArrayToken;
				if (arr != null)
					foreach (IToken x in arr.Data)
					{
						DictionaryToken dd = Resolve(x) as DictionaryToken;
						if (dd != null)
							yield return dd;
					}
			}
		}

		/// <summary>
		/// Returns the value of one of an element's attributes, as a string, or null if it has no such
		/// attribute.
		/// </summary>
		/// <param name="el">A structure element.</param>
		/// <param name="name">Name of the attribute, e.g. "ListNumbering".</param>
		private string Attribute(DictionaryToken el, string name)
		{
			foreach (DictionaryToken d in AttributeDicts(el))
			{
				IToken v;
				if (d.Data.TryGetValue(name, out v))
				{
					IToken resolved = Resolve(v);
					NameToken n = resolved as NameToken;
					if (n != null)
						return n.Data;
					NumericToken num = resolved as NumericToken;
					if (num != null)
						return num.Int.ToString();
					return resolved == null ? null : resolved.ToString();
				}
			}
			return null;
		}

		/// <summary>
		/// Follows a <c>/Link</c> element's <c>/OBJR</c> child to the link annotation's <c>/URI</c>
		/// action, which is where the whole URL lives even when the displayed text of it is split across
		/// a line wrap.
		/// </summary>
		/// <param name="el">A structure element whose tag is "Link".</param>
		/// <returns>The URL, or null if the element does not lead to one.</returns>
		private string FindHref(DictionaryToken el)
		{
			IToken kids;
			if (!el.Data.TryGetValue("K", out kids))
				return null;
			return FindHrefIn(kids, 0);
		}

		/// <summary>
		/// The recursive half of <see cref="FindHref"/>.
		/// </summary>
		/// <param name="node">A token beneath a <c>/Link</c> element.</param>
		/// <param name="depth">Recursion depth.</param>
		private string FindHrefIn(IToken node, int depth)
		{
			if (depth > 32)
				return null;
			node = Resolve(node);
			ArrayToken a = node as ArrayToken;
			if (a != null)
			{
				foreach (IToken x in a.Data)
				{
					string found = FindHrefIn(x, depth + 1);
					if (found != null)
						return found;
				}
				return null;
			}
			DictionaryToken d = node as DictionaryToken;
			if (d == null)
				return null;
			NameToken type = Get(d, "Type") as NameToken;
			if (type != null && type.Data == "OBJR")
			{
				DictionaryToken ann = Get(d, "Obj") as DictionaryToken;
				if (ann != null)
				{
					DictionaryToken action = Get(ann, "A") as DictionaryToken;
					if (action != null)
					{
						StringToken uri = Get(action, "URI") as StringToken;
						if (uri != null)
							return uri.Data;
					}
				}
				return null;
			}
			if (d.Data.ContainsKey("S"))
				return null; // Do not descend into a nested structure element.
			IToken k;
			if (d.Data.TryGetValue("K", out k))
				return FindHrefIn(k, depth + 1);
			return null;
		}

		/// <summary>
		/// Structure tags which this converter turns into a block of their own, as opposed to inline
		/// content of their parent.
		/// </summary>
		private static readonly HashSet<string> blockTags = new HashSet<string>(StringComparer.Ordinal)
		{
			"Document", "Part", "Sect", "Div", "Art", "P", "H", "H1", "H2", "H3", "H4", "H5", "H6",
			"L", "LI", "Lbl", "LBody", "Table", "THead", "TBody", "TFoot", "TR", "TH", "TD",
			"Figure", "Caption", "BlockQuote", "Note", "TOC", "TOCI", "Index", "Form"
		};

		/// <summary>
		/// Returns the structure elements beneath an element which are blocks, flattening the arrays and
		/// nested arrays which <c>/K</c> may be.
		/// </summary>
		/// <param name="el">A structure element.</param>
		private IEnumerable<DictionaryToken> BlockChildren(DictionaryToken el)
		{
			IToken kids;
			if (!el.Data.TryGetValue("K", out kids))
				return new DictionaryToken[0];
			List<DictionaryToken> result = new List<DictionaryToken>();
			FlattenChildren(kids, result, 0);
			return result.Where(c => c.Data.ContainsKey("S") && blockTags.Contains(TagOf(c)));
		}

		/// <summary>
		/// Collects the dictionaries in a <c>/K</c> value, which may be a dictionary, an array of them,
		/// or an array of arrays.
		/// </summary>
		/// <param name="node">A <c>/K</c> value or part of one.</param>
		/// <param name="result">List which the dictionaries are added to.</param>
		/// <param name="depth">Recursion depth.</param>
		private void FlattenChildren(IToken node, List<DictionaryToken> result, int depth)
		{
			if (depth > 32)
				return;
			node = Resolve(node);
			ArrayToken a = node as ArrayToken;
			if (a != null)
			{
				foreach (IToken x in a.Data)
					FlattenChildren(x, result, depth + 1);
				return;
			}
			DictionaryToken d = node as DictionaryToken;
			if (d != null)
				result.Add(d);
		}

		#endregion
		#region The block tree, and its reading order

		/// <summary>
		/// One block-level structure element, with the range of the document's reading order its text
		/// occupies.  The tree of these is built once, repaired, and then written out as HTML.
		/// </summary>
		private class StructNode
		{
			/// <summary>
			/// The structure element.
			/// </summary>
			public readonly DictionaryToken Element;
			/// <summary>
			/// Page the element's content is on.
			/// </summary>
			public readonly int Page;
			/// <summary>
			/// Standard structure tag of the element, with <c>/RoleMap</c> applied.
			/// </summary>
			public readonly string Tag;
			/// <summary>
			/// Position in the document's reading order of the first glyph beneath this element, or
			/// <see cref="int.MaxValue"/> if there are none.
			/// </summary>
			public int FirstGlyph = int.MaxValue;
			/// <summary>
			/// Position in the document's reading order of the last glyph beneath this element, or -1 if
			/// there are none.  This is not updated when a descendant is moved away, so it is only good
			/// for a diagnostic message.
			/// </summary>
			public int LastGlyph = -1;
			/// <summary>
			/// Block-level children, in the order they are to be written.
			/// </summary>
			public List<StructNode> Children = new List<StructNode>();
			/// <summary>
			/// True if there is any text beneath this element.
			/// </summary>
			public bool HasGlyphs => FirstGlyph != int.MaxValue;

			public StructNode(DictionaryToken element, int page, string tag)
			{
				Element = element;
				Page = page;
				Tag = tag;
			}
			/// <summary>
			/// Widens this node's reading-order range to include the given range.
			/// </summary>
			/// <param name="first">Start of a range to include.</param>
			/// <param name="last">End of a range to include.</param>
			public void Include(int first, int last)
			{
				if (first < FirstGlyph)
					FirstGlyph = first;
				if (last > LastGlyph)
					LastGlyph = last;
			}
		}

		/// <summary>
		/// Builds the block tree beneath a structure element, computing each node's reading-order range
		/// as it goes.
		/// </summary>
		/// <param name="el">A structure element.</param>
		/// <param name="page">The page its content is on.</param>
		/// <param name="depth">Recursion depth.</param>
		private StructNode BuildNode(DictionaryToken el, int page, int depth)
		{
			StructNode node = new StructNode(el, page, TagOf(el));
			if (depth <= 32)
			{
				foreach (DictionaryToken child in BlockChildren(el))
				{
					StructNode childNode = BuildNode(child, PageOf(child, page), depth + 1);
					node.Children.Add(childNode);
					if (childNode.HasGlyphs)
						node.Include(childNode.FirstGlyph, childNode.LastGlyph);
				}
			}
			MeasureOwnGlyphs(node, el, page, 0);
			return node;
		}

		/// <summary>
		/// <para>Widens a node's reading-order range to cover the marked content this element refers to
		/// directly, meaning its own inline content rather than its block children's.</para>
		/// <para>This follows the same traversal rule as <see cref="CollectInline"/>, so that the two
		/// agree about which text belongs to which node.</para>
		/// </summary>
		/// <param name="node">The node being measured.</param>
		/// <param name="token">A token beneath the node's element.</param>
		/// <param name="page">The page the content is on.</param>
		/// <param name="depth">Recursion depth.</param>
		private void MeasureOwnGlyphs(StructNode node, IToken token, int page, int depth)
		{
			if (depth > 32)
				return;
			token = Resolve(token);
			ArrayToken a = token as ArrayToken;
			if (a != null)
			{
				foreach (IToken x in a.Data)
					MeasureOwnGlyphs(node, x, page, depth + 1);
				return;
			}
			NumericToken num = token as NumericToken;
			if (num != null)
			{
				IncludeMcid(node, page, num.Int);
				return;
			}
			DictionaryToken d = token as DictionaryToken;
			if (d == null)
				return;
			if (depth > 0)
			{
				page = PageOf(d, page);
				if (!d.Data.ContainsKey("S"))
				{
					IToken mcid;
					if (d.Data.TryGetValue("MCID", out mcid))
					{
						NumericToken mn = Resolve(mcid) as NumericToken;
						if (mn != null)
							IncludeMcid(node, page, mn.Int);
						return;
					}
				}
				NameToken type = Get(d, "Type") as NameToken;
				if (type != null && type.Data == "OBJR")
					return;
				if (d.Data.ContainsKey("S") && blockTags.Contains(TagOf(d)))
					return; // A block of its own, measured as its own node.
			}
			IToken k;
			if (d.Data.TryGetValue("K", out k))
				MeasureOwnGlyphs(node, k, page, depth + 1);
		}

		/// <summary>
		/// Widens a node's reading-order range to include one marked-content range.
		/// </summary>
		/// <param name="node">The node being measured.</param>
		/// <param name="page">Page the marked content is on.</param>
		/// <param name="mcid">Marked-content ID.</param>
		private void IncludeMcid(StructNode node, int page, int mcid)
		{
			int order;
			if (readingOrder.TryGetValue((page, mcid), out order))
				node.Include(order, order);
		}

		/// <summary>
		/// <para>Reconciles the structure tree's idea of where text belongs with the page's.</para>
		/// <para><b>The structure tree is authoritative for nesting, but not for order.</b>  Word can
		/// tag an element as a child of one paragraph while printing its text somewhere else entirely:
		/// in Blue Iris's changelog it nests the "Reply data" sub-list inside the list item above it,
		/// and leaves that sub-list's label paragraph at the top level <i>after</i> the request table.
		/// Written out in tree order, those bullets appear before the table which is supposed to
		/// introduce them, and their label is left with nothing beneath it.</para>
		/// <para>Each marked-content range's position in the content stream says where its text actually
		/// is on the page, which gives every element a range in the document's reading order.  Two rules
		/// follow from it, and neither is specific to this document:</para>
		/// <list type="number">
		/// <item>A container's children are written in the order their text is read.  (A child with no
		/// text of its own has no opinion, so it keeps the position the tree gave it.)</item>
		/// <item>A child whose text begins at or after the point where its parent's own region ends -
		/// that is, at or after where the parent's next sibling begins - is not inside its parent on the
		/// page at all.  It is detached and re-inserted among the ancestors it does fit inside, at the
		/// position its text occupies.</item>
		/// </list>
		/// <para>Both rules do nothing at all where the tree and the page agree, which is everywhere
		/// else in this document.  A child which is detached keeps its own subtree; a disagreement
		/// nested inside a detached subtree is left alone, because the limit it would be judged against
		/// has moved.</para>
		/// </summary>
		/// <param name="node">The node whose children are to be put in order.</param>
		/// <param name="limit">Reading-order position at which this node's region ends.  A child whose
		/// text starts at or after this does not belong to this node.</param>
		/// <returns>Children which were detached and which the caller must re-insert.</returns>
		private List<StructNode> RepairReadingOrder(StructNode node, int limit)
		{
			List<StructNode> displaced = new List<StructNode>();
			List<StructNode> kept = new List<StructNode>();
			List<StructNode> children = SortByReadingOrder(node.Children);
			for (int i = 0; i < children.Count; i++)
			{
				StructNode child = children[i];
				if (child.HasGlyphs && child.FirstGlyph >= limit)
				{
					SortSubtree(child);
					NoteReordering(child, node);
					displaced.Add(child);
					continue;
				}
				// The child's own region ends where the next sibling with text begins, or where this
				// node's region ends, whichever comes first.
				int childLimit = limit;
				for (int j = i + 1; j < children.Count; j++)
					if (children[j].HasGlyphs)
					{
						childLimit = Math.Min(limit, children[j].FirstGlyph);
						break;
					}
				foreach (StructNode fromChild in RepairReadingOrder(child, childLimit))
				{
					if (fromChild.HasGlyphs && fromChild.FirstGlyph >= limit)
						displaced.Add(fromChild); // It does not fit here either; pass it further up.
					else
						kept.Add(fromChild);
				}
				kept.Add(child);
			}
			node.Children = SortByReadingOrder(kept);
			return displaced;
		}

		/// <summary>
		/// Records that an element was moved, both as a count and as a warning for the caller to log.
		/// A document which is tagged consistently produces none of these.
		/// </summary>
		/// <param name="moved">The element which was moved.</param>
		/// <param name="formerParent">The element it was tagged as a child of.</param>
		private void NoteReordering(StructNode moved, StructNode formerParent)
		{
			ReorderedElementCount++;
			Warnings.Add("structure element /" + moved.Tag + " on page " + moved.Page
				+ " is tagged inside /" + formerParent.Tag + " but its text is printed outside it;"
				+ " moved to where the page reads it");
		}

		/// <summary>
		/// Returns a node's children in the order their text is read.  The sort is stable and a child
		/// with no text of its own takes the position of the child before it, so children the page has
		/// no opinion about keep the order the structure tree gave them.
		/// </summary>
		/// <param name="children">Children of one node.</param>
		private static List<StructNode> SortByReadingOrder(List<StructNode> children)
		{
			List<KeyValuePair<int, StructNode>> keyed = new List<KeyValuePair<int, StructNode>>(children.Count);
			int previous = -1;
			foreach (StructNode child in children)
			{
				if (child.HasGlyphs)
					previous = child.FirstGlyph;
				keyed.Add(new KeyValuePair<int, StructNode>(previous, child));
			}
			return keyed.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
		}

		/// <summary>
		/// Puts a subtree's children in reading order without detaching anything.  Used on a subtree
		/// which is itself being moved.
		/// </summary>
		/// <param name="node">Root of the subtree.</param>
		private static void SortSubtree(StructNode node)
		{
			node.Children = SortByReadingOrder(node.Children);
			foreach (StructNode child in node.Children)
				SortSubtree(child);
		}

		#endregion
		#region Inline content

		/// <summary>
		/// A run of text which is styled all the same way.
		/// </summary>
		private class Run
		{
			public readonly string Text;
			public readonly bool Bold;
			public readonly bool Italic;
			public readonly bool Mono;
			public Run(string text, bool bold, bool italic, bool mono)
			{
				Text = text;
				Bold = bold;
				Italic = italic;
				Mono = mono;
			}
		}

		/// <summary>
		/// A piece of inline content: styled text, optionally all of it inside one hyperlink.
		/// </summary>
		private class Inline
		{
			public List<Run> Runs = new List<Run>();
			public string Href;
		}

		/// <summary>
		/// Returns true if a glyph is set in a monospace font.  The changelog has none, but a producer
		/// which sets code in Courier would land here.
		/// </summary>
		/// <param name="l">A glyph.</param>
		private static bool IsMono(Letter l)
		{
			string f = l.FontName;
			if (string.IsNullOrEmpty(f))
				return false;
			return f.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0
				|| f.IndexOf("Consol", StringComparison.OrdinalIgnoreCase) >= 0
				|| f.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Flattens the glyphs of one marked-content range into style runs.
		/// </summary>
		/// <param name="runs">List which the runs are added to.</param>
		/// <param name="letters">Glyphs of one marked-content range, in reading order.</param>
		private static void AppendLetters(List<Run> runs, IReadOnlyList<Letter> letters)
		{
			double lastY = 0;
			bool haveLastY = false;
			StringBuilder sb = new StringBuilder();
			bool bold = false, italic = false, mono = false, started = false;

			foreach (Letter l in letters)
			{
				double y = Math.Round(l.StartBaseLine.Y, 1);
				if (InsertSpaceAtLineWrap && haveLastY && Math.Abs(y - lastY) > 1.0)
				{
					// A line wrap: make sure the two lines do not run together.
					char tail = ' ';
					if (sb.Length > 0)
						tail = sb[sb.Length - 1];
					else if (runs.Count > 0 && runs[runs.Count - 1].Text.Length > 0)
						tail = runs[runs.Count - 1].Text[runs[runs.Count - 1].Text.Length - 1];
					if (!char.IsWhiteSpace(tail) && !l.Value.StartsWith(" "))
						sb.Append(' ');
				}
				lastY = y;
				haveLastY = true;

				bool b = l.FontDetails.IsBold, i = l.FontDetails.IsItalic, m = IsMono(l);
				if (!started)
				{
					bold = b;
					italic = i;
					mono = m;
					started = true;
				}
				else if (b != bold || i != italic || m != mono)
				{
					// Keep whitespace out of the style change, so that "<strong> </strong>" is never emitted.
					if (string.IsNullOrWhiteSpace(l.Value))
					{
						sb.Append(l.Value);
						continue;
					}
					if (sb.Length > 0)
						runs.Add(new Run(sb.ToString(), bold, italic, mono));
					sb.Clear();
					bold = b;
					italic = i;
					mono = m;
				}
				sb.Append(l.Value);
			}
			if (sb.Length > 0)
				runs.Add(new Run(sb.ToString(), bold, italic, mono));
		}

		/// <summary>
		/// Collects the inline content of an element, not descending into its block children.
		/// </summary>
		/// <param name="el">A structure element.</param>
		/// <param name="page">The page its content is on.</param>
		private List<Inline> CollectInline(DictionaryToken el, int page)
		{
			List<Inline> result = new List<Inline>();
			Inline current = new Inline();
			result.Add(current);
			IToken kids;
			if (el.Data.TryGetValue("K", out kids))
				CollectInlineFrom(kids, page, result, ref current, 0);
			return result;
		}

		/// <summary>
		/// The recursive half of <see cref="CollectInline"/>.
		/// </summary>
		/// <param name="node">A token beneath the element whose inline content is being collected.</param>
		/// <param name="page">The page the content is on.</param>
		/// <param name="result">List of inline pieces being built.</param>
		/// <param name="current">The inline piece text is currently being appended to.</param>
		/// <param name="depth">Recursion depth.</param>
		private void CollectInlineFrom(IToken node, int page, List<Inline> result, ref Inline current, int depth)
		{
			if (depth > 32)
				return;
			node = Resolve(node);
			ArrayToken a = node as ArrayToken;
			if (a != null)
			{
				foreach (IToken x in a.Data)
					CollectInlineFrom(x, page, result, ref current, depth + 1);
				return;
			}
			NumericToken num = node as NumericToken;
			if (num != null)
			{
				MarkedContentElement mc;
				if (markedContent.TryGetValue((page, num.Int), out mc))
				{
					AppendLetters(current.Runs, mc.Letters);
					GlyphsEmitted += mc.Letters.Count;
				}
				else
					Warnings.Add("missing marked content MCID " + num.Int + " on page " + page);
				return;
			}
			DictionaryToken d = node as DictionaryToken;
			if (d == null)
				return;

			page = PageOf(d, page);

			if (!d.Data.ContainsKey("S"))
			{
				IToken mcidToken;
				if (d.Data.TryGetValue("MCID", out mcidToken))
				{
					// A marked content reference dictionary.
					NumericToken mn = Resolve(mcidToken) as NumericToken;
					MarkedContentElement mc;
					if (mn != null && markedContent.TryGetValue((page, mn.Int), out mc))
					{
						AppendLetters(current.Runs, mc.Letters);
						GlyphsEmitted += mc.Letters.Count;
					}
					return;
				}
			}
			NameToken type = Get(d, "Type") as NameToken;
			if (type != null && type.Data == "OBJR")
				return;

			if (d.Data.ContainsKey("S"))
			{
				string tag = TagOf(d);
				if (tag == "Link")
				{
					Inline link = new Inline();
					link.Href = FindHref(d);
					result.Add(link);
					Inline linkCurrent = link;
					IToken linkKids;
					if (d.Data.TryGetValue("K", out linkKids))
						CollectInlineFrom(linkKids, page, result, ref linkCurrent, depth + 1);
					// Text after the link belongs to neither it nor what came before it.
					current = new Inline();
					result.Add(current);
					return;
				}
				if (blockTags.Contains(tag))
					return; // Handled by the block walker.
				// Span, Quote, Code, Reference, Annot and so on: transparent inline content.
			}
			IToken k;
			if (d.Data.TryGetValue("K", out k))
				CollectInlineFrom(k, page, result, ref current, depth + 1);
		}

		#endregion
		#region HTML output

		/// <summary>
		/// A release heading, e.g. "[6.1.6.1] – 2026-11-17", and also "[5.9.9.99 r10] – 2025-12-1".
		/// This is the most fragile line in the converter; if Blue Iris changes the format, release
		/// headings silently demote to <c>&lt;h3&gt;</c>, which the sanity check catches.
		/// </summary>
		private static readonly Regex versionHeading =
			new Regex(@"^\[\s*(?<v>\d+(?:\.\d+)*(?:[ \t]+[^\]]+?)?)\s*\]\s*(?:(?<s>[–—-])\s*(?<d>.+?))?\s*$", RegexOptions.Compiled);
		/// <summary>
		/// A URL written out as text, which the PDF does not carry a link annotation for.
		/// </summary>
		private static readonly Regex bareUrl =
			new Regex(@"\bhttps?://[^\s<>""')\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		/// <summary>
		/// The separator put between a version number and its date when the PDF's own separator could
		/// not be read.  The PDF uses an en dash.
		/// </summary>
		private const string defaultVersionDateSeparator = "–";

		private readonly StringBuilder output = new StringBuilder();
		private readonly HashSet<string> usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>
		/// True while a release's <c>&lt;details&gt;</c> element is open and waiting to be closed.
		/// </summary>
		private bool inRelease;
		/// <summary>
		/// True once the document's title has been seen, so that it is not repeated in the body.
		/// </summary>
		private bool titleEmitted;

		/// <summary>
		/// Converts the document to a complete HTML page.
		/// </summary>
		/// <param name="documentTitle">Title to use, or null to take the document's own.</param>
		/// <param name="pdfVersionToken">Token identifying the PDF this page is built from, for the link
		/// at the top of the page.  See the parameter of the same name on <see cref="ChangelogPageShell.BuildPage"/>.</param>
		public string Convert(string documentTitle = null, string pdfVersionToken = null)
		{
			output.Clear();
			DictionaryToken rootElement = BlockChildren(structTreeRoot).FirstOrDefault();
			if (rootElement == null)
				throw new InvalidOperationException("The PDF's structure tree has no root element.");

			StructNode root = BuildNode(rootElement, 1, 0);
			List<StructNode> leftOver = RepairReadingOrder(root, int.MaxValue);
			if (leftOver.Count > 0)
			{
				// Nothing can be displaced out of the root, which has no limit, but if it ever were, the
				// root is where it belongs.
				root.Children.AddRange(leftOver);
				root.Children = SortByReadingOrder(root.Children);
			}

			string title = documentTitle;
			if (title == null)
			{
				foreach (StructNode child in root.Children)
				{
					if (child.Tag == "H1" || child.Tag == "Title")
					{
						string t = PlainText(child);
						if (!string.IsNullOrWhiteSpace(t))
							title = t;
						break;
					}
				}
			}
			if (title == null)
				title = doc.Information.Title;

			WriteBlocks(root, 0);
			if (inRelease)
			{
				output.AppendLine("</details>");
				inRelease = false;
			}
			return ChangelogPageShell.BuildPage(title, null, output.ToString(), pdfVersionToken);
		}

		/// <summary>
		/// <para>Returns the reason the converted HTML should not be trusted, or null if it passes every
		/// check.</para>
		/// <para>These are the cheap signals that Blue Iris has changed how the PDF is produced.  The
		/// numbers are floors far below what this document actually produces (68,725 of 68,741 glyphs,
		/// 131 release headings, 473 list items, 105 KB), not targets.</para>
		/// </summary>
		/// <param name="html">The HTML which <see cref="Convert"/> returned.</param>
		public string GetQualityFailureReason(string html)
		{
			if (html == null || html.Length < 20000)
				return "the converted HTML is only " + (html == null ? 0 : html.Length) + " bytes";
			if (GlyphsOnPages > 0 && GlyphsEmitted < GlyphsOnPages * 0.95)
				return "only " + GlyphsEmitted + " of the " + GlyphsOnPages + " glyphs in the PDF were reached through its structure tree";
			if (ReleaseHeadingCount < 20)
				return "only " + ReleaseHeadingCount + " release headings were recognized";
			if (ListItemCount < 100)
				return "only " + ListItemCount + " list items were produced";
			if (UnmappedTagCount > 0)
				return UnmappedTagCount + " structure element(s) have a tag this converter does not understand";
			return null;
		}

		/// <summary>
		/// Returns all the text beneath a node, with its whitespace collapsed.
		/// </summary>
		/// <param name="node">A node of the block tree.</param>
		private string PlainText(StructNode node)
		{
			StringBuilder sb = new StringBuilder();
			foreach (Inline inl in CollectInline(node.Element, node.Page))
				foreach (Run r in inl.Runs)
					sb.Append(r.Text);
			foreach (StructNode child in node.Children)
				sb.Append(' ').Append(PlainText(child));
			return Normalize(sb.ToString());
		}

		/// <summary>
		/// Collapses whitespace, turns non-breaking spaces into ordinary ones and drops zero-width
		/// spaces.
		/// </summary>
		/// <param name="s">Text from the PDF.</param>
		private static string Normalize(string s)
		{
			s = s.Replace(' ', ' ').Replace("​", "");
			s = Regex.Replace(s, @"\s+", " ");
			return s.Trim();
		}

		private void Indent(int depth)
		{
			output.Append(new string(' ', depth * 2));
		}

		/// <summary>
		/// Writes every block child of a node.
		/// </summary>
		/// <param name="parent">A node of the block tree.</param>
		/// <param name="depth">Indentation depth, which is also what "top level" means to the heading heuristics.</param>
		private void WriteBlocks(StructNode parent, int depth)
		{
			foreach (StructNode child in parent.Children)
				WriteBlock(child, depth);
		}

		/// <summary>
		/// Writes one block.
		/// </summary>
		/// <param name="node">A node of the block tree.</param>
		/// <param name="depth">Indentation depth.</param>
		private void WriteBlock(StructNode node, int depth)
		{
			switch (node.Tag)
			{
				case "Document":
				case "Part":
				case "Sect":
				case "Div":
				case "Art":
					WriteBlocks(node, depth);
					return;

				case "H1":
				case "Title":
					// The page shell already prints the document title as <h1>.
					if (!titleEmitted)
					{
						titleEmitted = true;
						return;
					}
					WriteHeading(node, depth, 1);
					return;
				case "H2": WriteHeading(node, depth, 2); return;
				case "H3": WriteHeading(node, depth, 3); return;
				case "H4": WriteHeading(node, depth, 4); return;
				case "H5": WriteHeading(node, depth, 5); return;
				case "H6": WriteHeading(node, depth, 6); return;
				case "H": WriteHeading(node, depth, 2); return;

				case "P":
					WriteParagraph(node, depth);
					return;

				case "L":
					WriteList(node, depth);
					return;

				case "Table":
					WriteTable(node, depth);
					return;

				case "BlockQuote":
					Indent(depth);
					output.AppendLine("<blockquote>");
					WriteInlineAndBlocks(node, depth + 1, "p");
					Indent(depth);
					output.AppendLine("</blockquote>");
					return;

				case "Caption":
					Indent(depth);
					output.Append("<figcaption>").Append(RenderInline(node)).AppendLine("</figcaption>");
					return;

				case "Note":
					Indent(depth);
					output.AppendLine("<aside>");
					WriteInlineAndBlocks(node, depth + 1, "p");
					Indent(depth);
					output.AppendLine("</aside>");
					return;

				case "Figure":
					{
						StringToken alt = Get(node.Element, "Alt") as StringToken;
						Indent(depth);
						output.Append("<figure");
						if (alt != null && !string.IsNullOrWhiteSpace(alt.Data))
							output.Append(" aria-label=\"").Append(Escape(Normalize(alt.Data))).Append('"');
						output.AppendLine("></figure>");
						return;
					}

				default:
					// An unknown tag is the signal that the document has changed.  It is rendered rather
					// than dropped, so that no text is ever lost, and counted so that the caller can
					// decide the conversion is not to be trusted.
					UnmappedTagCount++;
					Warnings.Add("unmapped structure tag /" + node.Tag + " (page " + node.Page + ") rendered as <div>");
					Indent(depth);
					output.Append("<div data-pdf-tag=\"").Append(Escape(node.Tag)).AppendLine("\">");
					WriteInlineAndBlocks(node, depth + 1, "p");
					Indent(depth);
					output.AppendLine("</div>");
					return;
			}
		}

		/// <summary>
		/// Writes a node's own inline content, wrapped in the given element, and then its block children.
		/// </summary>
		/// <param name="node">A node of the block tree.</param>
		/// <param name="depth">Indentation depth.</param>
		/// <param name="inlineWrapper">Name of the element to wrap the inline content in.</param>
		private void WriteInlineAndBlocks(StructNode node, int depth, string inlineWrapper)
		{
			string inline = RenderInline(node);
			if (!string.IsNullOrWhiteSpace(inline))
			{
				Indent(depth);
				output.Append('<').Append(inlineWrapper).Append('>').Append(inline)
					.Append("</").Append(inlineWrapper).AppendLine(">");
			}
			WriteBlocks(node, depth);
		}

		/// <summary>
		/// Writes a heading which the PDF declared as one.
		/// </summary>
		/// <param name="node">A node of the block tree.</param>
		/// <param name="depth">Indentation depth.</param>
		/// <param name="level">Heading level, 1 to 6.</param>
		private void WriteHeading(StructNode node, int depth, int level)
		{
			string text = UnwrapWholeStrong(RenderInline(node));
			if (string.IsNullOrWhiteSpace(StripTags(text)))
				return;
			WriteHeadingText(depth, level, text);
		}

		/// <summary>
		/// <para>Writes a paragraph, which in this document may really be a heading.</para>
		/// <para>Word exported every heading in the changelog as a plain <c>/P</c>, so heading level has
		/// to be recovered from formatting plus shape: a top-level paragraph matching "[1.2.3] - date"
		/// is a release heading, and any other top-level paragraph which is entirely bold, short and
		/// does not end in a full stop is a section heading.</para>
		/// </summary>
		/// <param name="node">A node of the block tree whose tag is "P".</param>
		/// <param name="depth">Indentation depth.</param>
		private void WriteParagraph(StructNode node, int depth)
		{
			List<Inline> inlines = CollectInline(node.Element, node.Page);
			string raw = Normalize(string.Concat(inlines.SelectMany(i => i.Runs).Select(r => r.Text)));
			bool hasBlocks = node.Children.Count > 0;

			if (raw.Length == 0 && !hasBlocks)
				return; // A spacer paragraph.

			List<Run> allRuns = inlines.SelectMany(i => i.Runs).Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToList();
			bool allBold = allRuns.Count > 0 && allRuns.All(r => r.Bold);
			bool topLevel = depth == 0;

			Match m = versionHeading.Match(raw);
			if (topLevel && m.Success)
			{
				WriteReleaseHeading(m);
				return;
			}

			int innerDepth = inRelease && topLevel ? depth + 1 : depth;

			if (topLevel && allBold && raw.Length <= 60 && !raw.EndsWith("."))
			{
				WriteHeadingText(innerDepth, 3, UnwrapWholeStrong(RenderInlines(inlines)));
				return;
			}

			string html = RenderInlines(inlines);
			if (!string.IsNullOrWhiteSpace(StripTags(html)))
			{
				Indent(innerDepth);
				output.Append("<p>").Append(html).AppendLine("</p>");
			}
			if (hasBlocks)
				WriteBlocks(node, innerDepth);
		}

		/// <summary>
		/// <para>Writes the heading of one release, and opens the collapsible section which holds that
		/// release's entries.</para>
		/// <para>The section defaults to open: collapsing is an affordance for a reader who wants to
		/// skim 131 releases, not a default state.  Nothing is hidden from Ctrl+F, from "select all and
		/// copy", or from a reader whose browser does not do <c>&lt;details&gt;</c>.</para>
		/// <para>The version and the date are separated by the PDF's own separator <b>as text</b>, not
		/// by a CSS margin, so that copying a heading yields "6.1.6.1 – 2026-11-17" and not
		/// "6.1.6.12026-11-17".</para>
		/// <para>The version is deliberately not a link to its own anchor, even though the anchor exists
		/// and works.  A click on a link inside a <c>&lt;summary&gt;</c> also collapses the section, which
		/// the page's script could now prevent - it does exactly that for the copy button which sits
		/// beside this heading - so what keeps the link out is no longer a limitation, only that nothing
		/// has asked for it.</para>
		/// </summary>
		/// <param name="m">The match of <see cref="versionHeading"/> against the paragraph's text.</param>
		private void WriteReleaseHeading(Match m)
		{
			if (inRelease)
				output.AppendLine("</details>");
			string version = m.Groups["v"].Value;
			string id = UniqueId("v" + version);
			output.Append("<details class=\"release\" id=\"").Append(Escape(id)).AppendLine("\" open>");
			inRelease = true;
			ReleaseHeadingCount++;
			Indent(1);
			output.Append("<summary><h2><span class=\"version\">").Append(Escape(version)).Append("</span>");
			if (m.Groups["d"].Success)
			{
				string separator = m.Groups["s"].Success ? m.Groups["s"].Value : defaultVersionDateSeparator;
				output.Append("<span class=\"sep\"> ").Append(Escape(separator)).Append(" </span>")
					.Append("<span class=\"date\">").Append(Escape(m.Groups["d"].Value)).Append("</span>");
			}
			output.AppendLine("</h2></summary>");
		}

		/// <summary>
		/// Writes a heading element.
		/// </summary>
		/// <param name="depth">Indentation depth.</param>
		/// <param name="level">Heading level, 1 to 6.</param>
		/// <param name="html">Content of the heading, as HTML.</param>
		private void WriteHeadingText(int depth, int level, string html)
		{
			Indent(depth);
			output.Append("<h").Append(level).Append('>').Append(html).Append("</h").Append(level).AppendLine(">");
		}

		/// <summary>
		/// Turns a string into an HTML id which no other element on the page uses.
		/// </summary>
		/// <param name="seed">Text to build the id out of.</param>
		private string UniqueId(string seed)
		{
			string id = Regex.Replace(seed, @"[^A-Za-z0-9._-]+", "-").Trim('-');
			if (id.Length == 0)
				id = "s";
			string candidate = id;
			for (int i = 2; !usedIds.Add(candidate); i++)
				candidate = id + "-" + i;
			return candidate;
		}

		/// <summary>
		/// <c>/ListNumbering</c> values which mean an ordered list.
		/// </summary>
		private static readonly HashSet<string> orderedNumbering = new HashSet<string>(StringComparer.Ordinal)
		{ "Decimal", "UpperRoman", "LowerRoman", "UpperAlpha", "LowerAlpha" };

		/// <summary>
		/// Label glyphs which a CSS list marker already draws, so that the text of them is redundant.
		/// </summary>
		private static readonly HashSet<string> knownBullets = new HashSet<string>(StringComparer.Ordinal)
		{ "•", "o", "O", "○", "◦", "▪", "■", "§", "-", "–", "*", "·", "", "" };

		/// <summary>
		/// Writes a list and its items.
		/// </summary>
		/// <param name="node">A node of the block tree whose tag is "L".</param>
		/// <param name="depth">Indentation depth.</param>
		private void WriteList(StructNode node, int depth)
		{
			string numbering = Attribute(node.Element, "ListNumbering");
			if (numbering == null)
				numbering = "Disc";
			bool ordered = orderedNumbering.Contains(numbering);
			List<StructNode> items = node.Children.Where(c => c.Tag == "LI").ToList();
			if (items.Count == 0)
				return;

			int d = inRelease && depth == 0 ? depth + 1 : depth;
			Indent(d);
			if (ordered)
			{
				int? start = FirstOrdinal(items[0]);
				output.Append("<ol class=\"").Append(Escape(numbering.ToLowerInvariant())).Append('"');
				if (start.HasValue && start.Value > 1)
					output.Append(" start=\"").Append(start.Value).Append('"');
				output.AppendLine(">");
			}
			else
				output.Append("<ul class=\"").Append(Escape(numbering.ToLowerInvariant())).AppendLine("\">");

			foreach (StructNode li in items)
				WriteListItem(li, d + 1, ordered);

			Indent(d);
			output.AppendLine(ordered ? "</ol>" : "</ul>");
		}

		/// <summary>
		/// Writes one list item.
		/// </summary>
		/// <param name="li">A node of the block tree whose tag is "LI".</param>
		/// <param name="depth">Indentation depth.</param>
		/// <param name="ordered">True if the list is an ordered one.</param>
		private void WriteListItem(StructNode li, int depth, bool ordered)
		{
			Indent(depth);
			ListItemCount++;

			// If the label is not a bullet glyph which a CSS marker can stand in for, keep it: Word can
			// use a custom character, or a scheme it did not declare in /ListNumbering.
			string keepLabel = null;
			if (!ordered)
			{
				StructNode lbl = li.Children.FirstOrDefault(c => c.Tag == "Lbl");
				if (lbl != null)
				{
					string labelText = PlainText(lbl);
					if (labelText.Length > 0 && !knownBullets.Contains(labelText))
						keepLabel = labelText;
				}
			}
			if (keepLabel == null)
				output.Append("<li>");
			else
				output.Append("<li class=\"lbl\"><span class=\"lbl\">").Append(Escape(keepLabel)).Append("</span> ");

			List<StructNode> bodies = li.Children.Where(c => c.Tag == "LBody").ToList();
			if (bodies.Count == 0)
			{
				// A malformed LI without an LBody: take its own inline content.
				output.Append(RenderInline(li)).AppendLine("</li>");
				return;
			}

			List<string> inlineParts = new List<string>();
			foreach (StructNode b in bodies)
				inlineParts.Add(RenderInline(b));
			output.Append(string.Join(" ", inlineParts.Where(s => !string.IsNullOrWhiteSpace(s))));

			List<StructNode> nested = bodies.SelectMany(b => b.Children).ToList();
			if (nested.Count > 0)
			{
				output.AppendLine();
				foreach (StructNode nb in nested)
					WriteBlock(nb, depth + 1);
				Indent(depth);
			}
			output.AppendLine("</li>");
		}

		/// <summary>
		/// Returns the number an ordered list starts at, read from its first item's label, or null if
		/// the label does not contain one.
		/// </summary>
		/// <param name="li">The first item of an ordered list.</param>
		private int? FirstOrdinal(StructNode li)
		{
			StructNode lbl = li.Children.FirstOrDefault(c => c.Tag == "Lbl");
			if (lbl == null)
				return null;
			Match m = Regex.Match(PlainText(lbl), @"\d+");
			int v;
			if (m.Success && int.TryParse(m.Value, out v))
				return v;
			return null;
		}

		/// <summary>
		/// Writes a table.
		/// </summary>
		/// <param name="node">A node of the block tree whose tag is "Table".</param>
		/// <param name="depth">Indentation depth.</param>
		private void WriteTable(StructNode node, int depth)
		{
			int d = inRelease && depth == 0 ? depth + 1 : depth;
			TableCount++;
			// The wrapper is what lets a wide table scroll sideways on a phone instead of stretching the page.
			Indent(d);
			output.AppendLine("<div class=\"tableWrap\">");
			Indent(d + 1);
			output.AppendLine("<table>");
			foreach (StructNode part in node.Children)
			{
				switch (part.Tag)
				{
					case "THead": WriteRowGroup(part, d + 2, "thead"); break;
					case "TBody": WriteRowGroup(part, d + 2, "tbody"); break;
					case "TFoot": WriteRowGroup(part, d + 2, "tfoot"); break;
					case "TR": WriteRow(part, d + 2); break;
					case "Caption":
						Indent(d + 2);
						output.Append("<caption>").Append(RenderInline(part)).AppendLine("</caption>");
						break;
					default:
						Warnings.Add("unexpected /" + part.Tag + " inside /Table (page " + part.Page + ")");
						break;
				}
			}
			Indent(d + 1);
			output.AppendLine("</table>");
			Indent(d);
			output.AppendLine("</div>");
		}

		/// <summary>
		/// Writes a table's header, body or footer row group.
		/// </summary>
		/// <param name="node">A node of the block tree whose tag is "THead", "TBody" or "TFoot".</param>
		/// <param name="depth">Indentation depth.</param>
		/// <param name="name">Name of the HTML element to write.</param>
		private void WriteRowGroup(StructNode node, int depth, string name)
		{
			Indent(depth);
			output.Append('<').Append(name).AppendLine(">");
			foreach (StructNode tr in node.Children.Where(c => c.Tag == "TR"))
				WriteRow(tr, depth + 1);
			Indent(depth);
			output.Append("</").Append(name).AppendLine(">");
		}

		/// <summary>
		/// Writes one table row.
		/// </summary>
		/// <param name="node">A node of the block tree whose tag is "TR".</param>
		/// <param name="depth">Indentation depth.</param>
		private void WriteRow(StructNode node, int depth)
		{
			Indent(depth);
			output.AppendLine("<tr>");
			foreach (StructNode cell in node.Children)
			{
				if (cell.Tag != "TD" && cell.Tag != "TH")
					continue;
				bool isHeader = cell.Tag == "TH";
				string name = isHeader ? "th" : "td";
				Indent(depth + 1);
				output.Append('<').Append(name);
				string colSpan = Attribute(cell.Element, "ColSpan");
				if (colSpan != null && colSpan != "1")
					output.Append(" colspan=\"").Append(Escape(colSpan)).Append('"');
				string rowSpan = Attribute(cell.Element, "RowSpan");
				if (rowSpan != null && rowSpan != "1")
					output.Append(" rowspan=\"").Append(Escape(rowSpan)).Append('"');
				if (isHeader)
				{
					string scope = Attribute(cell.Element, "Scope");
					if (scope != null)
						output.Append(" scope=\"").Append(Escape(scope.ToLowerInvariant())).Append('"');
				}
				output.Append('>');

				string inline = RenderInline(cell);
				if (cell.Children.Count == 0)
					output.Append(inline);
				else if (cell.Children.Count == 1 && cell.Children[0].Tag == "P" && cell.Children[0].Children.Count == 0)
				{
					// A single paragraph in a cell needs no <p> wrapper.
					string cellHtml = RenderInline(cell.Children[0]);
					output.Append(isHeader ? UnwrapWholeStrong(cellHtml) : cellHtml);
				}
				else
				{
					if (!string.IsNullOrWhiteSpace(inline))
						output.Append(inline);
					output.AppendLine();
					foreach (StructNode b in cell.Children)
						WriteBlock(b, depth + 2);
					Indent(depth + 1);
				}
				output.Append("</").Append(name).AppendLine(">");
			}
			Indent(depth);
			output.AppendLine("</tr>");
		}

		#endregion
		#region Inline HTML

		/// <summary>
		/// Renders a node's own inline content as HTML.
		/// </summary>
		/// <param name="node">A node of the block tree.</param>
		private string RenderInline(StructNode node)
		{
			return RenderInlines(CollectInline(node.Element, node.Page));
		}

		/// <summary>
		/// Renders collected inline content as HTML.
		/// </summary>
		/// <param name="inlines">Inline content of one element.</param>
		private string RenderInlines(List<Inline> inlines)
		{
			StringBuilder sb = new StringBuilder();
			foreach (Inline inl in inlines)
			{
				StringBuilder inner = new StringBuilder();
				foreach (Run run in inl.Runs)
				{
					string text = run.Text.Replace(' ', ' ');
					if (text.Length == 0)
						continue;
					StringBuilder open = new StringBuilder();
					StringBuilder close = new StringBuilder();
					if (run.Mono)
					{
						open.Append("<code>");
						close.Insert(0, "</code>");
					}
					if (run.Bold)
					{
						open.Append("<strong>");
						close.Insert(0, "</strong>");
					}
					if (run.Italic)
					{
						open.Append("<em>");
						close.Insert(0, "</em>");
					}

					string escaped = Escape(text);
					if (inl.Href == null)
						escaped = AutoLink(escaped);
					if (string.IsNullOrWhiteSpace(text))
						inner.Append(escaped);
					else
						inner.Append(open).Append(escaped).Append(close);
				}
				string s = inner.ToString();
				if (inl.Href != null)
				{
					if (StripTags(s).Trim().Length == 0)
						continue;
					if (!IsSafeHttpUrl(inl.Href))
					{
						// A link annotation can carry any scheme at all, and this one came from a file
						// downloaded over the internet, so anything but http(s) is rendered as text.
						Warnings.Add("link annotation with an unusable URL was rendered as text");
						sb.Append(s);
						continue;
					}
					sb.Append("<a href=\"").Append(Escape(inl.Href)).Append("\">").Append(s.Trim()).Append("</a>");
				}
				else
					sb.Append(s);
			}
			return TidyEdges(CollapseWhitespace(MergeAdjacent(sb.ToString())));
		}

		/// <summary>
		/// Returns true if the given string is an absolute http or https URL which is safe to put in an
		/// "href" attribute.
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

		private static readonly Regex spaceInsideOpen = new Regex(@"<(strong|em|code)>(\s+)", RegexOptions.Compiled);
		private static readonly Regex spaceInsideClose = new Regex(@"(\s+)</(strong|em|code)>", RegexOptions.Compiled);
		private static readonly Regex emptyStyle = new Regex(@"<(strong|em|code)>\s*</\1>", RegexOptions.Compiled);

		/// <summary>
		/// Pushes whitespace out of inline tags, so that nothing ends up as "&lt;strong&gt;Fixed &lt;/strong&gt;".
		/// </summary>
		/// <param name="html">Inline HTML.</param>
		private static string TidyEdges(string html)
		{
			string previous;
			do
			{
				previous = html;
				html = spaceInsideOpen.Replace(html, m => m.Groups[2].Value + "<" + m.Groups[1].Value + ">");
				html = spaceInsideClose.Replace(html, m => "</" + m.Groups[2].Value + ">" + m.Groups[1].Value);
				html = emptyStyle.Replace(html, "");
			} while (html != previous);
			return html.Trim();
		}

		/// <summary>
		/// Removes a <c>&lt;strong&gt;</c> which wraps the whole of a heading, which is already bold.
		/// </summary>
		/// <param name="html">Inline HTML.</param>
		private static string UnwrapWholeStrong(string html)
		{
			Match m = Regex.Match(html, @"^<strong>((?:(?!</?strong>).)*)</strong>$", RegexOptions.Singleline);
			return m.Success ? m.Groups[1].Value : html;
		}

		/// <summary>
		/// Joins runs which the PDF split only because of font subsetting.
		/// </summary>
		/// <param name="html">Inline HTML.</param>
		private static string MergeAdjacent(string html)
		{
			string previous;
			do
			{
				previous = html;
				html = html.Replace("</strong><strong>", "")
					.Replace("</em><em>", "")
					.Replace("</code><code>", "")
					.Replace("</strong> <strong>", " ")
					.Replace("</em> <em>", " ")
					.Replace("</code> <code>", " ");
			} while (html != previous);
			return html;
		}

		private static string CollapseWhitespace(string s)
		{
			return Regex.Replace(s, @"[ \t\r\n]{2,}", " ");
		}

		/// <summary>
		/// Turns a URL written out as text into a link.
		/// </summary>
		/// <param name="escaped">Already-escaped text.</param>
		private static string AutoLink(string escaped)
		{
			return bareUrl.Replace(escaped, m => "<a href=\"" + m.Value + "\">" + m.Value + "</a>");
		}

		private static string StripTags(string s)
		{
			return Regex.Replace(s, "<[^>]*>", "");
		}

		/// <summary>
		/// Escapes text from the PDF for insertion into the page.  Everything in the document is
		/// untrusted: it is a file downloaded over the internet.
		/// </summary>
		/// <param name="s">Text from the PDF.</param>
		private static string Escape(string s)
		{
			return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
		}

		#endregion
	}
}
