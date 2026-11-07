using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Afterfall.Persistence
{
    public sealed class SaveSlotInfo
    {
        public string Slot { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime LastWriteUtc { get; set; }
        public SaveHeader Header { get; set; }
        public bool Readable => Header != null;
    }

    /// <summary>
    /// Save files on disk. Writes are atomic: content goes to a temporary file which then
    /// replaces the previous save, and the previous save is kept as a .bak backup used for
    /// recovery if the main file is ever found damaged.
    /// </summary>
    public sealed class SaveFileStore
    {
        public const string Extension = ".afsave";

        public SaveFileStore(string directory)
        {
            Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
        }

        public string Directory { get; }

        public string PathFor(string slot) => System.IO.Path.Combine(Directory, Sanitize(slot) + Extension);
        public string BackupPathFor(string slot) => PathFor(slot) + ".bak";

        public void Write(string slot, string content)
        {
            string path = PathFor(slot);
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                string bak = BackupPathFor(slot);
                if (File.Exists(bak)) File.Delete(bak);
                File.Replace(tmp, path, bak);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));
        public string ReadMain(string slot) => File.ReadAllText(PathFor(slot), Encoding.UTF8);
        public string ReadBackup(string slot) => File.Exists(BackupPathFor(slot)) ? File.ReadAllText(BackupPathFor(slot), Encoding.UTF8) : null;

        public List<SaveSlotInfo> List()
        {
            var list = new List<SaveSlotInfo>();
            foreach (string file in System.IO.Directory.GetFiles(Directory, "*" + Extension))
            {
                string slot = System.IO.Path.GetFileNameWithoutExtension(file);
                SaveHeader header = null;
                try
                {
                    header = SaveSerializer.ReadHeader(File.ReadAllText(file, Encoding.UTF8));
                }
                catch (IOException)
                {
                }
                list.Add(new SaveSlotInfo { Slot = slot, Path = file, LastWriteUtc = File.GetLastWriteTimeUtc(file), Header = header });
            }
            list.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            return list;
        }

        public void Delete(string slot)
        {
            foreach (string p in new[] { PathFor(slot), BackupPathFor(slot), PathFor(slot) + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }

        public static string Sanitize(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) return "save";
            var sb = new StringBuilder();
            foreach (char c in slot.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length > 48 ? sb.ToString(0, 48) : sb.ToString();
        }
    }
}
