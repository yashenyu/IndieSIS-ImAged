using System;
using System.Collections.ObjectModel;
using ImAged.MVVM.Model;

namespace ImAged.MVVM.ViewModel
{
    internal class SettingsViewModel
    {
        public ObservableCollection<Developer> Developers { get; } = new ObservableCollection<Developer>();

        public SettingsViewModel()
        {
            // Seed with your four developers. Update ImagePath to correct images.
            Developers.Add(new Developer
            {
                Name = "Dayrit, Mark Aaron B.",
                Role = "Full Stack Developer",
                School = "Holy Angel University",
                ImagePath = "/Images/Mark.png"
            });

            Developers.Add(new Developer
            {
                Name = "Pangan, Ric Christian B.",
                Role = "Front-End Developer",
                School = "Holy Angel University",
                ImagePath = "/Images/Ric.jpg"
            });

            Developers.Add(new Developer
            {
                Name = "Quizon, Andre Thomas M.",
                Role = "Project Manager",
                School = "Holy Angel University",
                ImagePath = "/Images/dre.jpg"
            });

            Developers.Add(new Developer
            {
                Name = "Siron, Carlo S.",
                Role = "UI/UX Designer Front-end Developer",
                School = "Holy Angel University",
                ImagePath = "/Images/base logo.png"
            });
        }
    }
}
