using System.Collections.Generic;
using System.Windows.Forms;

namespace Retrace.Tests
{
    public static class TrackListTests
    {
        sealed class KeyboardList : TrackList
        {
            public void Press(Keys key) { OnKeyDown(new KeyEventArgs(key)); }
        }

        public static void TestShiftArrowsExtendAndShrinkSelection()
        {
            var playlist = new Playlist();
            playlist.Add(new[] { "a.mp3", "b.mp3", "c.mp3", "d.mp3" });
            using (var list = new KeyboardList())
            {
                list.Height = TrackList.RowH * 4;
                list.Source = playlist;
                list.Press(Keys.Home);
                list.Press(Keys.Shift | Keys.Down);
                list.Press(Keys.Shift | Keys.Down);
                Assert.Equal(3, list.SelectionCount, "two Shift+Down presses extend the range twice");
                Assert.True(new HashSet<int>(list.Selection).Contains(2), "the moving end reached row 3");

                list.Press(Keys.Shift | Keys.Up);
                Assert.Equal(2, list.SelectionCount, "Shift+Up shrinks the range");
                Assert.False(new HashSet<int>(list.Selection).Contains(2), "row 3 was released");
            }
        }

    }
}
