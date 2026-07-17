using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NoesisGodot.Examples;

/// <summary>Exercises two-way bindings (TextBox, CheckBox) and computed properties.</summary>
public class WidgetLabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string _playerName = "";
    public string PlayerName
    {
        get => _playerName;
        set
        {
            if (_playerName != value)
            {
                _playerName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Greeting));
            }
        }
    }

    public string Greeting =>
        string.IsNullOrWhiteSpace(PlayerName) ? "Hello, stranger." : $"Hello, {PlayerName}!";

    private bool _awesomeMode;
    public bool AwesomeMode
    {
        get => _awesomeMode;
        set
        {
            if (_awesomeMode != value)
            {
                _awesomeMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AwesomeStatus));
            }
        }
    }

    public string AwesomeStatus =>
        AwesomeMode ? "Awesome mode: ENGAGED" : "Awesome mode: off";

    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
