namespace SF2MusicCooker
{
    public sealed class Options
    {
        public readonly string Title;
        public readonly string VolumeStrategy;
        public readonly int IsolateChannel;
        public readonly bool OptimizeNotes;
        public readonly bool DumpNotes;

        public Options(string title = null, string volumeStrategy = null, int isolateChannel = -1, bool optimizeNotes = false, bool dumpNotes = false)
        {
            Title = title;
            VolumeStrategy = volumeStrategy;
            IsolateChannel = isolateChannel;
            OptimizeNotes = optimizeNotes;
            DumpNotes = dumpNotes;
        }

        public static readonly Options Default = new Options();
    }
}