using System;
using System.Linq;
using UwEditor.Core.Conversations;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Giving an empty slot a conversation from the template, and deleting one (ConversationLibrary.AddConversation, DeleteConversation).</summary>
    public class AddConversationTests
    {
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void An_empty_slot_gets_a_conversation_that_reads_back_and_can_be_edited_and_deleted(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var empty = lib.EmptySlots();
            Assert.NotEmpty(empty);
            int slot = empty[0];
            Assert.False(lib.Archive.Has(slot));
            int count = lib.Archive.Slots().Count();

            var c = lib.AddConversation(slot, "Testa", "Well met, stranger.", "I must go.", "Go safely.");
            Assert.True(lib.Archive.Has(slot));
            Assert.Equal(count + 1, lib.Archive.Slots().Count());
            Assert.Equal(slot, c.Slot);
            Assert.Equal(lib.StringBlockFor(slot), c.StringBlock);
            Assert.Equal("Testa", lib.Name(slot));
            Assert.DoesNotContain(slot, lib.EmptySlots());
            // its strings are in its own block, and the Dialogue tab shows the template
            var strings = lib.ShownStringsOf(c);
            Assert.Contains("Well met, stranger.", strings);
            Assert.Contains("I must go.", strings);
            Assert.Contains("Go safely.", strings);
            string source = lib.Decompile(c).Text;
            Assert.Contains("say \"Well met, stranger.\"", source);
            Assert.Contains("\"I must go.\"", source);
            // the block survives the archive's bytes, and what the Dialogue tab shows compiles again to the same code
            var again = ConversationArchive.FromBytes(lib.Archive.ToBytes(), s.Kind).Read(slot);
            Assert.Equal(c.Code, again.Code);
            var r = lib.Compile(c, source, new CompileOptions { CompileAll = true });
            Assert.True(r.Ok, string.Join("; ", r.Errors.Select(e => e.ToString())));
            Assert.Equal(c.Code, r.Conversation.Code);
            // a changed line compiles and applies as on any conversation
            var r2 = lib.Compile(c, source.Replace("Well met, stranger.", "Hail!"), null);
            Assert.True(r2.Ok, string.Join("; ", r2.Errors.Select(e => e.ToString())));
            lib.Apply(r2);
            Assert.Contains("Hail!", lib.ShownStringsOf(lib.Archive.Read(slot)));

            // a slot that has one is refused; so is a slot no creature can name
            Assert.Throws<InvalidOperationException>(() => lib.AddConversation(slot));
            Assert.Throws<ArgumentOutOfRangeException>(() => lib.AddConversation(0));

            // deleting takes it out of the archive
            lib.DeleteConversation(slot);
            Assert.False(lib.Archive.Has(slot));
            Assert.Contains(slot, lib.EmptySlots());
            Assert.Null(ConversationArchive.FromBytes(lib.Archive.ToBytes(), s.Kind).Read(slot));
            Assert.Throws<InvalidOperationException>(() => lib.DeleteConversation(slot));
            // a shipped one too, and its neighbours stay as they were
            int shipped = lib.Archive.Slots().First(x => x < 0x100);
            int other = lib.Archive.Slots().First(x => x < 0x100 && x != shipped);
            var otherCode = lib.Archive.Read(other).Code;
            lib.DeleteConversation(shipped);
            var bytes = ConversationArchive.FromBytes(lib.Archive.ToBytes(), s.Kind);
            Assert.False(bytes.Has(shipped));
            Assert.Equal(otherCode, bytes.Read(other).Code);
        }
    }
}
