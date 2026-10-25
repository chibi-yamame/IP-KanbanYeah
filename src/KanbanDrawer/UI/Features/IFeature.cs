using System.Windows.Forms;
using KanbanDrawer.Core.Board;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// One editing feature = one tab. Features never reference each other; everything shared goes through
    /// <see cref="EditorHost"/>. Register new ones in <see cref="FeatureRegistry"/>.
    /// </summary>
    internal interface IFeature
    {
        /// <summary>Tab title.</summary>
        string Title { get; }

        /// <summary>Builds the tab's contents (called once).</summary>
        Control CreatePanel(EditorHost host);

        /// <summary>True for the objects this feature edits (selecting one switches to this tab).</summary>
        bool Owns(BoardObject o);

        /// <summary>The selection changed (null = nothing selected); show the object's values if it is ours.</summary>
        void OnSelection(BoardObject o);

        void Load(Settings s);
        void Save(Settings s);
    }

    /// <summary>A feature that takes picture files dropped on the window (the active tab first, else the first one).</summary>
    internal interface IAcceptsFiles
    {
        bool OpenFile(string path);
    }
}
