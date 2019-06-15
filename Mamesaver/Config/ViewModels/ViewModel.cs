﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mamesaver.Properties;

namespace Mamesaver.Config.ViewModels
{
    /// <inheritdoc />
    /// <summary>
    ///     Base class for view models, supporting property changed events.
    /// </summary>
    public abstract class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnAllPropertiesChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }
    }
}