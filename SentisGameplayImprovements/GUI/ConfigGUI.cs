using System.Windows.Controls;
using SentisGameplayImprovements;

namespace SOPlugin.GUI
{
    partial class ConfigGUI : UserControl
    {
        public ConfigGUI()
        {
            InitializeComponent();
            MainFilteredGrid.DataContext = SentisGameplayImprovementsPlugin.Config;
        }
    }
}