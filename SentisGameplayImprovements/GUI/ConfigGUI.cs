using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SOPlugin.GUI
{
    public class ConfigGUI : UserControl
    {
        // Previously generated from ConfigGUI.xaml; now built in managed code.
        internal FilteredGrid MainFilteredGrid;

        public ConfigGUI()
        {
            BuildUi();
            MainFilteredGrid.DataContext = SentisGameplayImprovements.SentisGameplayImprovementsPlugin.Config;
        }

        private void BuildUi()
        {
            var root = new StackPanel { Orientation = Orientation.Vertical };

            MainFilteredGrid = new FilteredGrid();
            root.Children.Add(MainFilteredGrid);

            Content = root;
        }
    }
}
