using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Services.DeploymentApi.Editor;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    [Serializable]
    class MatchmakerConfigResource : IDeploymentItem, ITypedItem
    {
        protected string m_Name;
        protected string m_Path;
        protected float m_Progress;
        protected DeploymentStatus m_Status;
        protected string m_Type;

        /// <inheritdoc/>
        public virtual string Name
        {
            get => m_Name;
            set => SetField(ref m_Name, value);
        }

        /// <inheritdoc/>
        public virtual string Path
        {
            get => m_Path;
            set => SetField(ref m_Path, value);
        }

        /// <inheritdoc/>
        public virtual float Progress
        {
            get => m_Progress;
            set => SetField(ref m_Progress, value);
        }

        /// <inheritdoc/>
        public virtual DeploymentStatus Status
        {
            get => m_Status;
            set => SetField(ref m_Status, value);
        }

        /// <inheritdoc/>
        public ObservableCollection<AssetState> States { get; } = new ObservableCollection<AssetState>();

        public IMatchmakerConfig Content { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <inheritdoc/>
        public string Type { get { return m_Type; } set { m_Type = value; } }

        /// <summary>
        /// Sets the field and raises an OnPropertyChanged event.
        /// </summary>
        /// <param name="field">The field to set.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="onFieldChanged">The callback.</param>
        /// <param name="propertyName">Name of the property to set.</param>
        /// <typeparam name="T">Type of the parameter.</typeparam>
        protected void SetField<T>(
            ref T field,
            T value,
            Action<T> onFieldChanged = null,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            OnPropertyChanged(propertyName);
            onFieldChanged?.Invoke(field);
        }

        void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
