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
        public RelayCommand HomeViewCommand { get; set; }
        public RelayCommand ViewViewCommand { get; set; } //FAVORITES
        public RelayCommand SettingsViewCommand { get; set; }
        public RelayCommand CloseCommand { get; set; }
        public RelayCommand MinimizeCommand { get; set; }

        public HomeViewModel HomeVm  { get; set; }
        public ViewViewModel ViewVm { get; set; }

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
            //SettingVm = new 
            CurrentView = HomeVm;

            HomeViewCommand = new RelayCommand(o =>
            {
                CurrentView = HomeVm;
            });

            ViewViewCommand = new RelayCommand(o =>
            {
                CurrentView = ViewVm;
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
