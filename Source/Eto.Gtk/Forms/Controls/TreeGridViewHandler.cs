using System;
using Eto.Forms;
using System.Collections.Generic;
using Eto.GtkSharp.Forms.Cells;
using System.Linq;

namespace Eto.GtkSharp.Forms.Controls
{
	public class TreeGridViewHandler : GridHandler<TreeGridView, TreeGridView.ICallback>, TreeGridView.IHandler, ICellDataSource, IGtkListModelHandler<ITreeGridItem, ITreeGridStore<ITreeGridItem>>
	{
		protected new TreeGridView.ICallback Callback { get { return (TreeGridView.ICallback)base.Callback; } }

		GtkTreeModel<ITreeGridItem, ITreeGridStore<ITreeGridItem>> model;
		CollectionHandler collection;
		bool? selectCollapsingItem;
		ITreeGridItem lastSelected;

		protected override void Initialize()
		{
			base.Initialize();

			// these are always handled to set the expanded property
			HandleEvent(TreeGridView.ExpandedEvent);
			HandleEvent(TreeGridView.CollapsedEvent);
			HandleEvent(TreeGridView.CollapsingEvent);
		}

		protected override ITreeModelImplementor CreateModelImplementor()
		{
			model = new GtkTreeModel<ITreeGridItem, ITreeGridStore<ITreeGridItem>> { Handler = this };
			return model;
		}

		public class CollectionHandler : DataStoreChangedHandler<ITreeGridItem, ITreeGridStore<ITreeGridItem>>
		{
			WeakReference handler;
			public TreeGridViewHandler Handler { get { return (TreeGridViewHandler)handler.Target; } set { handler = new WeakReference(value); } }

			void ExpandItems(ITreeGridStore<ITreeGridItem> store, Gtk.TreePath path)
			{
				for (int i = 0; i < store.Count; i++)
				{
					var item = store[i];
					if (item.Expandable && item.Expanded)
					{
						var newpath = path.Copy();
						newpath.AppendIndex(i);
						Handler.Tree.ExpandToPath(newpath);
						ExpandItems((ITreeGridStore<ITreeGridItem>)item, newpath);
					}
				}
			}

			void ExpandItems()
			{
				var store = Handler.collection.Collection;
				var path = new Gtk.TreePath();
				ExpandItems(store, path);
			}

			public override void AddRange(IEnumerable<ITreeGridItem> items)
			{
				Handler.UpdateModel();
				ExpandItems();
			}

			public override void AddItem(ITreeGridItem item)
			{
				var path = new Gtk.TreePath();
				path.AppendIndex(Collection.Count);
				var iter = Handler.model.GetIterFromItem(item, path);
				Handler.Tree.Model.EmitRowInserted(path, iter);
			}

			public override void InsertItem(int index, ITreeGridItem item)
			{
				var path = new Gtk.TreePath();
				path.AppendIndex(index);
				var iter = Handler.model.GetIterFromItem(item, path);
				Handler.Tree.Model.EmitRowInserted(path, iter);
			}

			public override void RemoveItem(int index)
			{
				var path = new Gtk.TreePath();
				path.AppendIndex(index);
				Handler.Tree.Model.EmitRowDeleted(path);
			}

			public override void RemoveAllItems()
			{
				Handler.UpdateModel();
			}
		}

		public ITreeGridStore<ITreeGridItem> DataStore
		{
			get { return collection != null ? collection.Collection : null; }
			set
			{
				if (collection != null)
					collection.Unregister();
				collection = new CollectionHandler { Handler = this };
				collection.Register(value);
			}
		}

		public ITreeGridItem SelectedItem
		{
			get
			{
				Gtk.TreeIter iter;
				return Tree.Selection.GetSelected(out iter) ? model.GetItemAtIter(iter) : null;
			}
			set
			{
				if (value != null)
				{
					var path = model.GetPathFromItem(value);
					if (path != null)
					{
						Tree.ExpandToPath(path);
						Tree.Selection.SelectPath(path);
						Tree.ScrollToCell(path, null, false, 0, 0);
					}
				}
				else
					Tree.Selection.UnselectAll();
			}
		}

		bool ChildIsSelected(ITreeGridItem item)
		{
			var node = SelectedItem;
			
			while (node != null)
			{
				if (node == item)
					return true;
				node = node.Parent;
			}
			return false;
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case TreeGridView.ExpandingEvent:
					Tree.TestExpandRow += Connector.HandleTestExpandRow;
					break;
				case TreeGridView.ExpandedEvent:
					Tree.RowExpanded += Connector.HandleRowExpanded;
					break;
				case TreeGridView.CollapsingEvent:
					Tree.TestCollapseRow += Connector.HandleTestCollapseRow;
					break;
				case TreeGridView.CollapsedEvent:
					Tree.RowCollapsed += Connector.HandleRowCollapsed;
					break;
				case TreeGridView.SelectedItemChangedEvent:
					Tree.Selection.Changed += Connector.HandleSelectionChanged;
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		protected new TreeGridViewConnector Connector { get { return (TreeGridViewConnector)base.Connector; } }

		protected override WeakConnector CreateConnector()
		{
			return new TreeGridViewConnector();
		}

		protected class TreeGridViewConnector : GridConnector
		{
			public new TreeGridViewHandler Handler { get { return (TreeGridViewHandler)base.Handler; } }

			public void HandleTestExpandRow(object o, Gtk.TestExpandRowArgs args)
			{
				var h = Handler;
				var e = new TreeGridViewItemCancelEventArgs(h.GetItem(args.Path) as ITreeGridItem);
				h.Callback.OnExpanding(h.Widget, e);
				args.RetVal = e.Cancel;
			}

			public void HandleRowExpanded(object o, Gtk.RowExpandedArgs args)
			{
				var h = Handler;
				var e = new TreeGridViewItemEventArgs(h.GetItem(args.Path) as ITreeGridItem);
				e.Item.Expanded = true;
				h.Callback.OnExpanded(h.Widget, e);
			}

			public void HandleTestCollapseRow(object o, Gtk.TestCollapseRowArgs args)
			{
				var h = Handler;
				var e = new TreeGridViewItemCancelEventArgs(h.GetItem(args.Path) as ITreeGridItem);
				h.Callback.OnCollapsing(h.Widget, e);
				args.RetVal = e.Cancel;
				if (!e.Cancel)
				{
					h.selectCollapsingItem = !h.AllowMultipleSelection && h.ChildIsSelected(e.Item);
					h.SkipSelectedChange = true;
				}
			}

			public void HandleRowCollapsed(object o, Gtk.RowCollapsedArgs args)
			{
				var h = Handler;
				var e = new TreeGridViewItemEventArgs(h.GetItem(args.Path) as ITreeGridItem);
				e.Item.Expanded = false;
				h.Callback.OnCollapsed(h.Widget, e);
				h.SkipSelectedChange = false;
				if (h.selectCollapsingItem == true)
				{
					h.Tree.Selection.UnselectAll();
					h.Tree.Selection.SelectPath(args.Path);
					h.selectCollapsingItem = null;
				}
			}

			public void HandleSelectionChanged(object sender, EventArgs e)
			{
				var h = Handler;
				var item = h.SelectedItem;
				if (!h.SkipSelectedChange && !object.ReferenceEquals(item, h.lastSelected))
				{
					h.Callback.OnSelectedItemChanged(h.Widget, EventArgs.Empty);
					h.lastSelected = item;
				}
			}
		}

		public override object GetItem(Gtk.TreePath path)
		{
			return model.GetItemAtPath(path);
		}

		public override int GetRowIndexOfPath(Gtk.TreePath path)
		{
			var tempPath = new Gtk.TreePath();
			int count = GetCount(Gtk.TreeIter.Zero, path.Indices[0]);
			// slow but works for now
			for (int i = 0; i < path.Indices.Length - 1; i++)
			{
				tempPath.AppendIndex(path.Indices[i]);
				Gtk.TreeIter iter;
				if (model.GetIter(out iter, tempPath))
					count += GetCount(iter, path.Indices[i + 1]);
			}
			count += path.Indices.Length - 1;
			//count += path.Indices[row.Indices.Length - 1];

			return count;
		}

		public override Gtk.TreeIter GetIterAtRow(int row)
		{
			Gtk.TreeIter iter;
			model.GetIter(out iter, GetPathAtRow(row));
			return iter;
		}

		public override Gtk.TreePath GetPathAtRow(int row)
		{
			Gtk.TreePath path;
			Gtk.TreeIter iter;
			Gtk.TreeIter temp;

			bool valid = Tree.Model.GetIterFirst(out iter);
			while (valid)
			{
				// Check
				path = Tree.Model.GetPath(iter);
				if (GetRowIndexOfPath(path) == row)
					return path;

				// Go Down
				if (Tree.GetRowExpanded(path) && Tree.Model.IterChildren(out iter, iter))
					continue;

				// Go Next
				temp = iter;
				if (Tree.Model.IterNext(ref iter))
					continue;
				else
					iter = temp;

				while (valid)
				{
					// Go Up
					if (Tree.Model.IterParent(out iter, iter))
					{
						// Go Next
						temp = iter;
						if (Tree.Model.IterNext(ref iter))
							break;
						else
							iter = temp;
					}
					else
						valid = false;
				}
			}

			// Get and return first if given row does not exist
			Tree.Model.GetIterFirst(out iter);
			return Tree.Model.GetPath(iter);
		}

		protected int GetRowCount()
		{
			Gtk.TreePath path;
			Gtk.TreeIter iter;
			Gtk.TreeIter temp;

			bool valid = Tree.Model.GetIterFirst(out iter);
			int count = 0;
			while (valid)
			{
				count++;

				// Go Down
				path = Tree.Model.GetPath(iter);
				if (Tree.GetRowExpanded(path) && Tree.Model.IterChildren(out iter, iter))
					continue;

				// Go Next
				temp = iter;
				if (Tree.Model.IterNext(ref iter))
					continue;
				else
					iter = temp;

				// Go Up and Next
				while (true)
				{
					// Go Up
					if (Tree.Model.IterParent(out iter, iter))
					{
						// Go Next
						temp = iter;
						if (Tree.Model.IterNext(ref iter))
							break;
						else
							iter = temp;
					}
					else
						return count;
				}
			}
			return count;
		}

		protected override void SetSelectedRows(IEnumerable<int> value)
		{
			Tree.Selection.UnselectAll();
			if (value != null && collection != null)
			{
				int start = -1;
				int end = -1;
				var count = GetRowCount();

				foreach (var row in value.Where(r => r < count).OrderBy(r => r))
				{
					if (start == -1)
						start = end = row;
					else if (row == end + 1)
						end = row;
					else
					{
						if (start == end)
							Tree.Selection.SelectIter(GetIterAtRow(start));
						else
							Tree.Selection.SelectRange(GetPathAtRow(start), GetPathAtRow(end));
						start = end = row;
					}
				}
				if (start != -1)
				{
					if (start == end)
						Tree.Selection.SelectIter(GetIterAtRow(start));
					else
						Tree.Selection.SelectRange(GetPathAtRow(start), GetPathAtRow(end));
				}
			}
		}

		public GLib.Value GetColumnValue(ITreeGridItem item, int dataColumn, int row)
		{
			if (dataColumn == RowDataColumn)
				return new GLib.Value(row);

			int column;
			if (ColumnMap.TryGetValue(dataColumn, out column))
			{
				var colHandler = (IGridColumnHandler)Widget.Columns[column].Handler;
				return colHandler.GetValue(item, dataColumn, row);
			}
			return new GLib.Value((string)null);
		}

		public int GetRowOfItem(ITreeGridItem item)
		{
			return collection == null ? -1 : collection.IndexOf(item);
		}

		int GetCount(Gtk.TreeIter parent, int upToIndex)
		{
			int rows = upToIndex == -1 ? model.IterNChildren(parent) : upToIndex;
			int count = 0;
			for (int i = 0; i < rows; i++)
			{
				Gtk.TreeIter iter;
				if (model.IterNthChild(out iter, parent, i))
				{
					var childPath = model.GetPath(iter);
					
					if (Tree.GetRowExpanded(childPath))
					{
						count += GetCount(iter, -1);
					}
				}
				count++;
			}
			return count;
		}

		public override IEnumerable<int> SelectedRows
		{
			get
			{
				var rows = Tree.Selection.GetSelectedRows();
				foreach (var row in rows)
					yield return GetRowIndexOfPath(row);
			}
		}
	}
}
