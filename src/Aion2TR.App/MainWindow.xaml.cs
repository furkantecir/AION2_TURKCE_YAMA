using System.Windows;
using System.Windows.Media.Animation;
using Aion2TR.App.ViewModels;

namespace Aion2TR.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ShowIntroAnimation)
        {
            if (FindResource("StartupIntroStoryboard") is Storyboard sb)
            {
                sb.Completed += (s, ev) =>
                {
                    IntroOverlay.Visibility = Visibility.Collapsed;
                };
                sb.Begin();
            }
            else
            {
                IntroOverlay.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            IntroOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}