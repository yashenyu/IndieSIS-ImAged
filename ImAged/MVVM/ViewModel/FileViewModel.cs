using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Diagnostics;

namespace ImAged.MVVM.ViewModel
{
    // This ViewModel represents a single folder item in the ItemsControl.
    public class FileViewModel : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _info;
        public string Info
        {
            get => _info;
            set
            {
                if (_info != value)
                {
                    _info = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _thumbnail;
        public string Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged();
                }
            }
        }

        // This command can be used to handle a click event on the folder.
        public ICommand FolderClickedCommand { get; }

        public FileViewModel()
        {
            // The RelayCommand now takes a method to execute (OpenFolder)
            // and an optional canExecute parameter.
            FolderClickedCommand = new RelayCommand(param => OpenFolder());
        }

        private void OpenFolder()
        {
            // This is where you would put the logic to navigate to the selected folder,
            // or perform some other action.
            Debug.WriteLine($"Folder '{Name}' was clicked!");
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    // A simple ICommand implementation to handle button clicks.
    // This is a generic version that can handle commands with and without a parameter.
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);
    }
}
