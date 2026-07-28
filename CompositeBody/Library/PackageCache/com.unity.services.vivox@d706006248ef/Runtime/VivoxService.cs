namespace Unity.Services.Vivox
{
    /// <summary>
    /// Stores the Instance of the VivoxService that is interacted with using the IVivoxService
    /// </summary>
    public static class VivoxService
    {
        /// <summary>
        /// The instance singleton used to access the VivoxService
        /// </summary>
        public static IVivoxService Instance { get; internal set; }
    }
}
