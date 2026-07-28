using Unity.Services.Core.Editor;

namespace Unity.Services.Vivox.Editor
{
    struct VivoxIdentifier : IEditorGameServiceIdentifier
    {
        /// <summary>
        /// Key for the Vivox package
        /// </summary>
        /// <returns>Vivox package key</returns>
        public string GetKey() => "Vivox";
    }
}
