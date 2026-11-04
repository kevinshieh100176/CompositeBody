namespace CompositeBody.Interaction.Diagnostics
{
    /// <summary>
    /// Module-specific buttons for <see cref="DesktopTestHud"/>. Implement on a MonoBehaviour in
    /// a manual-test scene; the HUD finds every implementer and draws each in its own section,
    /// inside its IMGUI window, so the implementation just calls GUILayout.
    /// </summary>
    public interface IDesktopTestActions
    {
        /// <summary>Section heading. ASCII.</summary>
        string desktopTestTitle { get; }

        /// <summary>Draw the section with GUILayout. Called from OnGUI; keep it cheap.</summary>
        void OnDesktopTestGUI();
    }
}
