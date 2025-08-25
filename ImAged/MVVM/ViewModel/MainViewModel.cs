using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ImAged.Core;

namespace ImAged.MVVM.ViewModel
{ 
    class MainViewModel : ObservableObject
    {
        public RelayCommand HomeViewCommand { get; set; } // HOME
        public RelayCommand ViewViewCommand { get; set; } // FAVORITES
        public RelayCommand FileViewCommand { get; set; } // ALL FILES
        public RelayCommand ConvertViewCommand { get; set; } // TOOLS / CONVERTER
        public RelayCommand SettingsViewCommand { get; set; } // SETTINGS

        public RelayCommand CloseCommand { get; set; }
        public RelayCommand MinimizeCommand { get; set; }

        public HomeViewModel HomeVm  { get; set; }
        public ViewViewModel ViewVm { get; set; }
        public FileViewModel FileVm { get; set; }
        public ConvertViewModel ConvertVm { get; set; }
        public SettingsViewModel SettingVm { get; set; }

        private object _currentView;

        public object CurrentView
        {
            get { return _currentView; }
            set 
            { 
                _currentView = value;
                onPropertyChanged();
            }
        }

        public MainViewModel()
        {
            HomeVm = new HomeViewModel();
            ViewVm = new ViewViewModel();
            SettingVm = new SettingsViewModel();
            ConvertVm = new ConvertViewModel();
            FileVm = new FileViewModel();

            CurrentView = HomeVm;

            HomeViewCommand = new RelayCommand(o =>
            {
                CurrentView = HomeVm;
            });

            // Call the initialization method for ViewVm
            ViewViewCommand = new RelayCommand(async o =>
            {
                CurrentView = ViewVm;
                await ViewVm.InitializeFoldersAsync(); // <-- This line is the key
            });
            
            FileViewCommand = new RelayCommand(o =>
            {
                CurrentView = FileVm;
            });

            ConvertViewCommand = new RelayCommand(o =>
            {
                CurrentView = ConvertVm;
            });

            SettingsViewCommand = new RelayCommand(o =>
            {
                CurrentView = SettingVm;
            });

            CloseCommand = new RelayCommand(o =>
            {
                if (o is Window window)
                    window.Close();
            });

            MinimizeCommand = new RelayCommand(o =>
            {
                if (o is Window window)
                    window.WindowState = WindowState.Minimized;
            });
        }
    }
}