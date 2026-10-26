using System.Collections.Generic;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// The editing features in tab order. Adding a feature = a class implementing <see cref="IFeature"/> + one line
    /// here; removing one = deleting the line (and its files / object kind). See docs/design/features.md.
    /// </summary>
    internal static class FeatureRegistry
    {
        public static List<IFeature> Create() => new List<IFeature>
        {
            new FrameFeature(),     // 下地: background colour / registered frames (the bottom layer first)
            new PictureFeature(),   // 画像: pictures placed as objects (not registered)
            new StampFeature(),     // スタンプ: registered pictures placed as objects
            new TextFeature(),      // 文字: palette-colour text objects
            new QuantizeFeature(),  // 減色: dithering, adjustments, colours used
        };
    }
}
