using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NoesisGodot.Examples;

/// <summary>ViewModel for the themed showcase: TextBox, CheckBox, Slider, ComboBox, ProgressBar.</summary>
public class ThemeLabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string _playerName = "";
    public string PlayerName
    {
        get => _playerName;
        set { if (Set(ref _playerName, value)) Notify(nameof(Greeting)); }
    }

    public string Greeting =>
        string.IsNullOrWhiteSpace(PlayerName) ? "Hello, stranger." : $"Hello, {PlayerName}!";

    private bool _awesomeMode;
    public bool AwesomeMode
    {
        get => _awesomeMode;
        set { if (Set(ref _awesomeMode, value)) Notify(nameof(AwesomeStatus)); }
    }

    public string AwesomeStatus => AwesomeMode ? "Awesome mode: ENGAGED" : "Awesome mode: off";

    private double _volume = 60;
    public double Volume
    {
        get => _volume;
        set { if (Set(ref _volume, value)) Notify(nameof(VolumeLabel)); }
    }

    public string VolumeLabel => $"Volume: {Volume:0}%";

    public List<string> QualityOptions { get; } = ["Low", "Medium", "High", "Ultra"];

    private string _quality = "High";
    public string Quality
    {
        get => _quality;
        set => Set(ref _quality, value);
    }

    public ICommand ResetCommand { get; }

    public ThemeLabViewModel()
    {
        ResetCommand = new DelegateCommand(_ =>
        {
            PlayerName = "";
            AwesomeMode = false;
            Volume = 60;
            Quality = "High";
        });
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Notify(name);
        return true;
    }

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
