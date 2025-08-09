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
        public RelayCommand SettingsViewCommand { get; set; } // SETTINGS
        public RelayCommand ConvertViewCommand { get; set; } // TOOLS / CONVERTER
        public RelayCommand CloseCommand { get; set; }
        public RelayCommand MinimizeCommand { get; set; }

        public HomeViewModel HomeVm  { get; set; }
        public ViewViewModel ViewVm { get; set; }
        public SettingsViewModel SettingVm { get; set; }
        public ConvertViewModel ConvertVm { get; set; }

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

            CurrentView = HomeVm;

            HomeViewCommand = new RelayCommand(o =>
            {
                CurrentView = HomeVm;
            });

            ViewViewCommand = new RelayCommand(o =>
            {
                CurrentView = ViewVm;
            });

            SettingsViewCommand = new RelayCommand(o =>
            {
                CurrentView = SettingVm;
            });

            ConvertViewCommand = new RelayCommand(o =>
            {
                CurrentView = ConvertVm;
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
