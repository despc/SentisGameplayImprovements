using System.Windows.Controls;
using SentisGameplayImprovements;

namespace SOPlugin.GUI
{
    public class ConfigGUI : UserControl
    {
        // Previously generated from ConfigGUI.xaml; now built in managed code.
        internal FilteredGrid MainFilteredGrid;

        public ConfigGUI()
        {
            BuildUi();
            MainFilteredGrid.DataContext = SentisGameplayImprovementsPlugin.Config;
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
