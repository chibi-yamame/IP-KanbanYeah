namespace KanbanDrawer.Core.Board
{
    /// <summary>A registered picture placed on the board (own pixel copy, so deleting it from the library keeps the board).</summary>
    public sealed class StampObject : ImageObject
    {
        /// <summary>Library id it came from (informational).</summary>
        public string Id;

        public StampObject(string id, Raster picture) : base(picture)
        {
            Id = id;
        }

        public override BoardObject Clone()
        {
            var o = new StampObject(Id, Picture);
            CopyTo(o);
            return o;
        }
    }
}
