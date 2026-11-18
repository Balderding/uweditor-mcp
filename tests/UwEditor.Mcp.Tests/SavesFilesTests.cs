using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Checkpoints, Restore all and the .ORIG/.BKP copies, on a saved game made in a temp copy.</summary>
    public class SavesFilesTests
    {
        [Fact]
        public void Saves_checkpoint_make_restore_delete()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            Assert.StartsWith("There are no checkpoints", ed.CheckpointList());
            Assert.StartsWith("Nothing unsaved", ed.CheckpointMake(null));
            ed.Fill(1, 2, 2, 3, 3, "open", 6, null, null);
            ed.PlayerSet("dexterity", "19");
            Assert.StartsWith("Checkpoint kept", ed.CheckpointMake("test"));
            Assert.StartsWith("1. ", ed.CheckpointList());
            Assert.Contains("PLAYER.DAT", ed.CheckpointList());
            // drop everything, then bring the checkpoint back
            Assert.StartsWith("Not restored. Unsaved:", ed.RestoreAll(false));
            Assert.StartsWith("Restored: every unsaved change", ed.RestoreAll(true));
            Assert.NotEqual(6, ed.Edits.For(1).TileField(2, 2, "Floor height"));
            Assert.DoesNotContain("dexterity: 19", ed.PlayerGet());
            Assert.StartsWith("Nothing unsaved", ed.RestoreAll(true));
            // Restore all kept its own checkpoint first: the one made by the test is now 2
            Assert.Contains("before Restore all", ed.CheckpointList());
            string r = ed.CheckpointRestore(2, false);
            Assert.StartsWith("Restored level 1, the player", r);
            Assert.Equal(6, ed.Edits.For(1).TileField(2, 2, "Floor height"));
            Assert.Contains("dexterity: 19", ed.PlayerGet());
            Assert.StartsWith("Not restored", ed.CheckpointRestore(2, false));   // the unsaved player would be replaced
            Assert.StartsWith("Not deleted", ed.CheckpointDelete(1, false));
            Assert.StartsWith("Deleted", ed.CheckpointDelete(1, true));
            Assert.StartsWith("Refused:", EditorTools.Text(new EditorTools(ed).CheckpointRestore(9)));
            Assert.Contains("newest 5", ed.CheckpointSettings(null, 5));
        }

        [Fact]
        public void Saves_backups_list_and_restore()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            string start = ed.PlayerGet();
            ed.PlayerSet("strength", "27");
            ed.PlayerSave();
            string list = ed.BackupList();
            Assert.True(list.Contains(Path.Combine("SAVE1", "PLAYER.DAT") + ": .ORIG (differs from now), .BKP"), list);
            Assert.StartsWith("Not restored", ed.BackupRestore(@"SAVE1\PLAYER.DAT", "orig", false));
            Assert.Contains("strength: 27", ed.PlayerGet());
            ed.PlayerSet("strength", "28");
            Assert.StartsWith("Refused: Unsaved", EditorTools.Text(new EditorTools(ed).BackupRestore(@"SAVE1\PLAYER.DAT", "orig", true)));
            ed.RestoreAll(true);
            Assert.StartsWith("Restored SAVE1", ed.BackupRestore("SAVE1/PLAYER.DAT", "orig", true));
            Assert.Equal(start.Split('\n')[2], ed.PlayerGet().Split('\n')[2]);
            Assert.DoesNotContain("strength: 27", ed.PlayerGet());
            // the .BKP now holds strength 27: put it back
            Assert.StartsWith("Restored", ed.BackupRestore(@"SAVE1\PLAYER.DAT", "bkp", true));
            Assert.Contains("strength: 27", ed.PlayerGet());
            Assert.StartsWith("Not restored", ed.BackupRestoreAll(false));
            Assert.StartsWith("Restored 1 file", ed.BackupRestoreAll(true));
            Assert.DoesNotContain("strength: 27", ed.PlayerGet());
            Assert.StartsWith("Every file is as first found", ed.BackupRestoreAll(true));
            Assert.StartsWith("Refused: No copy", EditorTools.Text(new EditorTools(ed).BackupRestore("DATA/NOPE.DAT", "bkp", true)));
        }
    }
}
