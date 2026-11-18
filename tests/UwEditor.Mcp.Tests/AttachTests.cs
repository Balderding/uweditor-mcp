using System;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>McpEditor attached to a game someone else holds (the app): its edits, its thread.</summary>
    public class AttachTests
    {
        sealed class Host
        {
            public GameSession Session;
            public LevelEdits Edits;
            public int Runs;
            public Host(string g) { if (g != null) { Session = GameSession.Open(g); Edits = new LevelEdits(Session); } }
            public McpEditor Editor() { return McpEditor.Attached(() => Session, () => Edits, f => { Runs++; return f(); }); }
        }

        [Fact]
        public void Tools_change_the_hosts_edits()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var h = new Host(g);
            var ed = h.Editor();
            ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null);
            Assert.True(h.Edits.HasChanges);
            Assert.Equal(3, h.Edits.For(1).TileField(4, 4, "Floor height"));
        }

        [Fact]
        public void The_hosts_new_game_is_followed()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var h = new Host(g);
            var ed = h.Editor();
            ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null);
            var other = new Host(GameCopy.Make());
            h.Session = other.Session; h.Edits = other.Edits;
            Assert.False(ed.Edits.HasChanges);
            Assert.Same(other.Edits, ed.Edits);
        }

        [Fact]
        public void Save_goes_through_the_hosts_save()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var h = new Host(g);
            int saves = 0;
            var ed = McpEditor.Attached(() => h.Session, () => h.Edits, f => f(), () => { saves++; return "saved by the host"; });
            Assert.Equal("Nothing to save.", ed.Save());              // (nothing changed: the host's save is not asked)
            ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null);
            Assert.Equal("saved by the host", ed.Save());
            Assert.Equal(1, saves);
        }

        [Fact]
        public void No_game_in_the_host()
        {
            var h = new Host(null);
            var e = Assert.Throws<McpEditorException>(() => h.Editor().LevelMap(1));
            Assert.Equal("No game is open in the app.", e.Message);
        }

        [Fact]
        public void Open_is_refused_when_attached()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var e = Assert.Throws<McpEditorException>(() => new Host(g).Editor().Open(g));
            Assert.Contains("in the app", e.Message);
        }

        [Fact]
        public void Every_tool_call_goes_through_the_runner()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var h = new Host(g);
            var tools = new EditorTools(h.Editor());
            tools.LevelMap(1);
            tools.TileGet(1, 5, 5);
            Assert.StartsWith("Refused", EditorTools.Text(tools.Open(g)));
            Assert.Equal(3, h.Runs);
        }
    }
}
