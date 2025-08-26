<<<<<<< Updated upstream
﻿using System;
=======
﻿using ImAged.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
<<<<<<< Updated upstream
            // This is where you would put the logic to navigate to the selected folder,
            // or perform some other action.
            Debug.WriteLine($"Folder '{Name}' was clicked!");
=======
            foreach (var folderPath in GetCandidateFolders())
            {
                if (!Directory.Exists(folderPath))
                    continue;

                try
=======
            var searchDirectories = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            foreach (var folderPath in searchDirectories)
            {
                if (!Directory.Exists(folderPath)) continue;

                foreach (var path in Directory.GetFiles(folderPath, "*.ttl", SearchOption.AllDirectories))
>>>>>>> Stashed changes
                {
                    foreach (var path in Directory.EnumerateFiles(folderPath, "*.ttl", SearchOption.AllDirectories))
                    {
<<<<<<< Updated upstream
                        FileInfo info;
                        try
                        {
                            info = new FileInfo(path);
                        }
                        catch
                        {
                            continue;
                        }

                        Files.Add(new FileItem
                        {
                            FileName = info.Name,
                            FileType = info.Extension,
                            FileSize = info.Length / 1024d, // in KB
                            FilePath = info.FullName,
                            Created = info.CreationTime,
                            State = "Converted",
                            ImagePath = "256x256.ico"
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip folders we cannot access
                }
                catch (IOException)
                {
                    // Skip problematic folders
=======
                        FileName = info.Name,
                        FileType = info.Extension,
                        FileSize = info.Length / 1024d, // in KB
                        FilePath = info.FullName,
                        Created = info.CreationTime,
                        State = "Converted",
                        ImagePath = "256x256.ico"
                    });
>>>>>>> Stashed changes
                }
            }
        }

        private IEnumerable<string> GetCandidateFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = string.IsNullOrEmpty(userProfile) ? null : Path.Combine(userProfile, "Downloads");

            if (!string.IsNullOrWhiteSpace(desktop)) folders.Add(desktop);
            if (!string.IsNullOrWhiteSpace(documents)) folders.Add(documents);
            if (!string.IsNullOrWhiteSpace(pictures)) folders.Add(pictures);
            if (!string.IsNullOrWhiteSpace(downloads)) folders.Add(downloads);

            // Also consider the application's base directory
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir)) folders.Add(baseDir);
            }
            catch { }

            return folders;
        }

        private bool FilterFiles(object obj)
        {
            if (obj is FileItem file)
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                    return true;

                return file.FileName.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                       file.FileType.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
>>>>>>> Stashed changes
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
