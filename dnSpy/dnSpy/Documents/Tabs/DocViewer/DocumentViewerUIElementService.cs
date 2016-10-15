﻿/*
    Copyright (C) 2014-2016 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Text;
using dnSpy.Contracts.Text.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace dnSpy.Documents.Tabs.DocViewer {
	static class DocumentViewerUIElementConstants {
		public const string CustomDataId = "DocumentViewerUIElement-CustomDataId";
		public const string ContentDataId = "DocumentViewerUIElement-ContentDataId";
	}

	struct DocumentViewerUIElement {
		public int Position { get; }
		public Func<UIElement> CreateElement { get; }
		public DocumentViewerUIElement(int position, Func<UIElement> createElement) {
			if (position < 0)
				throw new ArgumentOutOfRangeException(nameof(position));
			if (createElement == null)
				throw new ArgumentNullException(nameof(createElement));
			Position = position;
			CreateElement = createElement;
		}
	}

	sealed class DocumentViewerUIElementCollection {
		public static readonly DocumentViewerUIElementCollection Empty = new DocumentViewerUIElementCollection(Array.Empty<DocumentViewerUIElement>());

		public int Count => elements.Length;
		public DocumentViewerUIElement this[int index] => elements[index];

		readonly DocumentViewerUIElement[] elements;

		public DocumentViewerUIElementCollection(DocumentViewerUIElement[] elements) {
			if (elements == null)
				throw new ArgumentNullException(nameof(elements));
			// Most of the time it's already sorted, and if it isn't, use OrderBy and not Array.Sort
			// because we want a stable sort.
			this.elements = IsSorted(elements) ? elements : elements.OrderBy(a => a, DocumentViewerUIElementComparer.Instance).ToArray();
		}

		sealed class DocumentViewerUIElementComparer : IComparer<DocumentViewerUIElement> {
			public static readonly DocumentViewerUIElementComparer Instance = new DocumentViewerUIElementComparer();
			public int Compare(DocumentViewerUIElement x, DocumentViewerUIElement y) => x.Position - y.Position;
		}

		static bool IsSorted(DocumentViewerUIElement[] elements) {
			if (elements.Length <= 1)
				return true;
			int prevPosition = elements[0].Position;
			for (int i = 1; i < elements.Length; i++) {
				int position = elements[i].Position;
				if (prevPosition > position)
					return false;
				prevPosition = position;
			}
			return true;
		}

		public int GetStartIndex(int position) {
			var array = elements;
			int lo = 0, hi = array.Length - 1;
			while (lo <= hi) {
				int index = (lo + hi) / 2;

				var spanData = array[index];
				if (position < spanData.Position)
					hi = index - 1;
				else if (position > spanData.Position)
					lo = index + 1;
				else
					return index;
			}
			return lo < array.Length ? lo : -1;
		}
	}

	[ExportDocumentViewerCustomDataProvider]
	sealed class DocumentViewerUIElementCustomDataProvider : IDocumentViewerCustomDataProvider {
		public void OnCustomData(IDocumentViewerCustomDataContext context) {
			var data = context.GetData<DocumentViewerUIElement>(DocumentViewerUIElementConstants.CustomDataId);
			var coll = data.Length == 0 ? DocumentViewerUIElementCollection.Empty : new DocumentViewerUIElementCollection(data);
			context.AddCustomData(DocumentViewerUIElementConstants.ContentDataId, coll);
		}
	}

	[ExportDocumentViewerListener(DocumentViewerListenerConstants.ORDER_UIELEMENTSERVICE)]
	sealed class DocumentViewerUIElementListener : IDocumentViewerListener {
		readonly IDocumentViewerUIElementServiceProvider documentViewerUIElementServiceProvider;

		[ImportingConstructor]
		DocumentViewerUIElementListener(IDocumentViewerUIElementServiceProvider documentViewerUIElementServiceProvider) {
			this.documentViewerUIElementServiceProvider = documentViewerUIElementServiceProvider;
		}

		public void OnEvent(DocumentViewerEventArgs e) {
			if (e.EventType == DocumentViewerEvent.GotNewContent)
				documentViewerUIElementServiceProvider.GetService(e.DocumentViewer.TextView).SetData(e.DocumentViewer.Content.GetCustomData<DocumentViewerUIElementCollection>(DocumentViewerUIElementConstants.ContentDataId));
		}
	}

	[Export(typeof(IViewTaggerProvider))]
	[TagType(typeof(IntraTextAdornmentTag))]
	[ContentType(ContentTypes.Any)]
	sealed class DocumentViewerUIElementTaggerProvider : IViewTaggerProvider {
		readonly IDocumentViewerUIElementServiceProvider documentViewerUIElementServiceProvider;

		[ImportingConstructor]
		DocumentViewerUIElementTaggerProvider(IDocumentViewerUIElementServiceProvider documentViewerUIElementServiceProvider) {
			this.documentViewerUIElementServiceProvider = documentViewerUIElementServiceProvider;
		}

		public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag {
			if (textView.TextBuffer != buffer)
				return null;
			// We can't call textView.TextBuffer.TryGetDocumentViewer() since it hasn't completely
			// initialized yet and the method would return null.
			if (!textView.Roles.Contains(PredefinedDsTextViewRoles.DocumentViewer))
				return null;
			return textView.Properties.GetOrCreateSingletonProperty(
				typeof(DocumentViewerUIElementTagger),
				() => new DocumentViewerUIElementTagger(documentViewerUIElementServiceProvider.GetService(textView))) as ITagger<T>;
		}
	}

	interface IDocumentViewerUIElementTagger {
		void RefreshSpans(SnapshotSpanEventArgs e);
	}

	sealed class DocumentViewerUIElementTagger : ITagger<IntraTextAdornmentTag>, IDocumentViewerUIElementTagger {
		public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

		readonly IDocumentViewerUIElementService documentViewerUIElementService;

		public DocumentViewerUIElementTagger(IDocumentViewerUIElementService documentViewerUIElementService) {
			if (documentViewerUIElementService == null)
				throw new ArgumentNullException(nameof(documentViewerUIElementService));
			this.documentViewerUIElementService = documentViewerUIElementService;
			documentViewerUIElementService.RegisterTagger(this);
		}

		public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans) =>
			documentViewerUIElementService.GetTags(spans);

		public void RefreshSpans(SnapshotSpanEventArgs e) => TagsChanged?.Invoke(this, e);
	}

	interface IDocumentViewerUIElementServiceProvider {
		IDocumentViewerUIElementService GetService(ITextView textView);
	}

	[Export(typeof(IDocumentViewerUIElementServiceProvider))]
	sealed class DocumentViewerUIElementServiceProvider : IDocumentViewerUIElementServiceProvider {
		public IDocumentViewerUIElementService GetService(ITextView textView) =>
			textView.Properties.GetOrCreateSingletonProperty(typeof(DocumentViewerUIElementService), () => new DocumentViewerUIElementService(textView));
	}

	interface IDocumentViewerUIElementService {
		void SetData(DocumentViewerUIElementCollection collection);
		void RegisterTagger(IDocumentViewerUIElementTagger tagger);
		IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans);
	}

	sealed class DocumentViewerUIElementService : IDocumentViewerUIElementService {
		readonly ITextView textView;
		readonly Dictionary<int, UIElement> cachedUIElements;
		DocumentViewerUIElementCollection collection;
		IDocumentViewerUIElementTagger tagger;
		int textVersionNumber;

		public DocumentViewerUIElementService(ITextView textView) {
			if (textView == null)
				throw new ArgumentNullException(nameof(textView));
			this.textView = textView;
			this.cachedUIElements = new Dictionary<int, UIElement>();
			this.collection = DocumentViewerUIElementCollection.Empty;
			this.textVersionNumber = -1;
		}

		public void SetData(DocumentViewerUIElementCollection collection) {
			this.textVersionNumber = textView.TextSnapshot.Version.VersionNumber;
			var newCollection = collection ?? DocumentViewerUIElementCollection.Empty;
			if (newCollection.Count == 0)
				newCollection = DocumentViewerUIElementCollection.Empty;
			if (this.collection == newCollection)
				return;
			this.collection = newCollection;
			this.cachedUIElements.Clear();
			tagger?.RefreshSpans(new SnapshotSpanEventArgs(new SnapshotSpan(textView.TextSnapshot, 0, textView.TextSnapshot.Length)));
		}

		public void RegisterTagger(IDocumentViewerUIElementTagger tagger) {
			if (this.tagger != null)
				throw new InvalidOperationException();
			if (tagger == null)
				throw new ArgumentNullException(nameof(tagger));
			this.tagger = tagger;
		}

		public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans) {
			if (spans.Count == 0)
				yield break;
			if (textVersionNumber != textView.TextSnapshot.Version.VersionNumber)
				yield break;
			var snapshot = spans[0].Snapshot;
			foreach (var span in spans) {
				int index = collection.GetStartIndex(span.Start.Position);
				if (index < 0)
					continue;
				while (index < collection.Count) {
					var info = collection[index];
					if (info.Position > snapshot.Length)
						yield break;
					if (info.Position > span.End)
						break;

					UIElement uiElem;
					if (!cachedUIElements.TryGetValue(index, out uiElem)) {
						uiElem = info.CreateElement();
						cachedUIElements.Add(index, uiElem);
						Debug.Assert(uiElem != null);
						uiElem?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
					}
					if (uiElem == null)
						continue;

					var textHeight = Math.Ceiling(uiElem.DesiredSize.Height);
					var tag = new IntraTextAdornmentTag(uiElem, removalCallback: null, topSpace: 0, baseline: textHeight, textHeight: textHeight, bottomSpace: 0, affinity: PositionAffinity.Successor);
					yield return new TagSpan<IntraTextAdornmentTag>(new SnapshotSpan(snapshot, info.Position, 0), tag);
					index++;
				}
			}
		}
	}
}
