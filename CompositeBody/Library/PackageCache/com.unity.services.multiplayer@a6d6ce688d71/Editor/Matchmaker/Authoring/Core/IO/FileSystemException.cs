using System.IO;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.IO
{
    class FileSystemException : IOException
    {
        public string Path { get; }

        public enum Action
        {
            Read,
            Write,
            Delete,
            GetFileName,
            ListFiles
        }

        public Action PathAction { get; }

        public FileSystemException(string filePath, Action action, string message) : base(message)
        {
            Path = filePath;
            PathAction = action;
        }

        public override string ToString()
        {
            string error;
            switch (PathAction)
            {
                case Action.Read:
                    error = "read file at";
                    break;
                case Action.Write:
                    error = "write file at";
                    break;
                case Action.Delete:
                    error = "delete file at";
                    break;
                case Action.GetFileName:
                    error = "get file name at";
                    break;
                case Action.ListFiles:
                    error = "list files in";
                    break;
                default:
                    error = "unknown";
                    break;
            }

            return $"Error trying to {error} {Path} : {Message}";
        }
    }
}
