using System.IO;

namespace UltimateEditor.Games
{
	/// <summary>
	/// A game file that cannot be read as its format says: truncated, or holding counts, offsets or
	/// lengths that lie outside it. The readers throw this instead of running off the end of an
	/// array, allocating a negative or huge buffer, or looping, so a damaged file (a bad download, a
	/// half-written save, a mod from elsewhere) gives a message that names it.
	/// </summary>
	public sealed class GameFileException : IOException
	{
		/// <summary>The file's path or name, when the reader knew it (null otherwise).</summary>
		public string File { get; private set; }
		/// <summary>Where in the file the problem lies, or -1.</summary>
		public long Offset { get; private set; }
		/// <summary>What is wrong, without the file's name.</summary>
		public string What { get; private set; }

		public GameFileException(string what, long offset = -1, string file = null)
			: base(Compose(what, offset, file))
		{
			What = what; Offset = offset; File = file;
		}

		/// <summary>The same problem, naming the file (when it was not named yet).</summary>
		public GameFileException For(string file)
		{
			return File != null ? this : new GameFileException(What, Offset, file);
		}

		static string Compose(string what, long offset, string file)
		{
			string where = offset >= 0 ? " (at byte 0x" + offset.ToString("X") + ")" : "";
			string name = file != null ? Path.GetFileName(file) + " is damaged: " : "The file is damaged: ";
			return name + what + where + ".";
		}

		/// <summary>Throws unless [offset, offset + length) lies inside a file of fileLength bytes.</summary>
		public static void CheckRange(long offset, long length, long fileLength, string what)
		{
			if (offset < 0 || length < 0 || offset > fileLength || length > fileLength - offset)
				throw new GameFileException(what + " runs past the end of the file (" + length + " bytes from 0x" + offset.ToString("X") + " in a file of " + fileLength + ")", offset);
		}
	}
}
