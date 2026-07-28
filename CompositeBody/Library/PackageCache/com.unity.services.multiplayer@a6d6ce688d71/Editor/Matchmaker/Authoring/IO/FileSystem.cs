using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO
{
    class FileSystem : IFileSystem
    {
        public string GetFileName(string path)
        {
            return Path.GetFileName(path);
        }

        public List<string> GetFiles(string path,
            string searchPattern,
            bool recursive)
        {
            return Directory
                .GetFiles(path, searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .ToList();
        }

        public Task<string> ReadAllText(
            string path,
            CancellationToken token = default(CancellationToken))
        {
            return Task.FromResult(File.ReadAllText(path));
        }

        public Task WriteAllText(
            string path,
            string contents,
            CancellationToken token = default(CancellationToken))
        {
            File.WriteAllText(path, contents);
            return Task.CompletedTask;
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }
    }
}
