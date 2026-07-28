using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO
{
    class FileSystem : IFileSystem
    {
        public string GetFileName(string path) => Path.GetFileName(path);

        public List<string> GetFiles(string path, string searchPattern, bool recursive) =>
            Directory.GetFiles(path, searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();

        public async Task<string> ReadAllText(string path, CancellationToken token)
            => await File.ReadAllTextAsync(path, token);

        public async Task WriteAllText(string path, string contents, CancellationToken token) =>
            await File.WriteAllTextAsync(path, contents, token);

        public void Delete(string path) =>
            File.Delete(path);
    }
}
