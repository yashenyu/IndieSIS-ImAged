using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;

namespace ImAged.MVVM.ViewModel
{
    // The ViewModel must implement INotifyPropertyChanged to notify the UI of property changes
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterFolders(); // Call the filter method when search text changes
            }
        }

        private string _currentTab = "Recent"; // Default tab
        public string CurrentTab
        {
            get { return _currentTab; }
            set
            {
                _currentTab = value;
                OnPropertyChanged();
                FilterFolders(); // Call the filter method when the tab changes
            }
        }

        // Collection holding all folders
        public ObservableCollection<FolderModel> AllFolders { get; set; }

        // The filtered collection that the UI binds to
        public ObservableCollection<FolderModel> Folders { get; set; }

        // Command for changing tabs
        public ICommand ChangeTabCommand { get; set; }

        public MainViewModel()
        {
            // Initialize commands
            ChangeTabCommand = new RelayCommand(ChangeTab);

            // Populate with dummy data for demonstration
            AllFolders = new ObservableCollection<FolderModel>
            {
                new FolderModel { Name = "Project Omega", Info = "3 files", Thumbnail = "C:/path/to/omega.png" },
                new FolderModel { Name = "Family Vacation", Info = "120 files", Thumbnail = "C:/path/to/vacation.png" },
                new FolderModel { Name = "Archived Code", Info = "5 files", IsArchived = true, Thumbnail = "C:/path/to/code.png" },
                new FolderModel { Name = "Work Documents", Info = "35 files", Thumbnail = "C:/path/to/work.png" },
                new FolderModel { Name = "Old Photos", Info = "80 files", IsArchived = true, Thumbnail = "C:/path/to/oldphotos.png" }
            };

            // Initialize the UI-bound collection by filtering
            Folders = new ObservableCollection<FolderModel>();
            FilterFolders();
        }

        private void ChangeTab(object parameter)
        {
            if (parameter is string tabName)
            {
                CurrentTab = tabName;
            }
        }

        private void FilterFolders()
        {
            // First, filter by the selected tab
            IEnumerable<FolderModel> filteredList;
            if (CurrentTab == "Archived")
            {
                filteredList = AllFolders.Where(f => f.IsArchived);
            }
            else
            {
                filteredList = AllFolders.Where(f => !f.IsArchived);
            }

            // Then, apply the search filter if there is a search text
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filteredList = filteredList.Where(f => f.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Update the UI-bound collection
            Folders.Clear();
            foreach (var folder in filteredList)
            {
                Folders.Add(folder);
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // A simple Model class to represent the folder data
    public class FolderModel
    {
        public string Name { get; set; }
        public string Info { get; set; }
        public string Thumbnail { get; set; }
        public bool IsArchived { get; set; }
        public ICommand FolderClickedCommand { get; }

        public FolderModel()
        {
            FolderClickedCommand = new RelayCommand(ExecuteFolderClick);
        }

        private void ExecuteFolderClick(object obj)
        {
            MessageBox.Show($"Folder clicked: {Name}");
            // Add your navigation or other logic here
        }
    }

    // A reusable ICommand implementation for data binding
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